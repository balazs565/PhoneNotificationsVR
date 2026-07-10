using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Models;

namespace PhoneNotificationsVR.App.Services;

/// <summary>
/// A no-transport notification source used to power the "Test notification" button and to keep the
/// pipeline exercised even without a phone paired. It implements <see cref="INotificationSource"/> so
/// the dispatcher treats injected test cards exactly like real ones.
///
/// In the composition root this is layered *alongside* the real ANCS source via
/// <see cref="CompositeNotificationSource"/>, so tests and live notifications share one queue.
/// </summary>
public sealed class TestNotificationSource : INotificationSource
{
    private static readonly (string App, string AppId, string Sender, string Msg, NotificationCategory Cat)[] Samples =
    {
        ("Incoming Call", "com.apple.mobilephone", "John Smith", "", NotificationCategory.IncomingCall),
        ("WhatsApp", "net.whatsapp.WhatsApp", "Alice", "Are you free tonight?", NotificationCategory.Social),
        ("Discord", "com.hammerandchisel.discord", "Mike", "Join the VC.", NotificationCategory.Social),
        ("Messages", "com.apple.MobileSMS", "Mom", "Call me when you can ❤", NotificationCategory.Social),
        ("Telegram", "ph.telegra.Telegraph", "Dev Group", "Build is green ✅", NotificationCategory.Social),
    };

    private int _next;
    private uint _uid = 1_000_000;

    public event EventHandler<PhoneNotification>? NotificationReceived;
    public event EventHandler<uint>? NotificationRemoved; // unused for the test source
    public event EventHandler<ConnectionStatus>? StatusChanged;

    public ConnectionStatus Status { get; } = new(ConnectionState.Connected, "Test source");

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Emit the next sample notification, cycling through the examples from the spec.</summary>
    public void EmitSample()
    {
        var s = Samples[_next % Samples.Length];
        _next++;
        NotificationReceived?.Invoke(this, new PhoneNotification
        {
            Uid = _uid++,
            AppIdentifier = s.AppId,
            AppName = s.App,
            Title = s.Sender,
            Message = s.Msg,
            Category = s.Cat,
            Timestamp = DateTimeOffset.Now,
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
