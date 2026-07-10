using System.Threading;

namespace PhoneNotificationsVR.App;

/// <summary>
/// Ensures only one instance runs, so a second launch (e.g. the Windows autostart entry firing after
/// a manual launch) does not fight over the Bluetooth radio or the SteamVR overlay handle.
/// </summary>
internal static class SingleInstance
{
    private static Mutex? _mutex;

    public static bool Acquire()
    {
        _mutex = new Mutex(initiallyOwned: true, "Global\\PhoneNotificationsVR.SingleInstance", out bool createdNew);
        return createdNew;
    }

    public static void Release()
    {
        try { _mutex?.ReleaseMutex(); } catch { /* not owned */ }
        _mutex?.Dispose();
        _mutex = null;
    }
}
