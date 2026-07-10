namespace PhoneNotificationsVR.Core.Settings;

/// <summary>Where the notification overlay is anchored in the VR world.</summary>
public enum OverlayAnchor
{
    /// <summary>Locked to the headset – always in view (HUD style). Default.</summary>
    FollowHeadset,
    /// <summary>Placed once in world space and stays put as you move.</summary>
    FixedInWorld,
    /// <summary>Rides on a tracked controller.</summary>
    NearController,
    /// <summary>Rides just above a controller like a wristwatch (Quest-style).</summary>
    NearWrist,
}

/// <summary>Which hand to attach to for controller/wrist anchors.</summary>
public enum TrackedHand { Left, Right }

/// <summary>How the app treats the per-app filter list.</summary>
public enum FilterMode
{
    /// <summary>Show everything except apps in <see cref="AppFilterSettings.Blacklist"/>.</summary>
    BlacklistOnly,
    /// <summary>Show only apps in <see cref="AppFilterSettings.Whitelist"/>.</summary>
    WhitelistOnly,
}

/// <summary>Root settings object. Serialised to JSON in the user's AppData folder.</summary>
public sealed class AppSettings
{
    public OverlaySettings Overlay { get; set; } = new();
    public AppFilterSettings Filter { get; set; } = new();
    public SoundSettings Sound { get; set; } = new();
    public GeneralSettings General { get; set; } = new();

    /// <summary>Bluetooth id of the last paired phone, so we reconnect to the same device on start.</summary>
    public string? PairedPhoneId { get; set; }
    public string? PairedPhoneName { get; set; }
}

public sealed class OverlaySettings
{
    public OverlayAnchor Anchor { get; set; } = OverlayAnchor.FollowHeadset;
    public TrackedHand Hand { get; set; } = TrackedHand.Left;

    /// <summary>Overlay width in metres in the VR world (height derives from the rendered aspect).</summary>
    public double WidthMeters { get; set; } = 0.38;

    /// <summary>0..1 master opacity of the whole overlay.</summary>
    public double Opacity { get; set; } = 0.95;

    /// <summary>UI font scale multiplier (1.0 = design default).</summary>
    public double FontScale { get; set; } = 1.0;

    /// <summary>How long each notification stays fully visible, in seconds (excludes fade in/out).</summary>
    public double DurationSeconds { get; set; } = 6.0;

    /// <summary>Fade + slide animation length in seconds.</summary>
    public double AnimationSeconds { get; set; } = 0.35;

    /// <summary>
    /// Offset (metres, right/up/forward) applied after the anchor transform.
    /// Lets the user nudge the card so it does not block the crosshair.
    /// </summary>
    public double OffsetRight { get; set; } = 0.0;
    public double OffsetUp { get; set; } = -0.18;
    public double OffsetForward { get; set; } = -1.4; // negative = in front of the headset

    /// <summary>Distance in front of the headset for the HUD anchor, in metres.</summary>
    public double FollowDistance { get; set; } = 1.4;

    /// <summary>Curvature 0 (flat) .. 1 (fully curved) passed to the OpenVR overlay.</summary>
    public double Curvature { get; set; } = 0.0;
}

public sealed class AppFilterSettings
{
    public FilterMode Mode { get; set; } = FilterMode.BlacklistOnly;

    /// <summary>App bundle ids to always show (used in <see cref="FilterMode.WhitelistOnly"/>).</summary>
    public List<string> Whitelist { get; set; } = new();

    /// <summary>App bundle ids to always hide (used in <see cref="FilterMode.BlacklistOnly"/>).</summary>
    public List<string> Blacklist { get; set; } = new();

    /// <summary>Incoming calls bypass every filter when true (safety net for "never miss a call").</summary>
    public bool AlwaysShowCalls { get; set; } = true;
}

public sealed class SoundSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>0..1 playback volume.</summary>
    public double Volume { get; set; } = 0.6;

    /// <summary>Optional custom .wav for normal notifications; null = built-in chime.</summary>
    public string? DefaultSoundPath { get; set; }

    /// <summary>Optional custom .wav for calls; null = built-in ring.</summary>
    public string? CallSoundPath { get; set; }
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimizedToTray { get; set; } = true;
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>How many past notifications to retain in the history list.</summary>
    public int HistoryLimit { get; set; } = 200;
}
