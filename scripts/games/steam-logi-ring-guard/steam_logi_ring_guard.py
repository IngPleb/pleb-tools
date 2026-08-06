#!/usr/bin/env python3
"""Create Logi Options+ game profiles that disable the Actions Ring.

The script is intentionally Windows-only and has no third-party dependencies.
Preview mode is read-only. Apply mode stops Logi Options+, creates a consistent
SQLite backup, and adds or updates one app-specific profile per installed Steam
game. The profile maps the Actions Ring button to Logitech's own "Do Nothing"
card while leaving other assignments unchanged.
"""

from __future__ import annotations

import argparse
import copy
import ctypes
import difflib
import json
import math
import os
import re
import shutil
import sqlite3
import subprocess
import sys
import time
import uuid
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any, Iterable

if os.name != "nt":
    raise SystemExit("This script supports Windows only.")

import winreg  # noqa: E402  (Windows-only import)


LOGI_DB = Path(os.environ["LOCALAPPDATA"]) / "LogiOptionsPlus" / "settings.db"
LOGI_SERVICE = "OptionsPlusUpdaterService"
REPORT_NAME = "steam-logi-ring-preview.json"

GAME_PROCESS_RE = re.compile(
    r'AppID\s+(?P<appid>\d+).*?""(?P<exe>[^"\r\n]+?\.exe)(?:"|\s|$)',
    re.IGNORECASE,
)

HELPER_PARTS = {
    "anticheat",
    "battleye",
    "beclient",
    "beservice",
    "cefsharp",
    "crash",
    "crashpad",
    "diagnostic",
    "easyanticheat",
    "helper",
    "install",
    "language",
    "launcher",
    "overlay",
    "prereq",
    "redist",
    "report",
    "server",
    "setup",
    "unins",
    "uninstall",
    "unitycrashhandler",
    "updater",
    "vc_redist",
}

HELPER_STEMS = {
    "launch",
    "playgtav",
    "vconsole2",
}

# These manifests are Steam runtime/tool packages, not games whose windows need
# an Options+ profile.
EXCLUDED_APP_IDS = {"228980", "250820"}

NON_GAME_DIRS = {
    "_commonredist",
    "anticheat",
    "battleye",
    "crashreportclient",
    "dotnet",
    "easyanticheat",
    "engine\\binaries\\thirdparty",
    "launcher",
    "redist",
    "support",
}


@dataclass
class Candidate:
    path: Path
    score: float = 0.0
    sources: set[str] = field(default_factory=set)
    history_count: int = 0
    excluded_reason: str | None = None


@dataclass
class SteamGame:
    appid: str
    name: str
    install_dir: Path
    manifest: Path
    executables: list[Candidate] = field(default_factory=list)
    selected: list[Candidate] = field(default_factory=list)


class VdfError(ValueError):
    pass


def normalize_path(path: str | Path) -> str:
    return os.path.normcase(os.path.abspath(os.path.normpath(str(path))))


def is_relative_to(path: str | Path, parent: str | Path) -> bool:
    try:
        Path(normalize_path(path)).relative_to(Path(normalize_path(parent)))
        return True
    except ValueError:
        return False


def tokenize_vdf(text: str) -> list[str]:
    tokens: list[str] = []
    i = 0
    while i < len(text):
        if text[i].isspace():
            i += 1
            continue
        if text.startswith("//", i):
            end = text.find("\n", i)
            i = len(text) if end < 0 else end + 1
            continue
        if text[i] in "{}":
            tokens.append(text[i])
            i += 1
            continue
        if text[i] != '"':
            end = i
            while end < len(text) and not text[end].isspace() and text[end] not in "{}":
                end += 1
            tokens.append(text[i:end])
            i = end
            continue
        i += 1
        value: list[str] = []
        while i < len(text):
            if text[i] == '"':
                i += 1
                break
            if text[i] == "\\" and i + 1 < len(text) and text[i + 1] in {'"', "\\"}:
                value.append(text[i + 1])
                i += 2
            else:
                value.append(text[i])
                i += 1
        else:
            raise VdfError("Unterminated quoted string")
        tokens.append("".join(value))
    return tokens


