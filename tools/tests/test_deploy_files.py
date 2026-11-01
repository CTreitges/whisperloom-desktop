"""Tests für die Deploy-Vorlage der Demo-Instanz (server/deploy, W2-DEMO): Ports, Adressen und Anmeldeart passen
zusammen, keine Freigabe für 127.0.0.1, und der Wrapper wl-admin lädt die Umgebung und reicht Argumente unverändert durch."""
from __future__ import annotations

import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

DEPLOY = Path(__file__).resolve().parents[2] / "server" / "deploy"
SH = shutil.which("sh")


def env_values() -> dict[str, str]:
    lines = (DEPLOY / "env.example").read_text(encoding="utf-8").splitlines()
    return dict(line.split("=", 1) for line in lines if line and not line.startswith("#"))


class TemplateTest(unittest.TestCase):
    def test_server_unit_caddy_and_env_agree(self):
        unit = (DEPLOY / "whisperloom-admin.service").read_text(encoding="utf-8")
        caddy = (DEPLOY / "Caddyfile.block").read_text(encoding="utf-8")
        port = re.search(r"--host 127\.0\.0\.1 --port (\d+)", unit).group(1)
        self.assertIn(f"reverse_proxy 127.0.0.1:{port}", caddy)
        self.assertIn("import subsite", caddy)
        env = env_values()
        self.assertEqual("password_totp", env["WL_AUTH_MODES"])
        self.assertTrue(env["WL_BASE_URL"].startswith("https://whisperloom-admin."))
        self.assertEqual("127.0.0.1/32", env["WL_TRUSTED_PROXIES"])
        self.assertFalse(env["WL_MASTER_KEY_FILE"].startswith(env["WL_DATA_DIR"]), "Schlüssel nicht im Datenordner")

    def test_upstream_allow_is_exactly_the_fake_address(self):
        env = env_values()
        fake_unit = (DEPLOY / "whisperloom-admin-fake.service").read_text(encoding="utf-8")
        host, port = re.search(r"--host (\S+) --port (\d+)", fake_unit).groups()
        self.assertEqual(f"{host}:{port}", env["WL_DEMO_FAKE"])
        self.assertEqual(f"{host}/32", env["WL_UPSTREAM_ALLOW"])
        self.assertNotEqual("127.0.0.1", host)

    def test_no_secrets_in_templates(self):
        for path in DEPLOY.iterdir():
            text = path.read_text(encoding="utf-8")
            self.assertNotRegex(text, r"sk-[A-Za-z0-9_-]{20,}|gsk_[A-Za-z0-9]{20,}|AIza[0-9A-Za-z_-]{20,}", path.name)
            for key in re.findall(r"test-key[\w-]*", text):
                self.assertRegex(key, r"^test-key-not-real-\d+$", path.name)

    def test_units_have_no_trailing_comments(self):
        """systemd kennt Kommentare nur am Zeilenanfang."""
        for name in ("whisperloom-admin.service", "whisperloom-admin-fake.service"):
            for line in (DEPLOY / name).read_text(encoding="utf-8").splitlines():
                if "=" in line and not line.startswith("#"):
                    self.assertNotIn(" #", line, f"{name}: {line}")


@unittest.skipUnless(SH, "kein sh")
class WlAdminTest(unittest.TestCase):
    def run_wrapper(self, *args: str) -> subprocess.CompletedProcess:
        with tempfile.TemporaryDirectory() as d:
            base, conf = Path(d) / "base", Path(d) / "conf"
            (base / "app" / "server").mkdir(parents=True)
            (base / "venv" / "bin").mkdir(parents=True)
            conf.mkdir()
            (conf / "env").write_text("WL_DEMO_FAKE=127.0.0.2:8100\nWL_AUTH_MODES=password_totp\n", encoding="utf-8")
            fake_py = base / "venv" / "bin" / "python"
            fake_py.write_text('#!/bin/sh\nprintf "%s|" "$WL_AUTH_MODES" "$@"\n', encoding="utf-8", newline="\n")
            fake_py.chmod(0o755)
            env = {**os.environ, "WL_DEMO_BASE": base.as_posix(), "WL_DEMO_CONF": conf.as_posix()}
            return subprocess.run([SH, (DEPLOY / "wl-admin").as_posix(), *args], capture_output=True, text=True, env=env,
                                  timeout=30)

    def test_fake_url_from_env(self):
        r = self.run_wrapper("demo-fake-url")
        self.assertEqual((0, "http://127.0.0.2:8100/v1\n"), (r.returncode, r.stdout), r.stderr)

    def test_arguments_and_env_reach_the_cli(self):
        r = self.run_wrapper("tenant-create", "s", "Smoke GmbH", "--show")
        self.assertEqual("password_totp|-m|whisperloom_server|tenant-create|s|Smoke GmbH|--show|", r.stdout, r.stderr)


if __name__ == "__main__":
    unittest.main()
