# App Exposé

App Exposé is a small Windows 11 utility that shows the open windows belonging
to the application you are currently using. It keeps the familiar full-screen,
live-preview feel of Windows Task View while narrowing the view to one app, like
macOS App Exposé.

## Use it

Requirements: Windows 11 and the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

1. Double-click `run.cmd`. It builds and starts the app; a small icon remains in
   the notification area.
2. Focus an application with multiple windows.
3. Press **Right Windows + -**. The main-row, numpad, and detected
   layout-specific minus keys are supported.
4. Use the arrow keys or Tab to select a live preview, then press Enter. You can
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

App Exposé prefers Windows AppUserModelID so a browser and an installed web app
do not get mixed together even when they share a process. Older desktop apps
that publish no application identity fall back to process ownership.

## Build and check

```powershell
dotnet build .\AppExpose.csproj --configuration Release
dotnet run --project .\AppExpose.Tests\AppExpose.Tests.csproj --configuration Release
```

## Rollback

Right-click the notification-area icon and choose **Exit**, then delete this
folder. App Exposé does not add startup entries, services, registry values, or
scheduled tasks.