def parse_vdf(path: Path) -> dict[str, Any]:
    tokens = tokenize_vdf(path.read_text(encoding="utf-8-sig", errors="replace"))
    index = 0

    def parse_object(expect_close: bool) -> dict[str, Any]:
        nonlocal index
        result: dict[str, Any] = {}
        while index < len(tokens):
            token = tokens[index]
            if token == "}":
                if not expect_close:
                    raise VdfError(f"Unexpected closing brace in {path}")
                index += 1
                return result
            if token == "{":
                raise VdfError(f"Unexpected opening brace in {path}")
            key = token
            index += 1
            if index >= len(tokens):
                raise VdfError(f"Missing value for {key!r} in {path}")
            if tokens[index] == "{":
                index += 1
                value: Any = parse_object(True)
            else:
                value = tokens[index]
                index += 1
            result[key] = value
        if expect_close:
            raise VdfError(f"Missing closing brace in {path}")
        return result

    parsed = parse_object(False)
    return parsed


def registry_steam_path() -> Path:
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as key:
        value, _ = winreg.QueryValueEx(key, "SteamPath")
    path = Path(value)
    if not path.is_dir():
        raise FileNotFoundError(f"Steam path does not exist: {path}")
    return path


def steam_libraries(steam_path: Path) -> list[Path]:
    libraries = [steam_path]
    config = steam_path / "steamapps" / "libraryfolders.vdf"
    if config.is_file():
        root = parse_vdf(config)
        folders = root.get("libraryfolders", root)
        if isinstance(folders, dict):
            for key, value in folders.items():
                if not str(key).isdigit():
                    continue
                raw_path = value.get("path") if isinstance(value, dict) else value
                if raw_path:
                    libraries.append(Path(str(raw_path)))
    unique: dict[str, Path] = {}
    for library in libraries:
        unique[normalize_path(library)] = library
    return list(unique.values())


def installed_games(steam_path: Path) -> list[SteamGame]:
    games: dict[str, SteamGame] = {}
    for library in steam_libraries(steam_path):
        steamapps = library / "steamapps"
        if not steamapps.is_dir():
            continue
        for manifest in steamapps.glob("appmanifest_*.acf"):
            try:
                root = parse_vdf(manifest)
                state = root.get("AppState", root)
                appid = str(state["appid"])
                name = str(state.get("name") or f"Steam App {appid}")
                install_dir = steamapps / "common" / str(state["installdir"])
            except (KeyError, OSError, VdfError) as exc:
                print(f"Warning: skipped malformed manifest {manifest}: {exc}", file=sys.stderr)
                continue
            if appid not in EXCLUDED_APP_IDS and install_dir.is_dir():
                games[appid] = SteamGame(appid, name, install_dir, manifest)
    return sorted(games.values(), key=lambda game: game.name.casefold())


def steam_process_history(steam_path: Path) -> dict[str, Counter[str]]:
    history: dict[str, Counter[str]] = defaultdict(Counter)
    logs = steam_path / "logs"
    for name in ("gameprocess_log.txt", "gameprocess_log.previous.txt", "console_log.txt", "console_log.previous.txt"):
        path = logs / name
        if not path.is_file():
            continue
        for line in path.read_text(encoding="utf-8", errors="ignore").splitlines():
            match = GAME_PROCESS_RE.search(line)
            if not match:
                continue
            exe = match.group("exe").strip()
            if os.path.isabs(exe):
                history[match.group("appid")][normalize_path(exe)] += 1
    return history


def visible_window_executables() -> set[str]:
    user32 = ctypes.windll.user32
    kernel32 = ctypes.windll.kernel32
    process_query_limited_information = 0x1000
    paths: set[str] = set()

    enum_proc_type = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)

    def callback(hwnd: int, _: int) -> bool:
        if not user32.IsWindowVisible(hwnd) or user32.GetWindowTextLengthW(hwnd) <= 0:
            return True
        pid = ctypes.c_ulong()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        handle = kernel32.OpenProcess(process_query_limited_information, False, pid.value)
        if not handle:
            return True
        try:
            size = ctypes.c_ulong(32768)
            buffer = ctypes.create_unicode_buffer(size.value)
            if kernel32.QueryFullProcessImageNameW(handle, 0, buffer, ctypes.byref(size)):
                paths.add(normalize_path(buffer.value))
        finally:
            kernel32.CloseHandle(handle)
        return True

    callback_ref = enum_proc_type(callback)
    user32.EnumWindows(callback_ref, 0)
    return paths


def helper_reason(path: Path) -> str | None:
    lowered = str(path).casefold()
    stem = path.stem.casefold()
    if stem in HELPER_STEMS:
        return stem
    if stem.endswith("_be"):
        return "battleye-bootstrap"
    for directory in sorted(NON_GAME_DIRS, key=len, reverse=True):
        if directory in lowered:
            return directory
    for part in sorted(HELPER_PARTS, key=len, reverse=True):
        if part in stem:
            return part
    return None


