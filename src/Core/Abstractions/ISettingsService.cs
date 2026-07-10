using PhoneNotificationsVR.Core.Settings;

namespace PhoneNotificationsVR.Core.Abstractions;

/// <summary>Loads and persists <see cref="AppSettings"/> and notifies listeners on change.</summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>Raised after a successful <see cref="Save"/>, so live components can re-read settings.</summary>
    event EventHandler<AppSettings>? Changed;

    void Load();
    void Save();
}
