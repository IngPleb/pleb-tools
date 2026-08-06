import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts" / "games" / "steam-logi-ring-guard" / "steam_logi_ring_guard.py"
SPEC = importlib.util.spec_from_file_location("steam_logi_ring_guard", SCRIPT)
guard = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = guard
SPEC.loader.exec_module(guard)


class VdfTests(unittest.TestCase):
    def test_parses_nested_vdf_and_escapes(self):
        content = '''
        "libraryfolders"
        {
            "0"
            {
                "path" "C:\\\\Program Files (x86)\\\\Steam"
            }
        }
        '''
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "libraryfolders.vdf"
            path.write_text(content, encoding="utf-8")
            parsed = guard.parse_vdf(path)
        self.assertEqual(
            parsed["libraryfolders"]["0"]["path"],
            r"C:\Program Files (x86)\Steam",
        )

    def test_ignores_vdf_comments(self):
        tokens = guard.tokenize_vdf('"key" "value" // ignored\n"next" "item"')
        self.assertEqual(tokens, ["key", "value", "next", "item"])


class CandidateTests(unittest.TestCase):
    def test_filters_known_helpers(self):
        cases = {
            r"C:\Game\Launcher.exe": "launcher",
            r"C:\Game\EasyAntiCheat\EasyAntiCheat.exe": "easyanticheat",
            r"C:\Game\UnityCrashHandler64.exe": "unitycrashhandler",
            r"C:\Game\vconsole2.exe": "vconsole2",
        }
        for path, expected in cases.items():
            with self.subTest(path=path):
                self.assertEqual(guard.helper_reason(Path(path)), expected)

    def test_keeps_normal_game_executable(self):
        self.assertIsNone(guard.helper_reason(Path(r"C:\Game\game.exe")))


if __name__ == "__main__":
    unittest.main()
