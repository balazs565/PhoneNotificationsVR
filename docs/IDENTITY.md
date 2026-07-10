# Package identity (one-time setup)

The app reads notifications through the Windows **`UserNotificationListener`** API. That API refuses to run
from a plain unpackaged `.exe` — it requires the process to have **package identity**. This is a Windows
platform rule, not something the app can bypass in code.

The lightest way to satisfy it is a **sparse package** ("packaging with external location"): you keep the
normal `PhoneNotificationsVR.exe` and your normal build/run workflow, and just register a tiny signed
package that says "this .exe has identity X." No MSIX conversion, no change to how you launch the app.

## Do it in one command

1. **Build** the app first:
   ```powershell
   dotnet build -c Release
   ```
2. Open **PowerShell as Administrator** (trusting a dev cert + registering a package need admin).
3. From the repo root:
   ```powershell
   ./packaging/Register-Identity.ps1
   ```

That script:
1. generates placeholder logo images,
2. creates and **trusts** a self-signed dev certificate (subject `CN=PhoneNotificationsVR Dev`),
3. packs + signs a sparse `.msix` from [`packaging/AppxManifest.xml`](../packaging/AppxManifest.xml),
4. registers it with `-ExternalLocation` pointing at your build output folder.

If your build output is elsewhere, pass it:
```powershell
./packaging/Register-Identity.ps1 -AppDir "C:\path\to\your\app\folder"
```

> Requires the **Windows 10/11 SDK** (for `makeappx.exe` / `signtool.exe`). If the script can't find them,
> install "Windows SDK" via the Visual Studio Installer.

## After running it

- Launch `PhoneNotificationsVR.exe`. On first run Windows shows a **notification-access prompt** — click
  **Yes**. (You can also toggle it later at *Settings ▸ Privacy & security ▸ Notifications*.)
- The app's **iPhone/notifications status dot** turns green once access is granted and notifications flow.

## Removing identity

```powershell
Get-AppxPackage *PhoneNotificationsVR* | Remove-AppxPackage
```

## Production alternative: full MSIX

For a shippable product, package the app as a normal **MSIX** (which grants identity natively) and sign it
with a real certificate. The sparse-package flow above is the low-friction path for running your own build.

## Why not avoid this entirely?

We could — by only reading notifications from apps that don't need the listener — but there is no other API
that reads *system-wide* notifications (including Phone Link's iPhone notifications). Identity is the price
of that capability, and the sparse package keeps it to a one-time step.
