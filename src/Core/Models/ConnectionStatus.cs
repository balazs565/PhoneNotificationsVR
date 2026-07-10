namespace PhoneNotificationsVR.Core.Models;

/// <summary>Connection state for both the phone (Bluetooth/ANCS) and SteamVR links.</summary>
public enum ConnectionState
{
    /// <summary>Not started, or intentionally stopped.</summary>
    Disconnected,
    /// <summary>Actively trying to (re)establish the link.</summary>
    Connecting,
    /// <summary>Fully connected and operational.</summary>
    Connected,
    /// <summary>Connected once but the link dropped; the recovery loop is retrying.</summary>
    Reconnecting,
    /// <summary>A non-recoverable error occurred (e.g. Bluetooth radio missing).</summary>
    Faulted,
}

/// <summary>Immutable snapshot of a connection's state plus a human readable detail line.</summary>
/// <param name="State">Current state.</param>
/// <param name="Detail">Short message for the UI/log, e.g. "Paired to iPhone (RSSI -54)".</param>
public readonly record struct ConnectionStatus(ConnectionState State, string Detail)
{
    public static ConnectionStatus Disconnected(string detail = "Disconnected") => new(ConnectionState.Disconnected, detail);
    public bool IsConnected => State == ConnectionState.Connected;
}