def similarity_score(game: SteamGame, exe: Path) -> float:
    game_text = re.sub(r"[^a-z0-9]+", "", game.name.casefold())
    dir_text = re.sub(r"[^a-z0-9]+", "", game.install_dir.name.casefold())
    exe_text = re.sub(r"[^a-z0-9]+", "", exe.stem.casefold())
    if not exe_text:
        return 0.0
    return 45.0 * max(
        difflib.SequenceMatcher(None, game_text, exe_text).ratio(),
        difflib.SequenceMatcher(None, dir_text, exe_text).ratio(),
    )


def collect_candidates(
    game: SteamGame,
    history: Counter[str],
    visible_exes: set[str],
) -> None:
    candidates: dict[str, Candidate] = {}

    def add(path: Path, source: str, count: int = 0) -> None:
        normalized = normalize_path(path)
        if not is_relative_to(normalized, game.install_dir) or not normalized.endswith(".exe"):
            return
        actual = Path(normalized)
        if not actual.is_file():
            return
        candidate = candidates.setdefault(normalized, Candidate(actual))
        candidate.sources.add(source)
        candidate.history_count += count

    for path, count in history.items():
        add(Path(path), "steam-history", count)
    for path in visible_exes:
        add(Path(path), "visible-window")
    try:
        for path in game.install_dir.rglob("*.exe"):
            add(path, "folder-scan")
    except OSError as exc:
        print(f"Warning: could not fully scan {game.install_dir}: {exc}", file=sys.stderr)

    base_depth = len(game.install_dir.parts)
    for candidate in candidates.values():
        path = candidate.path
        candidate.excluded_reason = helper_reason(path)
        try:
            size = path.stat().st_size
        except OSError:
            size = 0
        depth = max(0, len(path.parts) - base_depth - 1)
        candidate.score = similarity_score(game, path) - depth * 2.0
        candidate.score += min(35.0, math.log2(max(size, 1)) * 1.5)
        if "steam-history" in candidate.sources:
            candidate.score += 160.0 + min(80.0, math.log2(candidate.history_count + 1) * 12.0)
        if "visible-window" in candidate.sources:
            candidate.score += 500.0
        if candidate.excluded_reason:
            candidate.score -= 400.0

    game.executables = sorted(candidates.values(), key=lambda item: (-item.score, str(item.path)))
    eligible = [item for item in game.executables if not item.excluded_reason]
    if not eligible:
        game.selected = []
        return

    observed = [
        item
        for item in eligible
        if "visible-window" in item.sources or "steam-history" in item.sources
    ]
    selected: list[Candidate] = []
    for item in observed:
        if item not in selected:
            selected.append(item)

    best = eligible[0]
    if best not in selected:
        selected.append(best)

    # Multiple rendering variants (DX11/DX12/VR) can all become foreground apps.
    # Keep close-scoring alternatives, but cap the profile to avoid helper noise.
    for item in eligible[1:]:
        if len(selected) >= 4:
            break
        if item in selected:
            continue
        if item.score >= best.score - 12.0 and item.score >= 35.0:
            selected.append(item)
    game.selected = sorted(selected, key=lambda item: (-item.score, str(item.path)))


def discover_games() -> tuple[Path, list[SteamGame]]:
    steam_path = registry_steam_path()
    games = installed_games(steam_path)
    history = steam_process_history(steam_path)
    visible = visible_window_executables()
    for game in games:
        collect_candidates(game, history.get(game.appid, Counter()), visible)
    return steam_path, games


def report_data(steam_path: Path, games: list[SteamGame]) -> dict[str, Any]:
    return {
        "generatedAt": datetime.now().astimezone().isoformat(timespec="seconds"),
        "steamPath": str(steam_path),
        "logiDatabase": str(LOGI_DB),
        "games": [
            {
                "appid": game.appid,
                "name": game.name,
                "installDir": str(game.install_dir),
                "selectedExecutables": [
                    {
                        "path": str(candidate.path),
                        "score": round(candidate.score, 1),
                        "sources": sorted(candidate.sources),
                        "historyCount": candidate.history_count,
                    }
                    for candidate in game.selected
                ],
                "excludedCandidates": [
                    {"path": str(candidate.path), "reason": candidate.excluded_reason}
                    for candidate in game.executables
                    if candidate.excluded_reason
                ],
            }
            for game in games
        ],
    }


