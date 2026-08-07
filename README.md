# pleb-tools

Small tools for Windows, games, and everyday fixes.

This is a growing collection. Each tool lives in its own folder with setup,
usage, and rollback instructions.

## Windows applications

### [Mac Controls for Windows](apps/windows/mac-controls)

Use physical **Left Win** as a macOS-style Command key and **Left Alt** as an
Option key for editing controls and the active layout's AltGr symbol layer. The
setup also preserves the existing personal Y/Z, layout-key, and Alt+J mappings.

### [App Exposé](apps/windows/app-expose)

Press **Right Windows + -** to see live previews of every window belonging to the
currently focused application, then switch with the keyboard or mouse.

## Games

### [Steam Logi Ring Guard](scripts/games/steam-logi-ring-guard)

Finds the real executables behind installed Steam games and disables the Logi
Options+ Actions Ring while those games are active. It also supports explicit
executables for games outside Steam.

## Ground rules

- Preview before changing anything.
- Back up user settings before writing.
- Keep dependencies to a minimum.
- Make rollback obvious.
