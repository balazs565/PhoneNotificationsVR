# Build instructions

## Prerequisites

- **Windows 10 (19041+) or Windows 11.**
- **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
  Verify: `dotnet --version` should print `8.0.x`.
- **Visual Studio 2022 (17.8+)** *optional* — with the *.NET desktop development* workload. The solution
  also builds fully from the command line.
- A **Bluetooth LE** adapter (to actually receive notifications; the app builds and runs without one).
- **SteamVR** installed (to see the overlay; the desktop app runs without it).

> The `Ancs`, `Overlay` and `App` projects target `net8.0-windows10.0.19041.0` because they use the
> Windows Bluetooth WinRT APIs and WPF. The `Core` project is plain `net8.0`.

## Command line

```powershell
# from the repository root (the folder containing PhoneNotificationsVR.sln)
dotnet restore
dotnet build -c Release
dotnet run  -c Release --project src/App
```

The first `restore` pulls these NuGet packages:

| Package | Purpose |
|---------|---------|
| `OVRSharp` (1.2.0) | OpenVR C# binding + bundled native `openvr_api.dll` |
| `Hardcodet.NotifyIcon.Wpf` (2.0.1) | System-tray icon |
| `CommunityToolkit.Mvvm` (8.3.2) | MVVM (`ObservableObject`, `[RelayCommand]`) |
| `Microsoft.Extensions.Hosting` (8.0.1) | Dependency-injection host + logging |
| `System.Drawing.Common` (8.0.7) | Renders the notification card bitmap |

## Visual Studio

1. Open `PhoneNotificationsVR.sln`.
2. Set **PhoneNotificationsVR.App** as the startup project.
3. Build/Run (F5).

## Publishing a self-contained build

```powershell
dotnet publish src/App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o publish
```

The output `publish/PhoneNotificationsVR.exe` runs on a machine without the .NET runtime installed.

### Native `openvr_api.dll`

OVRSharp ships the native `openvr_api.dll` and MSBuild copies it next to the executable automatically.
If SteamVR ever reports it cannot find the overlay runtime, confirm `openvr_api.dll` sits beside
`PhoneNotificationsVR.exe`; if missing, copy it from
`%ProgramFiles(x86)%\Steam\steamapps\common\SteamVR\bin\win64\openvr_api.dll`.

## Troubleshooting the build

| Symptom | Fix |
|---------|-----|
| `The Windows SDK ... 10.0.19041.0 was not found` | Install the SDK via VS Installer, or the app builds if a newer 10.0.x SDK is present — adjust the TFM if needed. |
| `NETSDK` errors about Windows TFM on non-Windows | This solution is Windows-only by design (Bluetooth + WPF + OpenVR). Build on Windows. |
| Restore fails offline | The packages above must be reachable; run `dotnet restore` once with internet access. |
