using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Settings;

namespace PhoneNotificationsVR.App.Services;

/// <summary>Persists <see cref="AppSettings"/> to %AppData%\PhoneNotificationsVR\settings.json.</summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger<JsonSettingsService> _log;

    public AppSettings Current { get; private set; } = new();
    public event EventHandler<AppSettings>? Changed;

    public JsonSettingsService(ILogger<JsonSettingsService> log)
    {
        _log = log;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhoneNotificationsVR");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                _log.LogInformation("Settings loaded from {Path}", _path);
            }
            else
            {
                Save(); // write defaults on first run
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load settings; using defaults.");
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
            Changed?.Invoke(this, Current);
            _log.LogDebug("Settings saved.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to save settings.");
        }
    }
}
