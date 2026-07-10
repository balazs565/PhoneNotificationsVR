using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhoneNotificationsVR.App.Services;
using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Models;
using PhoneNotificationsVR.Core.Services;

namespace PhoneNotificationsVR.App.ViewModels;

/// <summary>Top-level view model for the main window: status, preview, history, log and commands.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly CompositeNotificationSource _source;
    private readonly IOverlayService _overlay;
    private readonly ISettingsService _settings;
    private readonly PreviewImageFactory _previewFactory;

    public SettingsViewModel Settings { get; }
    public ReadOnlyObservableCollection<PhoneNotification> History { get; }
    public ReadOnlyObservableCollection<LogEntry> Log { get; }

    [ObservableProperty] private string _phoneStatusText = "Disconnected";
    [ObservableProperty] private bool _phoneConnected;
    [ObservableProperty] private string _vrStatusText = "Disconnected";
    [ObservableProperty] private bool _vrConnected;
    [ObservableProperty] private ImageSource? _previewImage;

    public MainViewModel(
        CompositeNotificationSource source,
        IOverlayService overlay,
        ISettingsService settings,
        INotificationHistory history,
        InMemoryLogStore logStore,
        PreviewImageFactory previewFactory,
        SettingsViewModel settingsVm)
    {
        _source = source;
        _overlay = overlay;
        _settings = settings;
        _previewFactory = previewFactory;
        Settings = settingsVm;
        History = history.Items;
        Log = logStore.Entries;

        _source.StatusChanged += (_, s) => RunOnUi(() => ApplyPhoneStatus(s));
        _overlay.StatusChanged += (_, s) => RunOnUi(() => ApplyVrStatus(s));
        _settings.Changed += (_, _) => RunOnUi(RefreshPreview);

        ApplyPhoneStatus(_source.Status);
        ApplyVrStatus(_overlay.Status);
        RefreshPreview();
    }

    private void ApplyPhoneStatus(ConnectionStatus s)
    {
        PhoneStatusText = s.Detail;
        PhoneConnected = s.IsConnected;
    }

    private void ApplyVrStatus(ConnectionStatus s)
    {
        VrStatusText = s.Detail;
        VrConnected = s.IsConnected;
    }

    /// <summary>Fires the built-in test notification (also drives the overlay if SteamVR is up).</summary>
    [RelayCommand]
    private void SendTestNotification() => _source.Test.EmitSample();

    [RelayCommand]
    private void RefreshPreview()
    {
        // Build a representative sample card so the user sees the effect of their settings live.
        var sample = new PhoneNotification
        {
            Uid = 0,
            AppIdentifier = "net.whatsapp.WhatsApp",
            AppName = "WhatsApp",
            Title = "Alice",
            Message = "Are you free tonight? We're starting the raid at 9.",
            Category = NotificationCategory.Social,
            Timestamp = DateTimeOffset.Now,
        };
        PreviewImage = _previewFactory.Create(sample);
    }

    private static void RunOnUi(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) action();
        else d.BeginInvoke(action);
    }
}
