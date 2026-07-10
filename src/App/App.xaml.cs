using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhoneNotificationsVR.App.Services;
using PhoneNotificationsVR.Listener;
using PhoneNotificationsVR.App.ViewModels;
using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Services;
using PhoneNotificationsVR.Overlay;

namespace PhoneNotificationsVR.App;

/// <summary>
/// Composition root. Wires up dependency injection, starts the background services (overlay, phone
/// link, dispatcher), and manages the tray icon + window lifecycle.
/// </summary>
public partial class App : Application
{
    private IHost _host = null!;
    private TaskbarIcon? _tray;
    private MainWindow? _window;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance guard so two copies don't both grab the overlay/Bluetooth.
        if (!SingleInstance.Acquire())
        {
            Shutdown();
            return;
        }

        _host = BuildHost();
        var settings = _host.Services.GetRequiredService<ISettingsService>();
        settings.Load();

        // Start background services. The notification source reads the Windows notification centre
        // (fed by Phone Link for the iPhone, and by desktop apps directly).
        var composite = _host.Services.GetRequiredService<CompositeNotificationSource>();
        await _host.Services.GetRequiredService<IOverlayService>().StartAsync();
        await composite.StartAsync();
        _host.Services.GetRequiredService<NotificationDispatcher>().Start();

        // Tray icon.
        _tray = CreateTray();

        // Main window.
        _window = _host.Services.GetRequiredService<MainWindow>();
        bool startMinimized = settings.Current.General.StartMinimizedToTray
                              || e.Args.Contains("--minimized");
        if (!startMinimized)
            _window.Show();

        _host.Services.GetRequiredService<ILogger<App>>()
             .LogInformation("Application started (minimized={Min}).", startMinimized);
    }

    private IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        // Logging: in-memory sink powers the Log window; Debug output aids development.
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddDebug();
        builder.Services.AddSingleton<InMemoryLogStore>();
        builder.Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(
            sp => new InMemoryLoggerProvider(sp.GetRequiredService<InMemoryLogStore>()));

        var s = builder.Services;

        // Core infrastructure.
        s.AddSingleton<ISettingsService, JsonSettingsService>();
        s.AddSingleton<AutoStartService>();
        s.AddSingleton<INotificationRenderer, NotificationRenderer>();
        s.AddSingleton<IOverlayService, SteamVrOverlayService>();
        s.AddSingleton<ISoundService, SoundService>();
        s.AddSingleton<INotificationHistory, NotificationHistory>();
        s.AddSingleton<PreviewImageFactory>();

        // Notification sources (real + test) behind one composite.
        // Primary = the Windows notification listener (Phone Link bridges the iPhone into it).
        s.AddSingleton<WindowsNotificationListenerSource>();
        s.AddSingleton<TestNotificationSource>();
        s.AddSingleton<CompositeNotificationSource>(sp => new CompositeNotificationSource(
            sp.GetRequiredService<WindowsNotificationListenerSource>(),
            sp.GetRequiredService<TestNotificationSource>()));
        s.AddSingleton<INotificationSource>(sp => sp.GetRequiredService<CompositeNotificationSource>());

        // Application services.
        s.AddSingleton<AppFilter>();
        s.AddSingleton<NotificationDispatcher>();

        // View models + window.
        s.AddSingleton<SettingsViewModel>();
        s.AddSingleton<MainViewModel>();
        s.AddSingleton<MainWindow>();

        return builder.Build();
    }

    private TaskbarIcon CreateTray()
    {
        var tray = new TaskbarIcon
        {
            ToolTipText = "Phone Notifications for SteamVR",
            Visibility = Visibility.Visible,
        };
        try { tray.Icon = System.Drawing.SystemIcons.Application; } catch { /* icon optional */ }

        var menu = new System.Windows.Controls.ContextMenu();
        var open = new System.Windows.Controls.MenuItem { Header = "Open" };
        open.Click += (_, _) => ShowWindow();
        var test = new System.Windows.Controls.MenuItem { Header = "Send test notification" };
        test.Click += (_, _) => _host.Services.GetRequiredService<MainViewModel>().SendTestNotificationCommand.Execute(null);
        var exit = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exit.Click += (_, _) => Shutdown();
        menu.Items.Add(open);
        menu.Items.Add(test);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(exit);
        tray.ContextMenu = menu;
        tray.TrayMouseDoubleClick += (_, _) => ShowWindow();
        return tray;
    }

    private void ShowWindow()
    {
        _window ??= _host.Services.GetRequiredService<MainWindow>();
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                await _host.Services.GetRequiredService<NotificationDispatcher>().DisposeAsync();
                await _host.Services.GetRequiredService<CompositeNotificationSource>().DisposeAsync();
                await _host.Services.GetRequiredService<IOverlayService>().DisposeAsync();
                _host.Dispose();
            }
        }
        catch { /* best-effort shutdown */ }
        _tray?.Dispose();
        SingleInstance.Release();
        base.OnExit(e);
    }
}
