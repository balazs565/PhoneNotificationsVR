namespace PhoneNotificationsVR.Core.Models;

/// <summary>
/// High level category of a notification.
/// The values mirror the ANCS (Apple Notification Center Service) CategoryID field so the
/// Bluetooth layer can map straight onto this enum without a lookup table.
/// See the ANCS specification, "CategoryID Values".
/// </summary>
public enum NotificationCategory
{
    Other = 0,
    IncomingCall = 1,
    MissedCall = 2,
    Voicemail = 3,
    Social = 4,
    Schedule = 5,
    Email = 6,
    News = 7,
    HealthAndFitness = 8,
    BusinessAndFinance = 9,
    Location = 10,
    Entertainment = 11,
}
