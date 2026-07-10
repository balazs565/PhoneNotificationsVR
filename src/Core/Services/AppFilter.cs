using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Models;
using PhoneNotificationsVR.Core.Settings;

namespace PhoneNotificationsVR.Core.Services;

/// <summary>Decides whether a given notification should be shown, based on the whitelist/blacklist settings.</summary>
public sealed class AppFilter
{
    private readonly ISettingsService _settings;

    public AppFilter(ISettingsService settings) => _settings = settings;

    /// <summary>Returns true if the notification passes the current filter rules.</summary>
    public bool ShouldShow(PhoneNotification n)
    {
        var f = _settings.Current.Filter;

        // Never-miss-a-call safety net: calls bypass all filtering when enabled.
        if (f.AlwaysShowCalls && n.IsCall)
            return true;

        var id = n.AppIdentifier;
        return f.Mode switch
        {
            FilterMode.WhitelistOnly => f.Whitelist.Contains(id, StringComparer.OrdinalIgnoreCase),
            FilterMode.BlacklistOnly => !f.Blacklist.Contains(id, StringComparer.OrdinalIgnoreCase),
            _ => true,
        };
    }
}
