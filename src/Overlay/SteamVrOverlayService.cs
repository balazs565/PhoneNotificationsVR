using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Models;
using PhoneNotificationsVR.Core.Settings;
using Valve.VR;

namespace PhoneNotificationsVR.Overlay;

/// <summary>
/// SteamVR overlay implementation using the OpenVR API.
///
/// Reliability:
///  * Runs as an OpenVR *overlay app*, so it coexists with any running game.
///  * A supervisor keeps OpenVR initialised; if SteamVR quits or restarts, init is retried until it
///    comes back, and the overlay is recreated automatically.
///
/// Performance:
///  * The card bitmap is uploaded to the overlay texture ONCE per notification. The fade + slide
///    animation only updates the (cheap) alpha and transform each frame, so CPU/GPU cost is minimal.
///  * When idle the overlay is hidden and no work is done at all.
/// </summary>
public sealed class SteamVrOverlayService : IOverlayService
{
    private const string OverlayKey = "phonenotificationsvr.card";
    private const string OverlayName = "Phone Notifications";

    private readonly INotificationRenderer _renderer;
    private readonly ISettingsService _settings;
    private readonly ILogger<SteamVrOverlayService> _log;

    private ulong _handle = OpenVR.k_ulOverlayHandleInvalid;
    private bool _initialised;
    private readonly SemaphoreSlim _showGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _supervisor;

    public event EventHandler<ConnectionStatus>? StatusChanged;
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected();

    public SteamVrOverlayService(INotificationRenderer renderer, ISettingsService settings, ILogger<SteamVrOverlayService> log)
    {
        _renderer = renderer;
        _settings = settings;
        _log = log;
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
        if (_supervisor is not null) { try { await _supervisor; } catch { } }
        Teardown();
        SetStatus(ConnectionState.Disconnected, "Stopped");
    }

