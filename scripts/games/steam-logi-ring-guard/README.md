# Steam Logi Ring Guard

Disable the Logi Options+ Actions Ring automatically inside your games.

The tool finds installed Steam libraries, resolves the executable each game
actually runs, and creates an app-specific Logi profile that maps the Actions
Ring button to **Do Nothing**.

## Requirements

- Windows 10 or 11
- Logi Options+ with an Actions Ring-capable mouse
- Steam
- Python 3.10 or newer

No Python packages are required.

## Use

1. Close any running game.
2. Double-click `preview.cmd`.
3. Review `steam-logi-ring-preview.json` in this folder.
4. Double-click `apply.cmd` and accept the Windows administrator prompt.

Logi Options+ stops briefly while its settings are updated, then starts again.

## How game detection works

The tool reads every configured Steam library and installed app manifest. It
then ranks executables using, in order:

1. visible game windows;
2. Steam's local process history from games previously launched;
3. a conservative scan for games that have not been launched yet.

Launchers, crash reporters, anti-cheat helpers, redistributables, installers,
servers, and similar background programs are filtered out. Multiple rendering
variants can share one Logi profile.

## Games outside Steam

Pass the game's display name and real gameplay executable:

```powershell
py -3 .\steam_logi_ring_guard.py --apply --extra "Overwatch" "C:\Program Files (x86)\Overwatch\_retail_\Overwatch.exe"
```

Repeat `--extra "Name" "C:\path\game.exe"` to add more than one.

## Safety and rollback

Preview mode is read-only. Apply mode:

- refuses to run while a selected game is open;
- stops both the Options+ updater and agent;
- creates a consistent SQLite backup;
- validates database integrity after writing;
- restarts Options+;
- can be run repeatedly without creating duplicate profiles.

Backups are stored at:

```text
%LOCALAPPDATA%\LogiOptionsPlus\steam-ring-backups
```

Restore a backup from an Administrator terminal:

```powershell
py -3 .\steam_logi_ring_guard.py --restore "C:\path\settings-YYYYMMDD-HHMMSS.db"
```

## Compatibility note

Logitech does not provide a public bulk-profile API. This tool updates the local
Options+ settings database and reads Logitech's installed **Do Nothing** action
definition instead of hard-coding it. A future Options+ schema change may
require an update. Always preview after a major Options+ update.

This project is not affiliated with Logitech or Valve.
