using System.Collections.ObjectModel;
using PhoneNotificationsVR.Core.Models;

namespace PhoneNotificationsVR.Core.Abstractions;

/// <summary>Keeps a bounded, most-recent-first list of notifications for the history UI.</summary>
public interface INotificationHistory
{
    ReadOnlyObservableCollection<PhoneNotification> Items { get; }
    void Add(PhoneNotification notification);
    void Clear();
}
