using System.ComponentModel;
using System.Windows;
using PhoneNotificationsVR.App.ViewModels;
using PhoneNotificationsVR.Core.Abstractions;

namespace PhoneNotificationsVR.App;

/// <summary>Main window. Hosts the dashboard, settings, history and log tabs.</summary>
public partial class MainWindow : Window
{
    private readonly ISettingsService _settings;

    public MainWindow(MainViewModel viewModel, ISettingsService settings)
    {
        _settings = settings;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Settings.LoadLists();
    }

    /// <summary>Honour the "minimize to tray on close" preference instead of exiting the app.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_settings.Current.General.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
