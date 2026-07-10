using Microsoft.Extensions.Logging;
using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Models;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace PhoneNotificationsVR.Listener;

/// <summary>
/// Notification source backed by the Windows <see cref="UserNotificationListener"/>.
///
/// Where the notifications come from:
///  * <b>iPhone</b> notifications (calls, iMessage/SMS, WhatsApp, …) arrive in the Windows notification
///    centre via <b>Microsoft Phone Link</b>, which owns the finicky iPhone↔Windows Bluetooth/ANCS bridge.
///  * <b>Desktop apps</b> (Discord, WhatsApp, Telegram, Slack, …) post to the notification centre directly.
///
/// We read them all here and normalise into <see cref="PhoneNotification"/>, so the rest of the app
/// (queue, overlay, settings, preview, history) is completely unchanged.
///
/// Reliability:
///  * The listener's push event is unreliable for foreground Win32 apps, so we <b>poll</b> on a short
///    interval and diff against the ids we have already seen. Polling a handful of toasts is very cheap.
///  * A supervisor loop re-requests access and keeps going across sleep / access changes.
///
/// Requirements:
///  * The process must have <b>package identity</b> (see docs/IDENTITY.md). Without it,
///    <see cref="UserNotificationListener.RequestAccessAsync"/> throws; we surface a clear status message.
///  * The user must grant notification access once (Windows shows a consent prompt / Settings ▸ Privacy
///    ▸ Notifications).
/// </summary>
public sealed class WindowsNotificationListenerSource : INotificationSource
{
    private readonly ILogger<WindowsNotificationListenerSource> _log;
    private readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(1500);

    // Ids we have already emitted, so each notification fires exactly once.
    private readonly HashSet<uint> _seen = new();
    private bool _primed;

    private CancellationTokenSource? _cts;
    private Task? _supervisor;

    public event EventHandler<PhoneNotification>? NotificationReceived;
    public event EventHandler<uint>? NotificationRemoved;
    public event EventHandler<ConnectionStatus>? StatusChanged;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected();

    public WindowsNotificationListenerSource(ILogger<WindowsNotificationListenerSource> log) => _log = log;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _supervisor = Task.Run(() => SuperviseAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is not null) await _cts.CancelAsync();
        if (_supervisor is not null) { try { await _supervisor; } catch { } }
        SetStatus(ConnectionState.Disconnected, "Stopped");
    }

    private async Task SuperviseAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetStatus(ConnectionState.Connecting, "Requesting notification access…");

                UserNotificationListener listener;
                UserNotificationListenerAccessStatus access;
                try
                {
                    listener = UserNotificationListener.Current;
                    access = await listener.RequestAccessAsync();
                }
                catch (Exception ex)
                {
                    // Almost always: the process lacks package identity (see docs/IDENTITY.md).
                    _log.LogError(ex, "UserNotificationListener unavailable (package identity missing?)");
                    SetStatus(ConnectionState.Faulted,
                        "Notification listener needs package identity — run the one-time sparse-package setup (docs/IDENTITY.md).");
                    await DelaySafe(TimeSpan.FromSeconds(10), ct);
                    continue;
                }

                if (access != UserNotificationListenerAccessStatus.Allowed)
                {
                    SetStatus(ConnectionState.Faulted,
                        "Notification access denied. Enable it in Windows Settings ▸ Privacy & security ▸ Notifications.");
                    await DelaySafe(TimeSpan.FromSeconds(15), ct);
                    continue;
                }

                SetStatus(ConnectionState.Connected, "Reading Windows notifications (Phone Link + desktop apps)");
                await PollLoopAsync(listener, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Listener loop error; retrying.");
                SetStatus(ConnectionState.Reconnecting, $"Listener error, retrying… ({ex.Message})");
                await DelaySafe(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task PollLoopAsync(UserNotificationListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            IReadOnlyList<UserNotification> current;
            try
            {
                current = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            }
            catch (Exception ex)
            {
                // Access can be revoked at runtime; bubble up to the supervisor to re-request.
                _log.LogWarning(ex, "GetNotificationsAsync failed; re-establishing access.");
                throw;
            }

            var currentIds = new HashSet<uint>();
            foreach (var un in current)
            {
                currentIds.Add(un.Id);
                if (_seen.Add(un.Id))
                {
                    // On the very first poll, absorb existing notifications without replaying them as
                    // a burst of stale cards — mirrors the ANCS "pre-existing" handling.
                    if (_primed)
                        EmitSafe(un);
                }
            }

            // Detect removals (notification dismissed on phone/PC), e.g. a call was answered.
            foreach (var goneId in _seen.Where(id => !currentIds.Contains(id)).ToList())
            {
                _seen.Remove(goneId);
                NotificationRemoved?.Invoke(this, goneId);
            }

            _primed = true;
            await DelaySafe(_pollInterval, ct);
        }
    }

    private void EmitSafe(UserNotification un)
    {
        try
        {
            var n = Map(un);
            if (n is not null)
                NotificationReceived?.Invoke(this, n);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Failed to map notification {Id}", un.Id);
        }
    }

    /// <summary>Normalise a Windows <see cref="UserNotification"/> into our domain model.</summary>
    private static PhoneNotification? Map(UserNotification un)
    {
        string appName = "Notification";
        string appId = string.Empty;
        try
        {
            var info = un.AppInfo?.DisplayInfo;
            if (info is not null) appName = info.DisplayName;
            appId = un.AppInfo?.AppUserModelId ?? string.Empty;
        }
        catch { /* some system notifications have no AppInfo */ }

        string title = string.Empty;
        string message = string.Empty;

        var binding = un.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
        if (binding is not null)
        {
            var texts = binding.GetTextElements();
            if (texts.Count > 0) title = texts[0].Text ?? string.Empty;
            if (texts.Count > 1) message = string.Join("\n", texts.Skip(1).Select(t => t.Text));
        }

        // Nothing worth showing (e.g. a progress toast with no text).
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(message))
            return null;

        var category = Classify(appName, appId, title, message);

        return new PhoneNotification
        {
            Uid = un.Id,
            AppIdentifier = string.IsNullOrEmpty(appId) ? appName : appId,
            AppName = appName,
            Title = title,
            Message = message,
            Category = category,
            Timestamp = un.CreationTime,
        };
    }

    /// <summary>
    /// Best-effort category detection. The Windows listener does not expose ANCS-style categories, so we
    /// infer calls from the text (Phone Link renders an incoming call as a toast such as "Incoming call").
    /// </summary>
    private static NotificationCategory Classify(string appName, string appId, string title, string message)
    {
        string haystack = $"{title} {message}".ToLowerInvariant();
        if (haystack.Contains("missed call")) return NotificationCategory.MissedCall;
        if (haystack.Contains("incoming call") || haystack.Contains("is calling") || haystack.Contains(" calling"))
            return NotificationCategory.IncomingCall;
        return NotificationCategory.Social;
    }

    private static async Task DelaySafe(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { }
    }

    private void SetStatus(ConnectionState state, string detail)
    {
        Status = new ConnectionStatus(state, detail);
        StatusChanged?.Invoke(this, Status);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}
