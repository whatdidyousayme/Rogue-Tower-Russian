import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'mod_installer'))
import install_core as core
sys.path.insert(0, str(ROOT))
from build_mod32 import DEFAULT_GAME

class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(dir=ROOT / 'build')
        self.game = Path(self.tmp.name) / 'game'
        self.game.mkdir()
        shutil.copy2(DEFAULT_GAME / 'Rogue Tower.exe', self.game)
        managed = self.game / 'Rogue Tower_Data/Managed'
        managed.mkdir(parents=True)
        (managed / 'Assembly-CSharp.dll').touch()
        self.payload = ROOT / 'build/payload'

    def tearDown(self):
        self.tmp.cleanup()

    def test_clean_install_repair_and_reinstall(self):
        core.install(self.game, self.payload)
        self.assertTrue(core.bepinex_ready(self.game))
        (self.game / 'winhttp.dll').unlink()
        legacy = self.game / 'BepInEx/plugins/_backup'
        legacy.mkdir()
        shutil.copy2(self.payload / 'RogueTowerRussian.dll', legacy)
        backup = core.install(self.game, self.payload)
        self.assertTrue(core.bepinex_ready(self.game))
        self.assertFalse(legacy.exists())
        self.assertTrue((backup / 'legacy_plugin_backup/RogueTowerRussian.dll').is_file())
        self.assertEqual(len(list((self.game / 'BepInEx/plugins').rglob('*.dll'))), 1)
        core.uninstall(self.game)
        self.assertTrue(core.bepinex_ready(self.game))
        self.assertFalse((self.game / 'BepInEx/plugins/RogueTowerRussian.dll').exists())

    def test_missing_payload_fails_before_writing(self):
        empty = Path(self.tmp.name) / 'empty'
        empty.mkdir()
        with self.assertRaises(RuntimeError):
            core.install(self.game, empty)
        self.assertFalse((self.game / 'BepInEx').exists())

    def test_copy_error_rolls_back(self):
        core.install(self.game, self.payload)
        dll = self.game / 'BepInEx/plugins/RogueTowerRussian.dll'
        original = dll.read_bytes()
        real_copy = shutil.copy2
        def fail_once(src, dst, *args, **kwargs):
            if str(dst) == str(dll) and 'rogue-russian-' in str(src):
                raise OSError('simulated locked plugin')
            return real_copy(src, dst, *args, **kwargs)
        with patch.object(core.shutil, 'copy2', side_effect=fail_once):
            with self.assertRaises(OSError):
                core.install(self.game, self.payload)
        self.assertEqual(dll.read_bytes(), original)
        self.assertTrue(core.bepinex_ready(self.game))

    def test_wrong_architecture_and_disabled_doorstop(self):
        with self.assertRaises(RuntimeError):
            core.validate_archive(ROOT / 'bepinex_core.zip', 0x8664)
        core.install(self.game, self.payload)
        cfg = self.game / 'doorstop_config.ini'
        cfg.write_text(cfg.read_text().replace('enabled = true', 'enabled=false'))
        self.assertFalse(core.bepinex_ready(self.game))
        core.install(self.game, self.payload)
        self.assertTrue(core.bepinex_ready(self.game))

if __name__ == '__main__':
    unittest.main()
