using PhoneNotificationsVR.Core.Models;

namespace PhoneNotificationsVR.Core.Abstractions;

/// <summary>
/// Abstraction over the SteamVR overlay. The concrete implementation talks to OpenVR; a headless
/// stub is used when SteamVR is not running so the desktop app still works for configuration.
/// The service owns a single reusable overlay and shows one notification at a time (the dispatcher
/// serialises the queue), animating fade + slide in/out.
/// </summary>
public interface IOverlayService : IAsyncDisposable
{
    event EventHandler<ConnectionStatus>? StatusChanged;
    ConnectionStatus Status { get; }

    /// <summary>Initialise OpenVR and create the overlay. Safe to call again after a SteamVR restart.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Display a single notification, running the in/out animation and holding for the configured
    /// duration. Completes when the card has fully faded out. Honour cancellation for "skip".
    /// </summary>
    Task ShowAsync(PhoneNotification notification, CancellationToken cancellationToken = default);
}
