namespace PhoneNotificationsVR.Ancs;

/// <summary>
/// Constants from Apple's ANCS (Apple Notification Center Service) specification.
/// The iPhone is the GATT <b>server</b> that exposes this service; Windows is the GATT <b>client</b>
/// (the "Notification Consumer") once the two devices are Bluetooth-paired/bonded.
/// Reference: developer.apple.com – "Apple Notification Center Service (ANCS) Specification".
/// </summary>
public static class AncsUuids
{
    /// <summary>The ANCS primary service.</summary>
    public static readonly Guid Service = new("7905F431-B5CE-4E99-A40F-4B1E122D00D0");

    /// <summary>Notify characteristic: 8-byte packets announcing added/modified/removed notifications.</summary>
    public static readonly Guid NotificationSource = new("9FBF120D-6301-42D9-8C58-25E699A21DBD");

    /// <summary>Write characteristic: we send "Get Notification/App Attributes" commands here.</summary>
    public static readonly Guid ControlPoint = new("69D1D8F3-45E1-49A8-9821-9BBDFDAAD9D9");

    /// <summary>Notify characteristic: the phone streams back the requested attribute data here.</summary>
    public static readonly Guid DataSource = new("22EAC6E9-24D6-4BB5-BE44-B36ACE7C7BFB");
}

/// <summary>EventID field of a Notification Source packet.</summary>
public enum AncsEventId : byte
{
    NotificationAdded = 0,
    NotificationModified = 1,
    NotificationRemoved = 2,
}

/// <summary>EventFlags bitmask of a Notification Source packet.</summary>
[Flags]
public enum AncsEventFlags : byte
{
    None = 0,
    Silent = 1 << 0,
    Important = 1 << 1,
    PreExisting = 1 << 2,
    PositiveAction = 1 << 3,
    NegativeAction = 1 << 4,
}

/// <summary>CommandID for a Control Point request.</summary>
public enum AncsCommandId : byte
{
    GetNotificationAttributes = 0,
    GetAppAttributes = 1,
    PerformNotificationAction = 2,
}

/// <summary>Notification attribute ids requested via GetNotificationAttributes.</summary>
public enum AncsNotificationAttributeId : byte
{
    AppIdentifier = 0,
    Title = 1,          // requires a 2-byte max-length parameter
    Subtitle = 2,       // requires a 2-byte max-length parameter
    Message = 3,        // requires a 2-byte max-length parameter
    MessageSize = 4,
    Date = 5,
    PositiveActionLabel = 6,
    NegativeActionLabel = 7,
}

/// <summary>App attribute ids requested via GetAppAttributes (maps bundle id → display name).</summary>
public enum AncsAppAttributeId : byte
{
    DisplayName = 0,
}
