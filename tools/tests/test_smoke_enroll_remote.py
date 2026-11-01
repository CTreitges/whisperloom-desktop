"""Tests für die Live-Variante der Suite enroll (tools/smoke_enroll_remote.py, W2-DEMO)."""
from __future__ import annotations

import argparse
import contextlib
import io
import os
import shlex
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import smoke  # noqa: E402
import smoke_enroll as se  # noqa: E402
import smoke_enroll_remote as remote  # noqa: E402

SERVER_PY = [p for p in (se.SERVER_DIR / ".venv" / "Scripts" / "python.exe", se.SERVER_DIR / ".venv" / "bin" / "python")
             if p.exists()]


class AdminCommandTest(unittest.TestCase):
    def test_ssh_gets_one_quoted_remote_command(self):
        cmd = remote.admin_command("ssh oracle-vps whisperloom-admin/bin/wl-admin", ("tenant-create", "s", "Smoke GmbH"))
        self.assertEqual(["ssh", "oracle-vps", "whisperloom-admin/bin/wl-admin", "tenant-create s 'Smoke GmbH'"], cmd)
        self.assertEqual(["tenant-create", "s", "Smoke GmbH"], shlex.split(cmd[-1]))

    def test_shell_metacharacters_stay_literal_over_ssh(self):
        cmd = remote.admin_command("ssh host wl", ("device-approve", "d_1; rm -rf ~"))
        self.assertEqual(["device-approve", "d_1; rm -rf ~"], shlex.split(cmd[-1]))

    def test_local_command_gets_plain_arguments(self):
        self.assertEqual(["py", "-m", "whisperloom_server", "tenant-list", "a b"],
                         remote.admin_command("py -m whisperloom_server", ("tenant-list", "a b")))

    def test_empty_is_an_error(self):
        with self.assertRaises(ValueError):
            remote.admin_command("  ", ("tenant-list",))


class TotpTest(unittest.TestCase):
    SECRET = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"  # RFC 6238 Anhang B (SHA-1), "12345678901234567890"

    def test_rfc_6238_vectors_last_six_digits(self):
        self.assertEqual("287082", remote.totp_code(self.SECRET, 59))
        self.assertEqual("081804", remote.totp_code(self.SECRET, 1111111109))

    def test_grouped_lowercase_secret_from_the_portal(self):
        grouped = " ".join(self.SECRET[i:i + 4] for i in range(0, len(self.SECRET), 4)).lower()
        self.assertEqual("287082", remote.totp_code(grouped, 59))


class SlugTest(unittest.TestCase):
    def test_valid_server_slug(self):
        slug = remote.make_slug("W2-DEMO", "20261101120000")
        self.assertEqual("smoke-w2-demo-20261101120000", slug)
        self.assertRegex(slug, r"^[a-z0-9-]{2,40}$")

    def test_long_or_odd_ids_are_cut_and_cleaned(self):
        self.assertRegex(remote.make_slug("A_very.long id " * 5, "1"), r"^[a-z0-9-]{2,40}$")


class RemoteFakeTest(unittest.TestCase):
    def test_url_requests_and_reset_go_through_the_wrapper(self):
        server = mock.Mock()
        server.cli.side_effect = lambda *a: {"demo-fake-url": "http://127.0.0.2:8100/v1\n",
                                             "demo-fake-requests": '{"requests": [{"path": "/v1/audio/transcriptions"},'
                                                                   ' {"path": "proxy:/x"}]}',
                                             "demo-fake-reset": ""}[a[0]]
        with remote.RemoteFake(server) as fake:
            self.assertEqual("http://127.0.0.2:8100", fake.base)
            self.assertEqual([{"path": "/v1/audio/transcriptions"}], fake.requests())
            fake.post("/_reset", {})
            with self.assertRaises(ValueError):
                fake.post("/_control", {"fail": {}})
        self.assertIn(mock.call("demo-fake-reset"), server.cli.call_args_list)

    def test_non_loopback_fake_is_refused(self):
        server = mock.Mock()
        server.cli.return_value = "http://10.0.0.5:8100/v1"
        with self.assertRaises(RuntimeError):
            remote.RemoteFake(server).__enter__()


