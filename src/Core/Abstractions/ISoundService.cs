using PhoneNotificationsVR.Core.Models;

namespace PhoneNotificationsVR.Core.Abstractions;

/// <summary>Plays the audible cue that accompanies a notification (respects the sound settings).</summary>
public interface ISoundService
{
    void Play(PhoneNotification notification);
}
