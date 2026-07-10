# Installation & pairing guide

This is the end-to-end setup, from a fresh PC to seeing your first iPhone notification in VR.

## 1. Set up Microsoft Phone Link with your iPhone (one time)

Phone Link is what actually bridges your iPhone to the PC over Bluetooth; our app reads what it delivers.

1. Install / open **Phone Link** on Windows (it's built in; else get it from the Microsoft Store).
2. Choose **iPhone** and follow the wizard — it pairs over Bluetooth and asks you to confirm access on the
   phone (contacts, notifications, calls). **Allow** these on the iPhone when prompted.
3. Verify in Phone Link that **calls and messages** appear. If they do, iPhone notifications are now
   reaching Windows.

> Only want desktop-app notifications (Discord/WhatsApp/Telegram/Slack desktop)? You can skip Phone Link —
> those apps post to Windows directly.

## 2. Grant package identity (one time)

The notification-reading API requires the app to have *package identity*. Build the app, then from an
**elevated PowerShell**:

```powershell
dotnet build -c Release
./packaging/Register-Identity.ps1
```

Full explanation and troubleshooting: [`IDENTITY.md`](IDENTITY.md).

## 3. First run

1. Launch **PhoneNotificationsVR.exe**.
2. Click **Yes** on the Windows **notification-access** prompt (or enable it later at *Settings ▸ Privacy &
   security ▸ Notifications*).
3. The window opens with two status dots at the top:
   - **iPhone (notifications)** — turns green when notification access is granted and toasts are readable.
   - **SteamVR Overlay** — turns green when SteamVR is running and the overlay is created.
4. If the notifications dot stays red:
   - Make sure you ran the identity setup (step 2) and approved the access prompt.
   - Confirm Phone Link is running and showing your phone's messages.
   - Watch the **Log** tab — it explains exactly what the listener is doing.

## 3. Verify

- With SteamVR running and headset on, click **Send Test Notification**. A card appears in VR.
- Lock the app's behavior in **Settings** (anchor, size, opacity, duration…). The **preview** on the
  Dashboard updates live and matches VR exactly.
- Now send yourself a real WhatsApp/iMessage or place a call — it appears in-headset.

## 4. Make it always-on

In **Settings ▸ General**:
- **Start with Windows** — adds a per-user startup entry (no admin needed).
- **Start minimized to tray** — launches silently into the system tray.
- **Minimize to tray on close** — the ✕ button hides to tray instead of quitting.

Right-click the tray icon for **Open**, **Send test notification**, and **Exit**.

## 5. Recovery behavior (nothing to configure)

The app self-heals from:
- SteamVR being closed/restarted (overlay is recreated).
- The phone leaving/returning to range, Bluetooth toggled, or Wi-Fi/router blips.
- The PC sleeping and waking.

You can confirm any recovery in the **Log** tab.

## Uninstall

- Turn off **Start with Windows** (removes the registry Run entry), then delete the app folder.
- Settings live in `%AppData%\PhoneNotificationsVR\settings.json` — delete to reset.
- Optionally unpair the PC from the iPhone in Bluetooth settings.
