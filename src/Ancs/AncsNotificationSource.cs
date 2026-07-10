using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.Extensions.Logging;
using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace PhoneNotificationsVR.Ancs;

/// <summary>
/// Production notification source: a Bluetooth-LE ANCS consumer that receives every notification
/// from a paired iPhone. No iOS app is involved – this is Apple's own sanctioned mechanism (the same
/// one the Apple Watch uses).
///
/// Lifecycle &amp; reliability:
///  * <see cref="StartAsync"/> spins a supervisor loop that (re)establishes the GATT connection and
///    keeps it alive. Any drop – phone out of range, Bluetooth toggled, Windows waking from sleep –
///    is caught and the loop retries with backoff. This satisfies the "auto-recover" requirements.
///  * The iPhone must be Bluetooth-<b>paired</b> with the PC once (Windows Settings ▸ Bluetooth).
///    After that, ANCS becomes available whenever the phone is in range and unlocked.
/// </summary>
public sealed class AncsNotificationSource : INotificationSource
{
    private readonly ILogger<AncsNotificationSource> _log;

    // Selected phone. Set via SetTargetDevice from persisted settings, or discovered automatically.
    private string? _targetDeviceId;

    private BluetoothLEDevice? _device;
    private GattDeviceService? _ancsService;
    private GattCharacteristic? _notificationSource;
    private GattCharacteristic? _controlPoint;
    private GattCharacteristic? _dataSource;

    private readonly AncsDataSourceAssembler _assembler = new();
    private readonly ConcurrentDictionary<string, string> _appNameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<uint, byte> _pendingCategories = new();

    private CancellationTokenSource? _cts;
    private Task? _supervisor;
    private readonly SemaphoreSlim _controlPointGate = new(1, 1);

    public event EventHandler<PhoneNotification>? NotificationReceived;
    public event EventHandler<uint>? NotificationRemoved;
    public event EventHandler<ConnectionStatus>? StatusChanged;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected();

    public AncsNotificationSource(ILogger<AncsNotificationSource> log) => _log = log;

    /// <summary>Choose which paired phone to connect to (Bluetooth device id from settings).</summary>
    public void SetTargetDevice(string? deviceId) => _targetDeviceId = deviceId;

    /// <summary>Enumerate paired BLE devices that advertise the ANCS service, for the settings UI picker.</summary>
    public static async Task<IReadOnlyList<(string Id, string Name)>> FindPairedPhonesAsync()
    {
        // Query paired BLE devices; the iPhone shows up once bonded with the PC.
        var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
        var devices = await DeviceInformation.FindAllAsync(selector);
        return devices.Select(d => (d.Id, d.Name)).ToList();
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _supervisor = Task.Run(() => SuperviseAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is not null) await _cts.CancelAsync();
        if (_supervisor is not null)
        {
            try { await _supervisor; } catch { /* logged inside */ }
        }
        await TeardownAsync();
        SetStatus(ConnectionState.Disconnected, "Stopped");
    }

    /// <summary>Supervisor loop: connect, then watch; on any failure, back off and reconnect.</summary>
    private async Task SuperviseAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(2);
        var maxBackoff = TimeSpan.FromSeconds(30);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetStatus(ConnectionState.Connecting, "Connecting to iPhone over Bluetooth…");
                await ConnectAsync(ct);
                SetStatus(ConnectionState.Connected, $"Connected to {_device?.Name ?? "iPhone"}");
                backoff = TimeSpan.FromSeconds(2); // reset backoff on success

