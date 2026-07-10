namespace PhoneNotificationsVR.Core.Models;

/// <summary>
/// A single notification received from the phone, normalised into a source-agnostic shape.
/// This is the central domain object that flows: source → filter → queue → overlay renderer.
/// </summary>
public sealed record PhoneNotification
{
    /// <summary>Stable identifier of the notification on the phone (ANCS NotificationUID).</summary>
    public required uint Uid { get; init; }

    /// <summary>Reverse-DNS bundle id of the originating app, e.g. <c>net.whatsapp.WhatsApp</c>.</summary>
    public required string AppIdentifier { get; init; }

    /// <summary>Human readable app name, e.g. "WhatsApp". Resolved via the ANCS app attributes.</summary>
    public string AppName { get; init; } = string.Empty;

    /// <summary>Notification title. For messengers this is usually the sender / chat name.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Secondary line under the title (group chat name, etc.). Often empty.</summary>
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>Body text of the notification.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Category as reported by the phone.</summary>
    public NotificationCategory Category { get; init; } = NotificationCategory.Other;

    /// <summary>Time the notification was created on the phone (falls back to receipt time).</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>True when the phone flagged this as important / requiring immediate attention.</summary>
    public bool IsImportant { get; init; }

    /// <summary>True for the two call categories – used to give calls a distinct, louder treatment.</summary>
    public bool IsCall => Category is NotificationCategory.IncomingCall or NotificationCategory.MissedCall;

    /// <summary>
    /// Best-effort "who sent this" string used on the overlay's sender line.
    /// Falls back through subtitle/title so the overlay is never blank.
    /// </summary>
    public string BestSender =>
        !string.IsNullOrWhiteSpace(Title) ? Title :
        !string.IsNullOrWhiteSpace(Subtitle) ? Subtitle :
        AppName;
}
