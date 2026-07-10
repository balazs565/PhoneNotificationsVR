# Architecture

## Goals

* One job, done reliably: surface iPhone notifications inside SteamVR.
* Simplest architecture that is **honest** about iOS constraints (see the ANCS decision in the README).
* Clean Architecture, DI, async, testable core, production-quality reliability.

## Layers & dependency direction

```
            ┌─────────────────────────────────────────────┐
            │                 App (WPF)                    │  Presentation + composition root
            │  ViewModels, Views, Tray, DI host, services  │
            └───────────────┬───────────────┬─────────────┘
                            │               │
              ┌─────────────▼───┐     ┌─────▼──────────────┐
              │    Listener     │     │      Overlay       │  Infrastructure
              │ Win notif. API  │     │  OpenVR + renderer │  (Ancs = optional alt.)
              └─────────────┬───┘     └─────┬──────────────┘
                            │               │
                        ┌───▼───────────────▼───┐
                        │         Core          │  Domain + Application
                        │  models, interfaces,  │
                        │  filter, dispatcher   │
                        └───────────────────────┘
```

`Core` depends on nothing external and knows nothing about Bluetooth, OpenVR or WPF. The outer layers
implement its interfaces (`INotificationSource`, `IOverlayService`, `ISettingsService`, …). This is what
makes the notification pipeline unit-testable without hardware.

## The pipeline

```
INotificationSource ──► AppFilter ──► NotificationDispatcher (bounded queue) ──► IOverlayService
 (Listener or Test)        │                    │                                     │
                           │                    ├──► INotificationHistory             ├──► INotificationRenderer
                           │                    └──► ISoundService                    └──► OpenVR overlay
                    whitelist/blacklist
```

* **`NotificationDispatcher`** owns a bounded `Channel<PhoneNotification>` with `DropOldest`. A single
  consumer guarantees exactly one card animates at a time and keeps memory flat under a flood; the newest
  notifications win. History records *everything* (even filtered) so nothing is silently lost.
* **`CompositeNotificationSource`** merges the real source (the Windows listener) and the in-app Test source
  behind one `INotificationSource`, so test cards and live notifications share the exact same path. It's
  source-agnostic, so swapping Listener↔ANCS needs no change here.

## Notification source — Windows listener (`src/Listener`)

The shipping source is `WindowsNotificationListenerSource`, backed by the WinRT
`UserNotificationListener`.

* iPhone notifications reach the Windows notification centre via **Microsoft Phone Link**; desktop apps
  post there directly. We read them all through one API.
* The listener's push event is unreliable for foreground Win32 apps, so we **poll** `GetNotificationsAsync`
  (~1.5 s) and diff against seen ids — cheap, and it never misses. Removed ids raise `NotificationRemoved`.
* On first poll we *prime* the seen-set without replaying existing toasts (mirrors ANCS "pre-existing"),
  so a restart doesn't dump a backlog of stale cards.
* Each `UserNotification` is mapped: app display name + AUMID, title/body from the ToastGeneric binding,
  timestamp, and a heuristic **call** category from the text.
* Requires **package identity** (sparse package — see `docs/IDENTITY.md`). A **supervisor loop** re-requests
  access and keeps going across sleep / revoked access, and reports status for the UI.

### Optional alternative — direct ANCS (`src/Ancs`)

Included but not wired in by default. Talks to the iPhone directly over Bluetooth LE as an ANCS
"Notification Consumer" (GATT client): subscribes to Notification/Data Source, requests attributes via the
Control Point, reassembles fragments, resolves app names. It's the "purest" path but needs the PC to
solicit ANCS and drive the LE bond, and depends on adapter LE-peripheral support — which is exactly why the
Phone-Link-fed listener is the default. Swapping is a one-line DI change because both implement
`INotificationSource`.

## Overlay (`src/Overlay`)

* Uses the OpenVR API (via OVRSharp's bundled `Valve.VR` binding) as an **overlay application**, so it
  coexists with any running SteamVR game.
* **Low cost**: `NotificationRenderer` draws the card to a bitmap **once**; the fade+slide animation only
  calls the cheap `SetOverlayAlpha` + `SetOverlayTransform…` per frame (~90 fps for a fraction of a second),
  then the overlay is hidden and does nothing while idle.
* **Anchors** (`OverlayPositioner`): HUD (HMD-relative), fixed world (absolute standing origin), controller
  (controller-relative), wrist (tucked closer to the controller). Controller/wrist fall back to the HMD if
  the controller is off.
* A **supervisor loop** keeps OpenVR initialised, pumps `VREvent_Quit`, and recreates the overlay after a
  SteamVR restart.

## Presentation (`src/App`)

* WPF + `CommunityToolkit.Mvvm`. `MainViewModel` exposes status, live preview, history and log; commands
  drive test/preview. `SettingsViewModel` is a two-way surface over `AppSettings` that saves on change and
  triggers a live preview refresh.
* Composition root (`App.xaml.cs`) builds the DI host, starts the overlay/ANCS/dispatcher, wires the tray
  icon, and honors autostart/minimize preferences. `SingleInstance` prevents two copies fighting over the
  radio/overlay.
* Logging goes to an in-memory sink that the **Log** window binds to directly.

## Settings

`AppSettings` (JSON at `%AppData%\PhoneNotificationsVR\settings.json`) covers position, size, opacity, font
scale, duration, animation speed, whitelist/blacklist + mode, "always show calls", sound, and general
startup options — the full spec list.

## Extension points / documented next steps

* **Notification actions** (answer/decline a call, mark-as-read): ANCS supports `PerformNotificationAction`
  via the Control Point; add a `PositiveAction/NegativeAction` write and surface a controller button on the
  overlay. The `AncsEventFlags.PositiveAction/NegativeAction` bits already tell you when actions exist.
* **Real app icons**: not available from ANCS. If ever desired, a tiny optional companion could supply icon
  artwork by bundle id — but the core notification data would still come from ANCS, not a companion.
* **Multiple simultaneous cards / stacking**: the dispatcher currently serialises; a stacking overlay would
  create N overlay handles positioned in a column.
```
