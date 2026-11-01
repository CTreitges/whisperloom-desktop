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
        # Caddy rechnet MB dezimal: Limit muss über der App-Grenze für Audio liegen (25 MiB + 64 KiB).
        size = re.search(r"max_size (\d+)MiB", caddy)
        self.assertIsNotNone(size, "max_size in MiB angeben")
        self.assertGreaterEqual(int(size.group(1)) * 1024 * 1024, 25 * 1024 * 1024 + 64 * 1024)
        env = env_values()
        self.assertEqual("password_totp,password", env["WL_AUTH_MODES"])  # W2-SFIX: Zwei-Faktor optional
        self.assertTrue(env["WL_BASE_URL"].startswith("https://whisperloom-admin."))
        self.assertEqual("127.0.0.1/32", env["WL_TRUSTED_PROXIES"])
        self.assertFalse(env["WL_MASTER_KEY_FILE"].startswith(env["WL_DATA_DIR"]), "Schlüssel nicht im Datenordner")

    def test_upstream_allow_is_exactly_the_fake_address(self):
        env = env_values()
        fake_unit = (DEPLOY / "whisperloom-admin-fake.service").read_text(encoding="utf-8")
        host, port = re.search(r"--host (\S+) --port (\d+)", fake_unit).groups()
        self.assertEqual(f"{host}:{port}", env["WL_DEMO_FAKE"])
        # W2-SFIX: Freigabe mit Port, sonst wären alle Ports von 127.0.0.2 erreichbar (sshd, Dev-Server, …).
        self.assertEqual(f"{host}:{port}", env["WL_UPSTREAM_ALLOW"])
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


PY3_STUB = """#!/bin/sh
case "$*" in
  "-m venv "*) mkdir -p "$3/bin"; cp "$STUBS/venv-python" "$3/bin/python"; chmod 755 "$3/bin/python" ;;
  *secrets*) echo "pw$(od -An -N6 -tx1 /dev/urandom | tr -d ' \\n')" ;;
  *urandom*) echo 0000000000000000000000000000000000000000000000000000000000000000 ;;
esac
"""
# Fake der Server-CLI: merkt sich Firmen, Admins (mit Passwort) und Keys in $HOME/state.
VENV_STUB = """#!/bin/sh
S="$HOME/state"; touch "$S"
[ "$1" = "-m" ] && [ "$2" = "pip" ] && exit 0
shift 2
case "$1" in
  init) echo bereit ;;
  tenant-list) grep -q '^tenant demo$' "$S" && printf 'demo\\taktiv\\t0 Geräte\\tDemo GmbH\\n' || echo "(keine)" ;;
  tenant-create) echo "tenant $2" >> "$S" ;;
  key-set) read -r k; echo "key $2 $3" >> "$S"; echo ok ;;
  instance-admin-create|admin-create)
    [ "$1" = admin-create ] && u=$3 || u=$2
    read -r pw
    grep -q "^admin $u " "$S" && { echo "fehler: h.benutzer_vergeben" >&2; exit 1; }
    [ "${FAIL_ON:-}" = "$u" ] && { echo "fehler: abbruch" >&2; exit 1; }
    echo "admin $u $pw" >> "$S"; echo "Admin: $u" ;;
esac
"""


@unittest.skipUnless(SH, "kein sh")
class SetupDemoTest(unittest.TestCase):
    """setup-demo.sh mit Stubs für python3, venv und systemctl: wiederholbar, Passwörter nur in der Datei."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.home = Path(self.tmp.name)
        stubs = self.home / "stubs"
        stubs.mkdir()
        for name, text in (("python3", PY3_STUB), ("venv-python", VENV_STUB),
                           ("systemctl", '#!/bin/sh\necho "systemctl $*" >> "$HOME/systemctl.log"\n'
                                         '[ "$2" = is-active ] && echo active\nexit 0\n')):
            (stubs / name).write_text(text, encoding="utf-8", newline="\n")
            (stubs / name).chmod(0o755)
        server = self.home / "whisperloom-admin" / "app" / "server"
        shutil.copytree(DEPLOY, server / "deploy")
        (server / "requirements.txt").write_text("", encoding="utf-8")
        self.env = {**os.environ, "HOME": self.home.as_posix(), "STUBS": stubs.as_posix(),
                    "PATH": stubs.as_posix() + os.pathsep + os.environ["PATH"]}
        self.env.pop("WL_DEMO_BASE", None)
        self.env.pop("WL_DEMO_CONF", None)

    def tearDown(self):
        self.tmp.cleanup()

    def setup(self, **env) -> subprocess.CompletedProcess:
        script = (self.home / "whisperloom-admin" / "app" / "server" / "deploy" / "setup-demo.sh").as_posix()
        return subprocess.run([SH, script], capture_output=True, text=True, env={**self.env, **env}, timeout=60)

    def state(self) -> list[str]:
        return (self.home / "state").read_text(encoding="utf-8").splitlines()

    def initial(self) -> str:
        return (self.home / ".config" / "whisperloom-admin" / "admin-initial.txt").read_text(encoding="utf-8")

    def test_first_run_creates_everything_and_keeps_passwords_off_stdout(self):
        r = self.setup()
        self.assertEqual(0, r.returncode, r.stderr)
        admins = {line.split()[1]: line.split()[2] for line in self.state() if line.startswith("admin ")}
        self.assertEqual({"admin", "demo"}, set(admins))
        for pw in admins.values():
            self.assertIn(f" / {pw}", self.initial())
            self.assertNotIn(pw, r.stdout + r.stderr)
        self.assertEqual(["key demo stt", "key demo llm"], [s for s in self.state() if s.startswith("key ")])
        env_text = (self.home / ".config" / "whisperloom-admin" / "env").read_text(encoding="utf-8")
        # Git-Bash zeigt HOME als /tmp/… statt C:/…: nur prüfen, dass der Platzhalter ersetzt ist.
        self.assertRegex(env_text, rf"\nWL_DATA_DIR=/\S*{re.escape(self.home.name)}/whisperloom-admin/data\n")
        self.assertNotIn("@HOME@", env_text)
        self.assertIn("enable --now whisperloom-admin-fake whisperloom-admin",
                      (self.home / "systemctl.log").read_text(encoding="utf-8"))

    def test_rerun_after_abort_uses_the_same_passwords_and_keeps_keys(self):
        r = self.setup(FAIL_ON="demo")
        self.assertNotEqual(0, r.returncode)
        first = self.initial()
        r = self.setup()
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertEqual(first, self.initial())
        admins = [line for line in self.state() if line.startswith("admin ")]
        self.assertEqual(2, len(admins))
        for line in admins:
            self.assertIn(f" / {line.split()[2]}", first)
        self.assertEqual(2, len([s for s in self.state() if s.startswith("key ")]), "Keys nicht erneut gesetzt")


if __name__ == "__main__":
    unittest.main()
