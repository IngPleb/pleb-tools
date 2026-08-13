# App Exposé

App Exposé is a small Windows 11 utility that shows the open windows belonging
to the application you are currently using. It keeps the familiar full-screen,
live-preview feel of Windows Task View while narrowing the view to one app, like
macOS App Exposé.

## Use it

Requirements: Windows 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

For a temporary source-tree run, double-click `run.cmd`. For a normal per-user
installation, open PowerShell in this folder:

```powershell
.\install.ps1
.\install.ps1 -Apply
```

The first command is read-only. `-Apply` publishes the app under
`%LOCALAPPDATA%\PlebTools\AppExpose`, registers it for current-user startup,
starts it, and waits for the tray icon and shortcut hook to report healthy.
Installation and upgrades restore the previous binaries and startup entry if
startup verification fails.

After the app starts:

1. Focus an application with multiple windows.
2. Press **Right Windows + -**. The main-row, numpad, and detected
   layout-specific minus keys are supported.
3. Use the arrow keys or Tab to select a live preview, then press Enter. You can
   also click a preview. Escape or clicking elsewhere dismisses the overview.

The left Windows key is not part of this binding. Right Windows by itself and
other shortcuts such as Right Windows + E continue to work normally.

Double-click the notification-area icon to invoke the overview. Right-click it
and choose **Exit** to stop the utility.

## How it works

The global shortcut records the foreground window, reads the application
identity Windows assigns to it, and asks Desktop Window Manager for live
thumbnails of matching top-level windows. It also keeps a frozen, in-memory
capture of the desktop behind the overview, then strongly blurs it beneath a
dark neutral scrim so the real windows are visually hidden. Selecting a thumbnail restores
and activates the original window. No screenshots are saved and no window
contents leave the machine.

Each preview begins at its real window bounds, moves into a compact Task View
row layout over 300 milliseconds, and follows the same path backward when the
overview closes. The layout scales to the available work area so window contents
remain readable instead of being held to a small fixed preview height.

Preview title bars use dark translucent chrome with high-contrast white text.
The overlay declares Per-Monitor V2 DPI awareness, rounds WPF layout to device
pixels, and sends DWM thumbnails directly to physical-pixel destinations so the
interface stays crisp across mixed Full HD, high-DPI, and 4K monitors. Enlarging
a source window beyond its own rendered pixel size can still reveal normal
compositor upscaling because Windows cannot synthesize detail the source window
did not render.

App Exposé prefers Windows AppUserModelID so a browser and an installed web app
do not get mixed together even when they share a process. Older desktop apps
that publish no application identity fall back to process ownership.

## Build and check

```powershell
dotnet build .\AppExpose.csproj --configuration Release
dotnet run --project .\AppExpose.Tests\AppExpose.Tests.csproj --configuration Release
```

## Rollback

Preview and apply removal with:

```powershell
.\uninstall.ps1
.\uninstall.ps1 -Apply
```

Uninstall stops App Exposé and removes its current-user startup entry and
installed files. Its audit receipt remains under
`%LOCALAPPDATA%\PlebTools\AppExposeState`. No service or scheduled task is used.
