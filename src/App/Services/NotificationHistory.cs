using System.Collections.ObjectModel;
using System.Windows;
using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Models;
using PhoneNotificationsVR.Core.Settings;

namespace PhoneNotificationsVR.App.Services;

/// <summary>Bounded, most-recent-first history bound directly to the WPF history list.</summary>
public sealed class NotificationHistory : INotificationHistory
{
    private readonly ISettingsService _settings;
    private readonly ObservableCollection<PhoneNotification> _items = new();

    public ReadOnlyObservableCollection<PhoneNotification> Items { get; }

    public NotificationHistory(ISettingsService settings)
    {
        _settings = settings;
        Items = new ReadOnlyObservableCollection<PhoneNotification>(_items);
    }

    public void Add(PhoneNotification n)
    {
        // Marshal to the UI thread since this feeds an ObservableCollection bound to the view.
        RunOnUi(() =>
        {
            _items.Insert(0, n);
            int limit = Math.Max(10, _settings.Current.General.HistoryLimit);
            while (_items.Count > limit) _items.RemoveAt(_items.Count - 1);
        });
    }

    public void Clear() => RunOnUi(_items.Clear);

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }
}
