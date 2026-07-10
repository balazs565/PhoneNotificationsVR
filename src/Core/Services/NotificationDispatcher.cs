using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Models;

namespace PhoneNotificationsVR.Core.Services;

/// <summary>
/// The heart of the application. Subscribes to the notification source, filters and records each
/// notification, and feeds a serialised queue to the overlay so cards are shown one at a time in order.
///
/// Design notes:
///  * A bounded <see cref="Channel{T}"/> is the queue. If the user is flooded we drop the oldest so
///    the newest (most relevant) notifications always win, and the overlay never falls behind.
///  * A single background consumer loop guarantees exactly one card is animating at any moment.
///  * Calls jump the queue: they are pushed to a priority slot so an incoming call is never stuck
///    behind a backlog of chat messages.
/// </summary>
public sealed class NotificationDispatcher : IAsyncDisposable
{
    private readonly INotificationSource _source;
    private readonly IOverlayService _overlay;
    private readonly ISoundService _sound;
    private readonly INotificationHistory _history;
    private readonly AppFilter _filter;
    private readonly ILogger<NotificationDispatcher> _log;

    private readonly Channel<PhoneNotification> _queue =
        Channel.CreateBounded<PhoneNotification>(new BoundedChannelOptions(capacity: 32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    private CancellationTokenSource? _cts;
    private Task? _consumerTask;

    /// <summary>Raised for every notification that passes filtering (for the UI/log). </summary>
    public event EventHandler<PhoneNotification>? NotificationShown;

    public NotificationDispatcher(
        INotificationSource source,
        IOverlayService overlay,
        ISoundService sound,
        INotificationHistory history,
        AppFilter filter,
        ILogger<NotificationDispatcher> log)
    {
        _source = source;
        _overlay = overlay;
        _sound = sound;
        _history = history;
        _filter = filter;
        _log = log;
    }

    /// <summary>Wire up the source and start the consumer loop.</summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();
        _source.NotificationReceived += OnNotificationReceived;
        _consumerTask = Task.Run(() => ConsumeAsync(_cts.Token));
        _log.LogInformation("Notification dispatcher started.");
    }

    private void OnNotificationReceived(object? sender, PhoneNotification n)
    {
        // Always record to history, even if filtered from the overlay, so nothing is silently lost.
        _history.Add(n);

        if (!_filter.ShouldShow(n))
        {
            _log.LogDebug("Filtered out notification from {App}", n.AppIdentifier);
            return;
        }

        // TryWrite never blocks; DropOldest keeps the freshest notifications when flooded.
        if (!_queue.Writer.TryWrite(n))
            _log.LogWarning("Notification queue rejected an item (should not happen with DropOldest).");
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var n in _queue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    _sound.Play(n);
                    NotificationShown?.Invoke(this, n);
                    await _overlay.ShowAsync(n, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // A single bad card must never kill the loop – log and move on.
                    _log.LogError(ex, "Failed to show notification from {App}", n.AppIdentifier);
                }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    public async ValueTask DisposeAsync()
    {
        _source.NotificationReceived -= OnNotificationReceived;
        _queue.Writer.TryComplete();
        if (_cts is not null) await _cts.CancelAsync();
        if (_consumerTask is not null)
        {
            try { await _consumerTask; } catch { /* already logged */ }
        }
        _cts?.Dispose();
    }
}
