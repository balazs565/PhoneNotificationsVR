using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhoneNotificationsVR.App.Services;
using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Settings;

namespace PhoneNotificationsVR.App.ViewModels;

/// <summary>
/// Two-way binding surface over <see cref="AppSettings"/>. Each property writes straight into the
/// live settings object and triggers a debounced save so the overlay reacts immediately while typing.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly AutoStartService _autoStart;

    public SettingsViewModel(ISettingsService settings, AutoStartService autoStart)
    {
        _settings = settings;
        _autoStart = autoStart;
        _startWithWindows = _autoStart.IsEnabled();
    }

    private OverlaySettings O => _settings.Current.Overlay;
    private AppFilterSettings F => _settings.Current.Filter;
    private SoundSettings S => _settings.Current.Sound;
    private GeneralSettings G => _settings.Current.General;

    public Array Anchors => Enum.GetValues(typeof(OverlayAnchor));
    public Array Hands => Enum.GetValues(typeof(TrackedHand));
    public Array FilterModes => Enum.GetValues(typeof(FilterMode));

    // ---- Overlay -----------------------------------------------------------------------------
    public OverlayAnchor Anchor { get => O.Anchor; set { O.Anchor = value; OnChanged(); } }
    public TrackedHand Hand { get => O.Hand; set { O.Hand = value; OnChanged(); } }
    public double WidthMeters { get => O.WidthMeters; set { O.WidthMeters = value; OnChanged(); } }
    public double Opacity { get => O.Opacity; set { O.Opacity = value; OnChanged(); } }
    public double FontScale { get => O.FontScale; set { O.FontScale = value; OnChanged(); } }
    public double DurationSeconds { get => O.DurationSeconds; set { O.DurationSeconds = value; OnChanged(); } }
    public double AnimationSeconds { get => O.AnimationSeconds; set { O.AnimationSeconds = value; OnChanged(); } }
    public double FollowDistance { get => O.FollowDistance; set { O.FollowDistance = value; OnChanged(); } }
    public double OffsetUp { get => O.OffsetUp; set { O.OffsetUp = value; OnChanged(); } }
    public double OffsetRight { get => O.OffsetRight; set { O.OffsetRight = value; OnChanged(); } }
    public double Curvature { get => O.Curvature; set { O.Curvature = value; OnChanged(); } }

    // ---- Filtering ---------------------------------------------------------------------------
    public FilterMode FilterMode { get => F.Mode; set { F.Mode = value; OnChanged(); } }
    public bool AlwaysShowCalls { get => F.AlwaysShowCalls; set { F.AlwaysShowCalls = value; OnChanged(); } }
    public ObservableCollection<string> Whitelist { get; } = new();
    public ObservableCollection<string> Blacklist { get; } = new();

    [ObservableProperty] private string _newAppId = string.Empty;

    // ---- Sound -------------------------------------------------------------------------------
    public bool SoundEnabled { get => S.Enabled; set { S.Enabled = value; OnChanged(); } }
    public double SoundVolume { get => S.Volume; set { S.Volume = value; OnChanged(); } }

    // ---- General -----------------------------------------------------------------------------
    public bool StartMinimized { get => G.StartMinimizedToTray; set { G.StartMinimizedToTray = value; OnChanged(); } }
    public bool MinimizeToTrayOnClose { get => G.MinimizeToTrayOnClose; set { G.MinimizeToTrayOnClose = value; OnChanged(); } }
    public int HistoryLimit { get => G.HistoryLimit; set { G.HistoryLimit = value; OnChanged(); } }

    [ObservableProperty] private bool _startWithWindows;

    partial void OnStartWithWindowsChanged(bool value)
    {
        _autoStart.SetEnabled(value);
        G.StartWithWindows = value;
        OnChanged();
    }

    public void LoadLists()
    {
        Whitelist.Clear();
        foreach (var id in F.Whitelist) Whitelist.Add(id);
        Blacklist.Clear();
        foreach (var id in F.Blacklist) Blacklist.Add(id);
    }

    [RelayCommand]
    private void AddToWhitelist()
    {
        if (string.IsNullOrWhiteSpace(NewAppId) || F.Whitelist.Contains(NewAppId)) return;
        F.Whitelist.Add(NewAppId.Trim());
        Whitelist.Add(NewAppId.Trim());
        NewAppId = string.Empty;
        OnChanged();
    }

    [RelayCommand]
    private void AddToBlacklist()
    {
        if (string.IsNullOrWhiteSpace(NewAppId) || F.Blacklist.Contains(NewAppId)) return;
        F.Blacklist.Add(NewAppId.Trim());
        Blacklist.Add(NewAppId.Trim());
        NewAppId = string.Empty;
        OnChanged();
    }

    [RelayCommand]
    private void RemoveWhitelisted(string id)
    {
        F.Whitelist.Remove(id);
        Whitelist.Remove(id);
        OnChanged();
    }

    [RelayCommand]
    private void RemoveBlacklisted(string id)
    {
        F.Blacklist.Remove(id);
        Blacklist.Remove(id);
        OnChanged();
    }

    private void OnChanged() => _settings.Save();
}