    /// <summary>Keep OpenVR alive; re-init after a SteamVR restart; watch for the Quit event.</summary>
    private async Task SuperviseAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!_initialised)
            {
                // IMPORTANT: only touch OpenVR when SteamVR is already running. Initialising with an
                // Overlay/Scene app type would *launch* SteamVR — and, because this loop retries, it would
                // relaunch it every time the user closes it. We attach to SteamVR, we never start it.
                if (!IsSteamVrRunning()) SetStatus(ConnectionState.Reconnecting, "Waiting for SteamVR…");
                else if (TryInit()) SetStatus(ConnectionState.Connected, "SteamVR overlay ready");
                else SetStatus(ConnectionState.Reconnecting, "Waiting for SteamVR…");
            }

            // Pump OpenVR events so we notice SteamVR shutting down and recover cleanly.
            if (_initialised) PumpEvents();

            try { await Task.Delay(TimeSpan.FromSeconds(_initialised ? 1 : 3), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// True when SteamVR is already running. Checked via its processes (side-effect free) rather than by
    /// calling OpenVR, because initialising OpenVR with an Overlay/Scene app type would <b>launch</b> SteamVR.
    /// </summary>
    private static bool IsSteamVrRunning()
    {
        foreach (var name in new[] { "vrserver", "vrmonitor" })
        {
            var procs = Process.GetProcessesByName(name);
            try { if (procs.Length > 0) return true; }
            finally { foreach (var p in procs) p.Dispose(); }
        }
        return false;
    }

    private bool TryInit()
    {
        try
        {
            var err = EVRInitError.None;
            OpenVR.Init(ref err, EVRApplicationType.VRApplication_Overlay);
            if (err != EVRInitError.None)
            {
                _log.LogDebug("OpenVR init not ready: {Error}", err);
                return false;
            }

            var overlay = OpenVR.Overlay;
            if (overlay is null) return false;

            var createErr = overlay.CreateOverlay(OverlayKey, OverlayName, ref _handle);
            if (createErr != EVROverlayError.None)
            {
                _log.LogError("CreateOverlay failed: {Error}", createErr);
                OpenVR.Shutdown();
                return false;
            }

            overlay.SetOverlayColor(_handle, 1f, 1f, 1f);
            _initialised = true;
            _log.LogInformation("SteamVR overlay created.");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "OpenVR init threw (SteamVR probably not running yet).");
            return false;
        }
    }

    private void PumpEvents()
    {
        try
        {
            var overlay = OpenVR.Overlay;
            var ev = new VREvent_t();
            uint size = (uint)Marshal.SizeOf<VREvent_t>();
            while (overlay is not null && overlay.PollNextOverlayEvent(_handle, ref ev, size))
            {
                if ((EVREventType)ev.eventType == EVREventType.VREvent_Quit)
                {
                    _log.LogWarning("SteamVR is quitting – tearing down overlay.");
                    // AcknowledgeQuit_Exiting exists on older bindings only – call it if present.
                    TryInvokeOptional(OpenVR.System, "AcknowledgeQuit_Exiting");
                    Teardown();
                    SetStatus(ConnectionState.Reconnecting, "SteamVR closed, waiting…");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Event pump error – assuming SteamVR went away.");
            Teardown();
        }
    }

    public async Task ShowAsync(PhoneNotification notification, CancellationToken cancellationToken = default)
    {
        if (!_initialised)
        {
            _log.LogDebug("Overlay not ready; dropping card from {App}", notification.AppIdentifier);
            return;
        }

        await _showGate.WaitAsync(cancellationToken);
        try
        {
            var o = _settings.Current.Overlay;
            var overlay = OpenVR.Overlay!;

            // Upload the texture once.
            var card = _renderer.Render(notification);
            UploadTexture(overlay, card);

            overlay.SetOverlayWidthInMeters(_handle, (float)o.WidthMeters);
            TrySetCurvature(overlay, (float)o.Curvature);
            overlay.ShowOverlay(_handle);

            double anim = Math.Max(0.05, o.AnimationSeconds);
            float slideFrom = 0.10f; // start 10cm above, slide into place

            // Fade + slide in.
            await AnimateAsync(overlay, o, tNorm =>
            {
                double eased = EaseOut(tNorm);
                return ((float)(eased * o.Opacity), (float)(slideFrom * (1 - eased)));
            }, anim, cancellationToken);

            // Hold at full opacity for the configured duration.
            ApplyFrame(overlay, o, (float)o.Opacity, 0f);
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.2, o.DurationSeconds)), cancellationToken); }
            catch (OperationCanceledException) { /* skip → fall through to fade out */ }

            // Fade + slide out.
            await AnimateAsync(overlay, o, tNorm =>
            {
                double eased = EaseIn(tNorm);
                return ((float)((1 - eased) * o.Opacity), (float)(slideFrom * eased * -0.5f));
            }, anim, CancellationToken.None);

            overlay.HideOverlay(_handle);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ShowAsync failed.");
        }
        finally
        {
            _showGate.Release();
        }
    }

    private async Task AnimateAsync(CVROverlay overlay, Core.Settings.OverlaySettings o,
        Func<double, (float alpha, float slide)> frame, double seconds, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            if (ct.IsCancellationRequested) return;
            double tNorm = Math.Clamp(sw.Elapsed.TotalSeconds / seconds, 0, 1);
            var (alpha, slide) = frame(tNorm);
            ApplyFrame(overlay, o, alpha, slide);
            try { await Task.Delay(11, ct); } catch (OperationCanceledException) { return; } // ~90 fps
        }
    }

    private void ApplyFrame(CVROverlay overlay, Core.Settings.OverlaySettings o, float alpha, float slide)
    {
        overlay.SetOverlayAlpha(_handle, alpha);

        var matrix = OverlayPositioner.BaseTransform(o, slide);
        if (OverlayPositioner.IsAbsolute(o))
        {
            overlay.SetOverlayTransformAbsolute(_handle, ETrackingUniverseOrigin.TrackingUniverseStanding, ref matrix);
        }
        else
        {
            uint device = OverlayPositioner.DeviceIndexFor(o);
            overlay.SetOverlayTransformTrackedDeviceRelative(_handle, device, ref matrix);
        }
    }

    private void UploadTexture(CVROverlay overlay, RenderedCard card)
    {
        // Pin the RGBA buffer and hand OpenVR a raw pointer (bytesPerPixel = 4).
        var handle = GCHandle.Alloc(card.Rgba, GCHandleType.Pinned);
        try
        {
            var ptr = handle.AddrOfPinnedObject();
            var err = overlay.SetOverlayRaw(_handle, ptr, (uint)card.Width, (uint)card.Height, 4);
            if (err != EVROverlayError.None)
                _log.LogWarning("SetOverlayRaw returned {Error}", err);
        }
        finally { handle.Free(); }
    }

    private void TrySetCurvature(CVROverlay overlay, float curvature)
    {
        // SetOverlayCurvature only exists on newer OpenVR bindings. Invoke reflectively so the
        // project compiles and runs against whichever openvr_api version OVRSharp bundles.
        try
        {
            var m = overlay.GetType().GetMethod("SetOverlayCurvature", new[] { typeof(ulong), typeof(float) });
            m?.Invoke(overlay, new object[] { _handle, Math.Clamp(curvature, 0f, 1f) });
        }
        catch { /* curvature is optional */ }
    }

    /// <summary>Invoke a parameterless method by name if the binding version exposes it.</summary>
    private static void TryInvokeOptional(object? target, string method)
    {
        if (target is null) return;
        try { target.GetType().GetMethod(method, Type.EmptyTypes)?.Invoke(target, null); }
        catch { /* not present on this binding */ }
    }

    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
    private static double EaseIn(double t) => t * t * t;

    private void Teardown()
    {
        try
        {
            if (_handle != OpenVR.k_ulOverlayHandleInvalid)
                OpenVR.Overlay?.DestroyOverlay(_handle);
        }
        catch { }
        _handle = OpenVR.k_ulOverlayHandleInvalid;
        if (_initialised)
        {
            try { OpenVR.Shutdown(); } catch { }
        }
        _initialised = false;
    }

    private void SetStatus(ConnectionState state, string detail)
    {
        Status = new ConnectionStatus(state, detail);
        StatusChanged?.Invoke(this, Status);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _showGate.Dispose();
        _cts?.Dispose();
    }
}
