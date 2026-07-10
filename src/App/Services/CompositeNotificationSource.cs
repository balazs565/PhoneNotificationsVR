using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Models;

namespace PhoneNotificationsVR.App.Services;

/// <summary>
/// Merges the real notification source (the Windows notification listener) and the in-app test source
/// behind one <see cref="INotificationSource"/> so the dispatcher has a single input. The connection
/// status the UI shows is the real source's, forwarded through unchanged.
///
/// Source-agnostic on purpose: the primary is just an <see cref="INotificationSource"/>, so swapping the
/// listener for the ANCS/Bluetooth source (or any future source) needs no change here.
/// </summary>
public sealed class CompositeNotificationSource : INotificationSource
{
    public INotificationSource Primary { get; }
    public TestNotificationSource Test { get; }

    public event EventHandler<PhoneNotification>? NotificationReceived;
    public event EventHandler<uint>? NotificationRemoved;
    public event EventHandler<ConnectionStatus>? StatusChanged;

    public ConnectionStatus Status => Primary.Status;

    public CompositeNotificationSource(INotificationSource primary, TestNotificationSource test)
    {
        Primary = primary;
        Test = test;

        Primary.NotificationReceived += (_, n) => NotificationReceived?.Invoke(this, n);
        Primary.NotificationRemoved += (_, uid) => NotificationRemoved?.Invoke(this, uid);
        Primary.StatusChanged += (_, s) => StatusChanged?.Invoke(this, s);

        Test.NotificationReceived += (_, n) => NotificationReceived?.Invoke(this, n);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Test.StartAsync(cancellationToken);
        await Primary.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await Primary.StopAsync(cancellationToken);
        await Test.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Primary.DisposeAsync();
        await Test.DisposeAsync();
    }
}
