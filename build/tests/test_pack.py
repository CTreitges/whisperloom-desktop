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

    def test_ohne_kanal_kein_channel(self):
        # Stabil = Kanal wie das OS (win/linux), so wurde 1.0.0 gepackt; sonst fänden installierte 1.0.0 nichts mehr.
        for channel in (None, "stable"):
            for rid in pack.RIDS:
                self.assertNotIn("--channel", pack.vpk_cmd("1.0.1", rid, Path("p"), Path("o"), host="win", channel=channel))

    def test_beta_und_test_gehen_als_channel_je_os_an_vpk(self):
        for channel in ("beta", "test"):
            for rid, os_name in (("win-x64", "win"), ("linux-x64", "linux")):
                cmd = pack.vpk_cmd("1.1.0-beta.1", rid, Path("p"), Path("o"), host="win", channel=channel)
                self.assertEqual(cmd[cmd.index("--channel") + 1], f"{os_name}-{channel}")
                self.assertEqual(cmd.count("--channel"), 1)

    def test_unbekannter_kanal_abgelehnt(self):
        with self.assertRaises(ValueError):
            pack.channel_name("nightly", "win-x64")
        with self.assertRaises(SystemExit):
            pack.main(["--version", "1.0.0", "--rid", "win-x64", "--out", "o", "--channel", "nightly"])

    def test_release_workflow_test_version_mit_laufnummer_vor_branch(self):
        # Review W3-CHAN: SemVer vergleicht Kennungen von links; mit dem Branch vorn stünde test.main.50 über test.feat-a.51.
        workflow = (pack.ROOT / ".github" / "workflows" / "release.yml").read_text(encoding="utf-8")
        lines = [line.strip() for line in workflow.splitlines() if line.strip().startswith('version="$base-test.')]
        self.assertEqual(lines, ['version="$base-test.$RUN_NUMBER.$slug" ;;'])

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
