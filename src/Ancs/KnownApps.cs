namespace PhoneNotificationsVR.Ancs;

/// <summary>
/// Friendly-name fallback for common apps, used when the phone has not (yet) returned a display name
/// via GetAppAttributes. Purely cosmetic – the real name from the phone always takes precedence.
/// </summary>
public static class KnownApps
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["com.apple.mobilephone"] = "Phone",
        ["com.apple.MobileSMS"] = "Messages",
        ["com.apple.facetime"] = "FaceTime",
        ["net.whatsapp.WhatsApp"] = "WhatsApp",
        ["com.hammerandchisel.discord"] = "Discord",
        ["com.facebook.Messenger"] = "Messenger",
        ["ph.telegra.Telegraph"] = "Telegram",
        ["com.tinyspeck.chatlyio"] = "Slack",
        ["com.toyopagroup.picaboo"] = "Snapchat",
        ["com.burbn.instagram"] = "Instagram",
        ["com.google.Gmail"] = "Gmail",
        ["com.apple.mobilemail"] = "Mail",
        ["com.microsoft.Office.Outlook"] = "Outlook",
        ["com.atebits.Tweetie2"] = "X",
    };

    /// <summary>Resolve a friendly name, falling back to the last dotted segment of the bundle id.</summary>
    public static string Friendly(string bundleId)
    {
        if (Map.TryGetValue(bundleId, out var name))
            return name;

        // e.g. "com.example.CoolApp" -> "CoolApp"
        var idx = bundleId.LastIndexOf('.');
        return idx >= 0 && idx < bundleId.Length - 1 ? bundleId[(idx + 1)..] : bundleId;
    }
}
