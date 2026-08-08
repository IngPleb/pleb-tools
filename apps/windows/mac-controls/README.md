# Mac Controls for Windows

Mac Controls brings the fundamental macOS text-editing model to Windows while
preserving normal Windows behavior everywhere else. It is a small native .NET 9
notification-area utility with no network access, service, or third-party
runtime dependency.

## Shortcuts

| Physical keys | Windows output | Result |
| --- | --- | --- |
| Left Win + Left / Right | Home / End | Start or end of the current line |
| Left Win + Up / Down | Ctrl + Home / End | Start or end of the document |
| Left Win + Backspace | Shift + Home, Backspace | Delete to the start of the line |
| Left Alt + Left / Right | Ctrl + Left / Right | Previous or next word boundary |
| Left Alt + Up / Down | Ctrl + Up / Down | Previous or next paragraph boundary |
| Left Alt + Backspace | Ctrl + Backspace | Delete the previous word |
| Left Alt + mapped printable key | Unicode text | Type the personal symbol layer (`Left Alt + 7` is `&`) without holding AltGr |
| Left Alt + J | Unicode text | Insert `'` using the existing personal shortcut |
| Y / Z | Z / Y | Preserve the existing Y/Z swap |
| OEM 5 (`VK 220`) | OEM 102 (`VK 226`) | Preserve the existing layout-key remap |

Hold Shift with any navigation shortcut to extend the selection. Outside these
editing chords, physical Left Win behaves as Left Ctrl, so familiar macOS-style
Command+C, Command+V, Command+A, Command+Z, and application shortcuts continue
to use the normal Windows Ctrl commands. Non-printable Left Alt chords such as
Alt+Tab remain ordinary Alt shortcuts. Mapped printable Left Alt chords emit
Unicode directly, and unmapped printable chords are consumed without creating
Alt or Ctrl state. Right Win, physical Right Alt/AltGr, and both physical Ctrl
keys are unchanged.

This gives access to the Czech QWERTY-derived symbol set without reaching for
the physical Right Alt key or asking Windows to hold its coupled Ctrl+Alt state.
Left Alt+7 produces `&`, Left Alt+2 produces `@`, and Left Alt+E produces `€`.
The mapping is intentionally explicit rather than changing when the active
Windows input layout changes.

## Why PowerToys and this app have separate jobs

The inspected PowerToys profile remapped Left Win to Left Ctrl, owned five
editing shortcuts, swapped Y/Z, remapped one OEM layout key, and inserted an
apostrophe for Alt+J. PowerToys can express most individual translations, but
it cannot express the ordered `Shift + Home` then `Backspace` sequence needed
for Command+Backspace or the combined Option/symbol policy.

Running two keyboard hooks for the same chord makes the result depend on hook
order. The installer therefore transfers all recognized personal mappings above
to Mac Controls and leaves any unrelated PowerToys mappings untouched. The app
refuses to start if it detects that one of its owned mappings still exists.

## Preview and install

Requirements for installation from source: Windows 11 and the
[.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

Open PowerShell in this folder:

```powershell
.\install.ps1
.\install.ps1 -Apply
```

The first command is read-only. It identifies the active PowerToys profile and
reports exactly how many entries will move. `-Apply` then:

1. publishes the app to `%LOCALAPPDATA%\PlebTools\MacControls`;
2. backs up the exact active PowerToys profile;
3. removes only the recognized overlapping mappings;
4. records the removed JSON entries and hashes in an install receipt;
5. adds a current-user startup entry and starts PowerToys with the migrated profile;
6. starts Mac Controls and waits for an explicit healthy tray/hook signal.

Installation is transactional. A failed first install restores the exact
PowerToys backup and removes the partial startup entry. A failed upgrade also
restores the previous installed binaries and receipt. Repeated installs merge
rather than overwrite the original restoration data.

Right-click the tray icon to suspend the keyboard hook, view the shortcut
reference, or exit. Suspending or exiting also releases any synthetic Ctrl key
that the app owns, preventing a stuck modifier. Every synthetic Alt down
event is paired with a synthetic up event. Startup clears stale owned modifiers
left behind by an earlier interrupted run without imposing a hold timeout.

## Build and check

```powershell
dotnet build .\MacControls.csproj --configuration Release
dotnet run --project .\MacControls.Tests\MacControls.Tests.csproj --configuration Release
dotnet run --project .\MacControls.Tests\MacControls.Tests.csproj --configuration Release -- --integration
.\Setup.Tests.ps1
```

The default automated checks verify every output sequence and the PowerToys
conflict gate. `--integration` opens a real Windows text control and exercises a
temporary low-level hook, so it requires an interactive desktop and is kept out
of headless CI. A final acceptance pass must still use a physical keyboard in
the actual applications you care about; synthetic input is not equivalent proof
of a real low-level keyboard path.

## Rollback

Preview and apply removal with:

```powershell
.\uninstall.ps1
.\uninstall.ps1 -Apply
```

Uninstall stops Mac Controls, removes its startup entry and installed files, and
merges the transferred entries back into the current PowerToys profile. It will
not overwrite a newer mapping that now owns the same source shortcut. Backups
and audit receipts remain under `%LOCALAPPDATA%\PlebTools\MacControlsState`.

## Known Windows boundary

The app runs at normal user integrity. Like PowerToys in normal mode, Windows
may block it from injecting keystrokes into an elevated administrator window.
The app deliberately does not create an elevated scheduled task or request
administrator rights silently.
