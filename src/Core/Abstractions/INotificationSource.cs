using PhoneNotificationsVR.Core.Models;

namespace PhoneNotificationsVR.Core.Abstractions;

/// <summary>
/// A source of phone notifications. The production implementation is the Bluetooth/ANCS client,
/// but tests and the "Test notification" button use in-memory sources against the same contract.
/// Implementations own their own reconnection loop and simply report state via <see cref="StatusChanged"/>.
/// </summary>
public interface INotificationSource : IAsyncDisposable
{
    /// <summary>Raised for every notification the phone pushes (added or modified).</summary>
    event EventHandler<PhoneNotification>? NotificationReceived;

    /// <summary>Raised when a notification is dismissed/removed on the phone (e.g. call answered).</summary>
    event EventHandler<uint>? NotificationRemoved;

    /// <summary>Raised whenever the connection state changes.</summary>
    event EventHandler<ConnectionStatus>? StatusChanged;

    /// <summary>Current connection status.</summary>
    ConnectionStatus Status { get; }

    /// <summary>Start connecting and stay connected (with automatic recovery) until stopped.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stop and release the underlying transport.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