                // Stay connected until the link drops. The device's ConnectionStatusChanged event
                // completes this TCS, which unblocks us and triggers a reconnect.
                var disconnected = new TaskCompletionSource();
                void OnConn(BluetoothLEDevice s, object _)
                {
                    if (s.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                        disconnected.TrySetResult();
                }
                if (_device is not null) _device.ConnectionStatusChanged += OnConn;

                using (ct.Register(() => disconnected.TrySetResult()))
                    await disconnected.Task;

                if (_device is not null) _device.ConnectionStatusChanged -= OnConn;
                if (ct.IsCancellationRequested) break;

                _log.LogWarning("iPhone link dropped – will reconnect.");
                SetStatus(ConnectionState.Reconnecting, "Link lost, reconnecting…");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "ANCS connect failed; retrying in {Backoff}", backoff);
                SetStatus(ConnectionState.Reconnecting, $"Retrying in {backoff.TotalSeconds:0}s… ({ex.Message})");
            }

            await TeardownAsync();

            try { await Task.Delay(backoff, ct); } catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, maxBackoff.Ticks));
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        var deviceId = _targetDeviceId ?? await DiscoverPhoneAsync();
        if (deviceId is null)
            throw new InvalidOperationException("No paired iPhone found. Pair the phone in Windows Bluetooth settings first.");

        _device = await BluetoothLEDevice.FromIdAsync(deviceId)
            ?? throw new InvalidOperationException("Could not open the Bluetooth device.");

        // Resolve the ANCS service. Uncached forces a fresh GATT discovery after reconnect.
        var servicesResult = await _device.GetGattServicesForUuidAsync(AncsUuids.Service, BluetoothCacheMode.Uncached);
        if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
            throw new InvalidOperationException(
                "ANCS service not exposed. Make sure the iPhone is unlocked and has granted notification " +
                "access to this PC (a prompt appears on the phone the first time).");

        _ancsService = servicesResult.Services[0];

        _notificationSource = await GetCharacteristicAsync(AncsUuids.NotificationSource, ct);
        _controlPoint = await GetCharacteristicAsync(AncsUuids.ControlPoint, ct);
        _dataSource = await GetCharacteristicAsync(AncsUuids.DataSource, ct);

        // Subscribe to Data Source first so attribute responses are never missed.
        _dataSource.ValueChanged += OnDataSourceValueChanged;
        await SubscribeAsync(_dataSource);

        _notificationSource.ValueChanged += OnNotificationSourceValueChanged;
        await SubscribeAsync(_notificationSource);
    }

    private async Task<GattCharacteristic> GetCharacteristicAsync(Guid uuid, CancellationToken ct)
    {
        var result = await _ancsService!.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached);
        if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
            throw new InvalidOperationException($"ANCS characteristic {uuid} not found.");
        return result.Characteristics[0];
    }

    private static async Task SubscribeAsync(GattCharacteristic c)
    {
        var status = await c.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify);
        if (status != GattCommunicationStatus.Success)
            throw new InvalidOperationException($"Failed to subscribe to {c.Uuid} ({status}).");
    }

    /// <summary>Best-effort auto-discovery: pick the first paired device exposing ANCS.</summary>
    private static async Task<string?> DiscoverPhoneAsync()
    {
        var phones = await FindPairedPhonesAsync();
        return phones.Count > 0 ? phones[0].Id : null;
    }

    // ---- Notification Source: 8-byte add/modify/remove announcements -------------------------

    private void OnNotificationSourceValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            var data = ToBytes(args.CharacteristicValue);
            var packet = AncsNotificationSourcePacket.Parse(data);

            if (packet.EventId == AncsEventId.NotificationRemoved)
            {
                NotificationRemoved?.Invoke(this, packet.NotificationUid);
                return;
            }

            // Remember the category so we can classify once the text attributes come back.
            _pendingCategories[packet.NotificationUid] = packet.CategoryId;

            // Skip pre-existing notifications that were already on the phone before we connected,
            // otherwise we would replay a burst of stale cards on every reconnect.
            if (packet.Flags.HasFlag(AncsEventFlags.PreExisting))
            {
                _pendingCategories.TryRemove(packet.NotificationUid, out _);
                return;
            }

            _ = RequestAttributesAsync(packet.NotificationUid);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to handle Notification Source packet.");
        }
    }

    private async Task RequestAttributesAsync(uint uid)
    {
        try
        {
            var payload = AncsRequestBuilder.GetNotificationAttributes(uid);
            await WriteControlPointAsync(payload);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to request attributes for notification {Uid}", uid);
        }
    }

    private async Task WriteControlPointAsync(byte[] payload)
    {
        // Serialise control-point writes; the phone answers one request at a time on Data Source.
        await _controlPointGate.WaitAsync();
        try
        {
            if (_controlPoint is null) return;
            using var writer = new DataWriter();
            writer.WriteBytes(payload);
            await _controlPoint.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithResponse);
        }
        finally { _controlPointGate.Release(); }
    }

    // ---- Data Source: streamed attribute responses -------------------------------------------

    private void OnDataSourceValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            var data = ToBytes(args.CharacteristicValue);
            if (data.Length == 0) return;

            // Byte 0 is the CommandID – route app-attribute responses separately.
            if (data[0] == (byte)AncsCommandId.GetAppAttributes)
            {
                HandleAppAttributes(data);
                return;
            }

            var attrs = _assembler.Push(data);
            if (attrs is not null)
                _ = FinishNotificationAsync(attrs);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to handle Data Source fragment.");
            _assembler.Reset();
        }
    }

    private async Task FinishNotificationAsync(AncsNotificationAttributes attrs)
    {
        byte categoryId = _pendingCategories.TryRemove(attrs.Uid, out var c) ? c : (byte)0;
        var category = (NotificationCategory)categoryId;

        var appName = await ResolveAppNameAsync(attrs.AppIdentifier);

        var notification = new PhoneNotification
        {
            Uid = attrs.Uid,
            AppIdentifier = attrs.AppIdentifier,
            AppName = appName,
            Title = attrs.Title,
            Subtitle = attrs.Subtitle,
            Message = attrs.Message,
            Category = category,
            Timestamp = attrs.Date ?? DateTimeOffset.Now,
        };

        NotificationReceived?.Invoke(this, notification);
    }

    /// <summary>Resolve a human name for the app, caching results. Falls back to the KnownApps table.</summary>
    private async Task<string> ResolveAppNameAsync(string appId)
    {
        if (string.IsNullOrEmpty(appId)) return string.Empty;
        if (_appNameCache.TryGetValue(appId, out var cached)) return cached;

        // Ask the phone for the display name; the answer arrives on Data Source (command 1).
        try { await WriteControlPointAsync(AncsRequestBuilder.GetAppAttributes(appId)); }
        catch (Exception ex) { _log.LogDebug(ex, "GetAppAttributes failed for {App}", appId); }

        // Use the friendly fallback immediately; the cache updates for next time when the answer lands.
        var friendly = KnownApps.Friendly(appId);
        _appNameCache.TryAdd(appId, friendly);
        return friendly;
    }

    private void HandleAppAttributes(byte[] data)
    {
        // Layout: CommandID(1) AppId(NUL-terminated) then { AttrID(1) Len(2) Value(Len) }.
        int nul = Array.IndexOf(data, (byte)0, 1);
        if (nul < 0) return;
        var appId = Encoding.UTF8.GetString(data, 1, nul - 1);
        int offset = nul + 1;
        if (offset + 3 > data.Length) return;

        ushort len = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 1, 2));
        int valueStart = offset + 3;
        if (valueStart + len > data.Length) return;

        var displayName = Encoding.UTF8.GetString(data, valueStart, len);
        if (!string.IsNullOrWhiteSpace(displayName))
            _appNameCache[appId] = displayName; // authoritative name for next notifications
    }

    private static byte[] ToBytes(IBuffer buffer)
    {
        var bytes = new byte[buffer.Length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private async Task TeardownAsync()
    {
        try
        {
            if (_notificationSource is not null) _notificationSource.ValueChanged -= OnNotificationSourceValueChanged;
            if (_dataSource is not null) _dataSource.ValueChanged -= OnDataSourceValueChanged;
            _ancsService?.Dispose();
            _device?.Dispose();
        }
        catch (Exception ex) { _log.LogDebug(ex, "Teardown error (ignored)."); }
        finally
        {
            _notificationSource = _controlPoint = _dataSource = null;
            _ancsService = null;
            _device = null;
            _assembler.Reset();
            _pendingCategories.Clear();
            await Task.CompletedTask;
        }
    }

    private void SetStatus(ConnectionState state, string detail)
    {
        Status = new ConnectionStatus(state, detail);
        StatusChanged?.Invoke(this, Status);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _controlPointGate.Dispose();
        _cts?.Dispose();
    }
}
