"""Tests für build/pack.py: Befehle für publish und vpk, Version, portable ZIP. Aufruf: python -m unittest discover -s build/tests"""
from __future__ import annotations

import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import pack  # noqa: E402


class PackTest(unittest.TestCase):
    def test_windows_auf_windows_ohne_direktive_mit_eigener_portable(self):
        cmd = pack.vpk_cmd("0.9.0", "win-x64", Path("p"), Path("o"), host="win")
        self.assertEqual(cmd[:3], ["dotnet", "vpk", "pack"])
        self.assertIn("--noPortable", cmd)
        self.assertEqual(cmd[cmd.index("--mainExe") + 1], "WhisperLoom.exe")
        self.assertTrue(cmd[cmd.index("--icon") + 1].endswith("whisperloom.ico"))
        self.assertEqual(cmd[cmd.index("--packVersion") + 1], "0.9.0")

    def test_linux_von_windows_braucht_direktive(self):
        cmd = pack.vpk_cmd("0.9.0", "linux-x64", Path("p"), Path("o"), host="win")
        self.assertEqual(cmd[:4], ["dotnet", "vpk", "[linux]", "pack"])
        self.assertEqual(cmd[cmd.index("--mainExe") + 1], "WhisperLoom")
        self.assertTrue(cmd[cmd.index("--icon") + 1].endswith("whisperloom.png"))
        self.assertNotIn("--noPortable", cmd)

    def test_linux_auf_linux_ohne_direktive(self):
        self.assertEqual(pack.vpk_cmd("1.0.0", "linux-x64", Path("p"), Path("o"), host="linux")[2], "pack")

    def test_publish_setzt_version_und_rid(self):
        cmd = pack.publish_cmd("0.9.1", "linux-x64", Path("pub"))
        self.assertIn("-p:Version=0.9.1", cmd)
        self.assertEqual(cmd[cmd.index("-r") + 1], "linux-x64")
        self.assertIn("--self-contained", cmd)

    def test_publish_mit_readytorun(self):
        # PERF-3: vorkompiliert (weniger JIT beim Start und beim ersten Öffnen jeder Seite), für beide RIDs.
        for rid in pack.RIDS:
            cmd = pack.publish_cmd("1.0.0", rid, Path("pub"))
            self.assertIn("-p:PublishReadyToRun=true", cmd)
            self.assertIn("--self-contained", cmd)  # Composite nur self-contained
        # Windows ohne Composite (Microsoft-Signaturen der Laufzeit bleiben), Linux mit.
        self.assertNotIn("-p:PublishReadyToRunComposite=true", pack.publish_cmd("1.0.0", "win-x64", Path("pub")))
        self.assertIn("-p:PublishReadyToRunComposite=true", pack.publish_cmd("1.0.0", "linux-x64", Path("pub")))

    def test_version_muss_semver_sein(self):
        self.assertEqual(pack.check_version("0.0.1-dryrun.7"), "0.0.1-dryrun.7")
        for bad in ("v0.9.0", "0.9", "0.9.0+sha", ""):
            with self.assertRaises(ValueError):
                pack.check_version(bad)

    def test_portable_zip_ohne_pdb_und_ohne_oberordner(self):
        with tempfile.TemporaryDirectory() as tmp:
            src = Path(tmp) / "pub"
            (src / "sub").mkdir(parents=True)
            (src / "WhisperLoom.exe").write_bytes(b"x")
            (src / "WhisperLoom.pdb").write_bytes(b"x")
            (src / "sub" / "lib.dll").write_bytes(b"x")
            target = Path(tmp) / "out" / pack.portable_name("0.9.0", "win-x64")
            self.assertEqual(pack.zip_dir(src, target), 2)
            self.assertEqual(target.name, "WhisperLoom-0.9.0-win-x64-portable.zip")
            with zipfile.ZipFile(target) as z:
                self.assertEqual(sorted(z.namelist()), ["WhisperLoom.exe", "sub/lib.dll"])


if __name__ == "__main__":
    unittest.main()