class RemoteServerCliTest(unittest.TestCase):
    def test_stdin_is_closed_without_input_and_fed_with_input(self):
        live = remote.RemoteServer("https://wl.example", "ssh host wl", "T")
        done = subprocess.CompletedProcess([], 0, b"ok", b"")
        with mock.patch.object(remote.subprocess, "run", return_value=done) as run:
            live.cli("tenant-list")
            self.assertIs(subprocess.DEVNULL, run.call_args.kwargs["stdin"])
            self.assertNotIn("input", run.call_args.kwargs)
            live.cli("key-set", "s", "stt", stdin="test-key-not-real-1\n")
            self.assertEqual(b"test-key-not-real-1\n", run.call_args.kwargs["input"])

    def test_test_tenant_is_deleted_even_after_a_failure(self):
        live = remote.RemoteServer("https://wl.example", "ssh host wl", "T")
        with mock.patch.object(live, "cli") as cli, mock.patch.object(live, "request", return_value=(200, b"")):
            with self.assertRaises(RuntimeError), live:
                live.created = True
                raise RuntimeError("Prüfung abgebrochen")
        cli.assert_called_once_with("tenant-delete", live.slug, "--confirm", live.slug)
        self.assertFalse(live.created)

    def test_timeout_after_tenant_create_still_cleans_up(self):
        """D4: Die Firma kann angelegt sein, auch wenn die CLI-Antwort (ssh) ausbleibt."""
        live = remote.RemoteServer("https://wl.example", "ssh host wl", "T")
        calls: list[tuple] = []

        def cli(*args, **kwargs):
            calls.append(args)
            if args[0] == "tenant-create":
                raise subprocess.TimeoutExpired("ssh", 90)
            return "gelöscht"

        with mock.patch.object(live, "cli", side_effect=cli), mock.patch.object(live, "request", return_value=(200, b"")):
            with self.assertRaises(subprocess.TimeoutExpired), live:
                live.tenant()
        self.assertEqual(("tenant-delete", live.slug, "--confirm", live.slug), calls[-1])

    def test_failing_cleanup_does_not_hide_the_original_error(self):
        live = remote.RemoteServer("https://wl.example", "ssh host wl", "T")
        with mock.patch.object(live, "cli", side_effect=RuntimeError("Admin-CLI tenant-delete: rc=1")), \
                mock.patch.object(live, "request", return_value=(200, b"")), \
                contextlib.redirect_stderr(io.StringIO()) as err:
            with self.assertRaisesRegex(ValueError, "Prüfung"), live:
                live.created = True
                raise ValueError("Prüfung fehlgeschlagen")
        self.assertIn("cleanup WARN", err.getvalue())

    def test_failing_cleanup_after_success_is_an_error(self):
        live = remote.RemoteServer("https://wl.example", "ssh host wl", "T")
        with mock.patch.object(live, "cli", side_effect=RuntimeError("Admin-CLI tenant-delete: rc=1")), \
                mock.patch.object(live, "request", return_value=(200, b"")):
            with self.assertRaises(RuntimeError), live:
                live.created = True


class RunArgsTest(unittest.TestCase):
    def test_server_without_admin_cli_fails(self):
        report = smoke.Report()
        se.run(argparse.Namespace(server_local=False, server="https://x.invalid", admin_cli=None), Path(tempfile.gettempdir()),
               report, smoke)
        self.assertEqual([("admin-cli", False)], report.results)

    def test_options_are_registered(self):
        with mock.patch.object(se, "run") as run, mock.patch.object(smoke.Report, "finish", return_value=0):
            smoke.main(["--platform", "win", "--suite", "enroll", "--id", "T", "--app", "x.exe",
                        "--server", "https://wl.example", "--admin-cli", "ssh host wl-admin"])
        args = run.call_args.args[0]
        self.assertEqual(("https://wl.example", "ssh host wl-admin"), (args.server, args.admin_cli))


@unittest.skipUnless(SERVER_PY, "keine Server-venv (server/.venv)")
class RemoteServerAgainstLocalInstanceTest(unittest.TestCase):
    """Echter Server mit password_totp auf Loopback; die Admin-CLI ist hier der lokale Python statt ssh."""

    def test_invite_totp_portal_and_cleanup(self):
        # password_totp = Zwei-Faktor Pflicht; password_totp,password = Demo-Stand seit W2-SFIX (überspringbar).
        for modes in ("password_totp", "password_totp,password"):
            with self.subTest(modes=modes):
                self.run_against_local(modes)

    def run_against_local(self, modes: str) -> None:
        py = str(SERVER_PY[0])
        # Windows: server.log ist nach dem Beenden des venv-Launchers kurz noch offen → Aufräumfehler ignorieren.
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as d:
            srv = se.LocalServer(py, Path(d) / "daten")
            srv.env["WL_AUTH_MODES"] = modes
            with srv, mock.patch.dict(os.environ, srv.env):
                admin_cli = shlex.join([py, "-m", "whisperloom_server"])
                with remote.RemoteServer(srv.base, admin_cli, "T-1") as live:
                    code = live.tenant()
                    self.assertRegex(code, r"^[A-Z0-9]{4}(-[A-Z0-9]{4}){3}$")
                    self.assertEqual(200, live.request("GET", "/richtlinie")[0])
                    self.assertEqual(303, live.form("/richtlinie", se.policy_fields({"language": ("fixed", "en")})))
                    self.assertIn(live.slug, live.cli("tenant-list"))
                self.assertNotIn("smoke-t-1", srv.cli("tenant-list"))
                self.assertFalse(live.created)


if __name__ == "__main__":
    unittest.main()
