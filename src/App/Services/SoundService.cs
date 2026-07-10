using System.IO;
using System.Media;
using System.Windows;
using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Models;

namespace PhoneNotificationsVR.App.Services;

/// <summary>
/// Plays a short cue when a notification is shown. Uses a user-supplied .wav if configured, otherwise
/// a system sound. Kept intentionally light – audio must never block the notification pipeline.
/// </summary>
public sealed class SoundService : ISoundService
{
    private readonly ISettingsService _settings;

    public SoundService(ISettingsService settings) => _settings = settings;

    public void Play(PhoneNotification n)
    {
        var s = _settings.Current.Sound;
        if (!s.Enabled) return;

        // Fire-and-forget so audio latency never delays the overlay.
        Task.Run(() =>
        {
            try
            {
                var custom = n.IsCall ? s.CallSoundPath : s.DefaultSoundPath;
                if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
                {
                    using var player = new SoundPlayer(custom);
                    player.Play();
                }
                else
                {
                    // Built-in fallbacks: a distinct, more urgent tone for calls.
                    (n.IsCall ? SystemSounds.Exclamation : SystemSounds.Asterisk).Play();
                }
            }
            catch { /* audio is best-effort */ }
        });
    }
}