def write_report(path: Path, steam_path: Path, games: list[SteamGame]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report_data(steam_path, games), indent=2), encoding="utf-8")


def is_admin() -> bool:
    return bool(ctypes.windll.shell32.IsUserAnAdmin())


def relaunch_elevated() -> None:
    executable = sys.executable
    script = str(Path(__file__).resolve())
    arguments = subprocess.list2cmdline([script, *sys.argv[1:]])
    result = ctypes.windll.shell32.ShellExecuteW(None, "runas", executable, arguments, None, 1)
    if result <= 32:
        raise PermissionError("Administrator elevation was cancelled or failed.")


def service_state() -> str:
    result = subprocess.run(
        ["sc.exe", "query", LOGI_SERVICE],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    match = re.search(r"STATE\s*:\s*\d+\s+(\w+)", result.stdout)
    return match.group(1) if match else "UNKNOWN"


def stop_logi() -> bool:
    was_running = service_state() == "RUNNING"
    subprocess.run(
        ["taskkill.exe", "/IM", "logioptionsplus.exe", "/T", "/F"],
        capture_output=True,
        text=True,
    )
    if was_running:
        result = subprocess.run(
            ["sc.exe", "stop", LOGI_SERVICE],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        if result.returncode not in (0, 1062):
            raise RuntimeError(f"Could not stop {LOGI_SERVICE}:\n{result.stdout}\n{result.stderr}")
        deadline = time.monotonic() + 20
        while service_state() not in {"STOPPED", "UNKNOWN"} and time.monotonic() < deadline:
            time.sleep(0.25)
        if service_state() != "STOPPED":
            raise TimeoutError(f"Timed out stopping {LOGI_SERVICE}")

    # The updater service can stop without terminating its already-spawned
    # agent. Kill that orphan only after the updater is down so it cannot be
    # immediately respawned while the database is being changed.
    subprocess.run(
        ["taskkill.exe", "/IM", "logioptionsplus_agent.exe", "/T", "/F"],
        capture_output=True,
        text=True,
    )
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        result = subprocess.run(
            ["tasklist.exe", "/FI", "IMAGENAME eq logioptionsplus_agent.exe", "/NH"],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        if "logioptionsplus_agent.exe" not in result.stdout.casefold():
            break
        time.sleep(0.2)
    else:
        raise TimeoutError("Timed out stopping the Logi Options+ agent")
    return was_running


def start_logi() -> None:
    result = subprocess.run(
        ["sc.exe", "start", LOGI_SERVICE],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if result.returncode not in (0, 1056):
        raise RuntimeError(f"Could not restart {LOGI_SERVICE}:\n{result.stdout}\n{result.stderr}")


def running_selected_games(games: list[SteamGame]) -> list[str]:
    selected_names = {candidate.path.name.casefold(): game.name for game in games for candidate in game.selected}
    tasklist = subprocess.run(
        ["tasklist.exe", "/FO", "CSV", "/NH"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    ).stdout.casefold()
    return sorted({name for exe, name in selected_names.items() if f'"{exe}",' in tasklist})


def load_logi_json(connection: sqlite3.Connection) -> tuple[int, dict[str, Any]]:
    row = connection.execute("SELECT _id, file FROM data ORDER BY _id LIMIT 1").fetchone()
    if not row:
        raise RuntimeError("Logi settings database has no data row")
    raw = row[1]
    if isinstance(raw, bytes):
        raw = raw.decode("utf-8")
    data = json.loads(raw)
    if not isinstance(data.get("applications", {}).get("applications"), list):
        raise RuntimeError("Unsupported Logi Options+ schema: applications list is missing")
    if not isinstance(data.get("profile_keys"), list):
        raise RuntimeError("Unsupported Logi Options+ schema: profile_keys is missing")
    return int(row[0]), data


def find_default_profile(data: dict[str, Any]) -> dict[str, Any]:
    for key in data["profile_keys"]:
        profile = data.get(key)
        if isinstance(profile, dict) and profile.get("name") == "PROFILE_NAME_DEFAULT":
            return profile
    raise RuntimeError("Could not find the default Logi Options+ profile")


def find_do_nothing_card() -> dict[str, Any]:
    program_data = Path(os.environ["PROGRAMDATA"]) / "LogiOptionsPlus" / "depots"
    preset_paths = sorted(
        program_data.glob("*/logioptionsplus/data/card_presets/card_presets_win.json"),
        key=lambda path: path.stat().st_mtime,
        reverse=True,
    )
    for preset_path in preset_paths:
        try:
            content = json.loads(preset_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue

        def walk(value: Any) -> Iterable[dict[str, Any]]:
            if isinstance(value, dict):
                yield value
                for child in value.values():
                    yield from walk(child)
            elif isinstance(value, list):
                for child in value:
                    yield from walk(child)

        for card in walk(content):
            if card.get("id") == "card_global_presets_do_nothing" and card.get("macro", {}).get("type") == "DO_NOTHING":
                return copy.deepcopy(card)
    raise RuntimeError("Could not find Logitech's Do Nothing action card")


def disable_ring(profile: dict[str, Any], do_nothing: dict[str, Any]) -> int:
    assignments = profile.get("assignments")
    if not isinstance(assignments, list):
        raise RuntimeError("Unsupported Logi profile schema: assignments is missing")
    changed = 0
    for assignment in assignments:
        card = assignment.get("card", {})
        tags = set(card.get("tags", []))
        action = card.get("macro", {}).get("system", {}).get("action")
        is_ring = (
            action == "SHOW_RADIAL_MENU"
            or card.get("id") == "card_global_presets_show_radial_menu"
            or "PRESET_TAG_LPS_ACTION_RING" in tags
        )
        if is_ring:
            assignment["card"] = copy.deepcopy(do_nothing)
            assignment["cardId"] = do_nothing["id"]
            changed += 1
    return changed


def existing_application(data: dict[str, Any], paths: set[str]) -> dict[str, Any] | None:
    for application in data["applications"]["applications"]:
        app_paths = list(application.get("applicationPathsList", []))
        if application.get("applicationPath"):
            app_paths.append(application["applicationPath"])
        if paths.intersection(normalize_path(path) for path in app_paths):
            return application
    return None


def upsert_profiles(data: dict[str, Any], games: list[SteamGame]) -> tuple[int, int, int]:
    default_profile = find_default_profile(data)
    do_nothing = find_do_nothing_card()
    created = 0
    updated = 0
    skipped = 0

    for game in games:
        if not game.selected:
            skipped += 1
            continue
        selected_paths = {normalize_path(candidate.path) for candidate in game.selected}
        application = existing_application(data, selected_paths)
        if application is None:
            app_id = str(uuid.uuid4())
            ordered_paths = sorted(selected_paths)
            application = {
                "applicationFolder": normalize_path(game.install_dir),
                "applicationId": app_id,
                "applicationPath": ordered_paths[0],
                "applicationPathsList": ordered_paths,
                "isCustom": True,
                "isInstalled": True,
                "name": game.name,
            }
            data["applications"]["applications"].append(application)
        else:
            app_id = str(application["applicationId"])
            merged = {normalize_path(path) for path in application.get("applicationPathsList", [])}
            if application.get("applicationPath"):
                merged.add(normalize_path(application["applicationPath"]))
            merged.update(selected_paths)
            application["applicationPathsList"] = sorted(merged)
            application["isInstalled"] = True

        profile_key = next(
            (
                key
                for key in data["profile_keys"]
                if isinstance(data.get(key), dict) and data[key].get("applicationId") == app_id
            ),
            None,
        )
        if profile_key is None:
            profile = copy.deepcopy(default_profile)
            profile_id = str(uuid.uuid4())
            profile_key = f"profile-{profile_id}"
            profile["id"] = profile_id
            profile["applicationId"] = app_id
            profile["name"] = game.name
            profile["activeForApplication"] = True
            data[profile_key] = profile
            data["profile_keys"].append(profile_key)
            created += 1
        else:
            profile = data[profile_key]
            updated += 1

        if disable_ring(profile, do_nothing) == 0:
            # A rerun is valid when this profile is already mapped to Do Nothing.
            already_disabled = any(
                assignment.get("card", {}).get("macro", {}).get("type") == "DO_NOTHING"
                and assignment.get("slotId", "").endswith("_c416")
                for assignment in profile.get("assignments", [])
            )
            if not already_disabled:
                raise RuntimeError(f"No Actions Ring assignment found in profile for {game.name}")

    return created, updated, skipped


def backup_database(connection: sqlite3.Connection) -> Path:
    backup_dir = LOGI_DB.parent / "steam-ring-backups"
    backup_dir.mkdir(parents=True, exist_ok=True)
    backup = backup_dir / f"settings-{datetime.now():%Y%m%d-%H%M%S}.db"
    with sqlite3.connect(backup) as destination:
        connection.backup(destination)
        result = destination.execute("PRAGMA integrity_check").fetchone()[0]
        if result != "ok":
            raise RuntimeError(f"Backup integrity check failed: {result}")
    return backup


def apply_profiles(games: list[SteamGame]) -> None:
    if not LOGI_DB.is_file():
        raise FileNotFoundError(f"Logi Options+ settings database not found: {LOGI_DB}")
    running = running_selected_games(games)
    if running:
        raise RuntimeError(
            "Refusing to restart Logi Options+ while these games are running: " + ", ".join(running)
        )

    was_running = stop_logi()
    try:
        with sqlite3.connect(LOGI_DB) as connection:
            backup = backup_database(connection)
            row_id, data = load_logi_json(connection)
            created, updated, skipped = upsert_profiles(data, games)
            payload = json.dumps(data, indent=2, ensure_ascii=False)
            connection.execute("UPDATE data SET file = ? WHERE _id = ?", (payload, row_id))
            integrity = connection.execute("PRAGMA integrity_check").fetchone()[0]
            if integrity != "ok":
                raise RuntimeError(f"Database integrity check failed: {integrity}")
            connection.commit()
        print(f"Backup: {backup}")
        print(f"Profiles created: {created}; updated: {updated}; games skipped: {skipped}")
    finally:
        if was_running:
            start_logi()


def restore_database(backup: Path) -> None:
    if not backup.is_file():
        raise FileNotFoundError(f"Backup does not exist: {backup}")
    with sqlite3.connect(f"file:{backup}?mode=ro", uri=True) as connection:
        integrity = connection.execute("PRAGMA integrity_check").fetchone()[0]
        if integrity != "ok":
            raise RuntimeError(f"Backup integrity check failed: {integrity}")
    was_running = stop_logi()
    try:
        shutil.copy2(backup, LOGI_DB)
        for suffix in ("-wal", "-shm"):
            sidecar = Path(str(LOGI_DB) + suffix)
            if sidecar.exists():
                sidecar.unlink()
        print(f"Restored Logi Options+ settings from {backup}")
    finally:
        if was_running:
            start_logi()


def print_summary(games: list[SteamGame]) -> None:
    found = sum(bool(game.selected) for game in games)
    observed = sum(
        any("visible-window" in candidate.sources or "steam-history" in candidate.sources for candidate in game.selected)
        for game in games
    )
    print(f"Installed Steam games: {len(games)}")
    print(f"Games with selected executables: {found}")
    print(f"Games backed by actual Steam/window history: {observed}")
    for game in games:
        paths = ", ".join(candidate.path.name for candidate in game.selected) or "NO SAFE CANDIDATE"
        print(f"[{game.appid}] {game.name}: {paths}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    action = parser.add_mutually_exclusive_group()
    action.add_argument("--apply", action="store_true", help="Back up and update Logi Options+ profiles")
    action.add_argument("--restore", type=Path, metavar="BACKUP", help="Restore a backup made by this script")
    parser.add_argument(
        "--report",
        type=Path,
        default=Path(__file__).resolve().with_name(REPORT_NAME),
        help="Preview report path (default: next to this script)",
    )
    parser.add_argument(
        "--extra",
        nargs=2,
        action="append",
        default=[],
        metavar=("NAME", "EXE"),
        help="Also configure a non-Steam game using an explicit gameplay executable",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if (args.apply or args.restore) and not is_admin():
        relaunch_elevated()
        return 0

    if args.restore:
        restore_database(args.restore.resolve())
        return 0

    steam_path, games = discover_games()
    for name, raw_exe in args.extra:
        exe = Path(os.path.expandvars(raw_exe)).resolve()
        if not exe.is_file() or exe.suffix.casefold() != ".exe":
            raise FileNotFoundError(f"Extra gameplay executable does not exist: {exe}")
        game = SteamGame(f"extra:{exe.stem.casefold()}", name, exe.parent, Path())
        candidate = Candidate(exe, score=1000.0, sources={"explicit"})
        game.executables = [candidate]
        game.selected = [candidate]
        games.append(game)
    games.sort(key=lambda game: game.name.casefold())
    write_report(args.report.resolve(), steam_path, games)
    print_summary(games)
    print(f"Preview report: {args.report.resolve()}")
    if args.apply:
        apply_profiles(games)
    else:
        print("Preview only. Run again with --apply to change Logi Options+.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise SystemExit(1)
