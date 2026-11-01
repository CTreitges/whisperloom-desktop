"""Suite enroll (W2-ENR): Firmen-Anbindung gegen einen echten Firmen-Server, ohne echte Eingabe und ohne Zwischenablage.

Mit --server-local startet die Suite den Server aus <repo>/server auf einem freien Port (Python der Server-venv,
siehe server/README.md; anders per --server-python bzw. WLD_SERVER_PYTHON) und dazu tools/fake_api.py als Anbieter
hinter dem Proxy. Mit --server <URL> --admin-cli "<Befehl>" läuft dieselbe Suite gegen eine laufende Instanz, etwa die
Demo auf dem VPS (W2-DEMO, tools/smoke_enroll_remote.py: eigene Testfirma, die am Ende gelöscht wird). Prüfungen:
- join-pending: `WhisperLoom join --server … --code …` → Exit 0, Zustand „wartet“, Token im Secret-Store, nicht in settings.json.
- approve-sync: Freigabe per Server-CLI (`device-approve`), `WhisperLoom sync` → freigegeben, Proxy-Zugang Fest.
- fixed-values: Fest/Vorgabe im Portal (Anmeldeart local_only) → nach `sync` in settings.json und policy.json.
- proxy-dictation: Diktat per --test-audio über den Proxy des Servers (W2-SRV2) an den Fake-Anbieter, Ergebnis in der
  Ausgabe-Senke --output-file (wie Suite full); Anfrage am Fake mit dem Key der Firma, nicht dem Geräte-Token.
- blocked-notice: Sperre per Server-CLI (`device-block`) → nach `sync` Zustand „gesperrt“ samt Hinweis, Einstellungen bleiben.
- logs-clean: weder Firmencode noch Geräte-Token noch Fake-Key in den Logs der App.
Prozesse (Server, Fake-API, App) startet und beendet die Suite nur über die eigene PID.
"""
from __future__ import annotations

import http.cookiejar
import json
import os
import re
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
SERVER_DIR = TOOLS.parent / "server"
PROXY_ENTRY = "company:proxy"
TENANT = "Smoke GmbH"


def free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def server_python(explicit: str | None) -> str:
    """Python mit den Server-Abhängigkeiten: Schalter, Umgebung, server/.venv, sonst Fehler."""
    for candidate in (explicit, os.environ.get("WLD_SERVER_PYTHON"),
                      str(SERVER_DIR / ".venv" / "Scripts" / "python.exe"), str(SERVER_DIR / ".venv" / "bin" / "python")):
        if candidate and Path(candidate).exists():
            return candidate
    raise RuntimeError("keine Server-venv (server/.venv, siehe server/README.md) und kein --server-python")


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


class LocalServer:
    """Firmen-Server als Kindprozess mit eigenem Datenordner; Portal-Anmeldung über local_only (nur Loopback)."""

    slug = "smoke"

    def __init__(self, python: str, data: Path, server_dir: Path = SERVER_DIR):
        self.python = python
        self.server_dir = server_dir
        self.port = free_port()
        self.base = f"http://127.0.0.1:{self.port}"
        self.data = data
        # SSRF-Schutz (W2-SRV2) erlaubt Anbieter auf Loopback nur ausdrücklich: der Fake-Anbieter läuft auf 127.0.0.1.
        self.env = {**os.environ, "WL_DATA_DIR": str(data), "WL_BASE_URL": self.base, "WL_AUTH_MODES": "local_only",
                    "WL_UPSTREAM_ALLOW": "127.0.0.1/32", "PYTHONIOENCODING": "utf-8"}
        for k in ("WL_MASTER_KEY", "WL_MASTER_KEY_FILE", "WL_CONFIG_FILE", "WL_TRUSTED_PROXIES"):
            self.env.pop(k, None)
        self.proc: subprocess.Popen | None = None
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPCookieProcessor(self.jar),
                                                  NoRedirect)

    def cli(self, *args: str) -> str:
        r = subprocess.run([self.python, "-m", "whisperloom_server", *args], cwd=self.server_dir, env=self.env,
                           capture_output=True, timeout=60)
        if r.returncode != 0:
            raise RuntimeError(f"Server-CLI {args[0]}: {r.stderr.decode('utf-8', 'replace').strip()[:200]}")
        return r.stdout.decode("utf-8")

    def __enter__(self) -> LocalServer:
        self.data.mkdir(parents=True, exist_ok=True)
        self.cli("init")
        self.log = open(self.data / "server.log", "wb")  # noqa: SIM115 (bis zum Ende offen)
        self.proc = subprocess.Popen([self.python, "-m", "whisperloom_server", "serve", "--port", str(self.port)], cwd=self.server_dir,
                                     env=self.env, stdout=self.log, stderr=subprocess.STDOUT)
        deadline = time.time() + 30
        while time.time() < deadline:
            try:
                if self.request("GET", "/healthz")[0] == 200:
                    return self
            except OSError:
                pass
            time.sleep(0.2)
        raise RuntimeError("Server antwortet nicht auf /healthz")

    def __exit__(self, *exc) -> None:
        if self.proc:
            self.proc.kill()  # nur der eigene Kindprozess (gespeicherte PID)
            self.proc.wait(timeout=10)
        self.log.close()

    def request(self, method: str, path: str, form: dict | None = None) -> tuple[int, bytes]:
        data = urllib.parse.urlencode(form).encode() if form is not None else None
        headers = {"Origin": self.base} if method == "POST" else {}
        if data is not None:
            headers["Content-Type"] = "application/x-www-form-urlencoded"
        req = urllib.request.Request(self.base + path, data=data, headers=headers, method=method)
        try:
            with self.opener.open(req, timeout=15) as r:
                return r.status, r.read()
        except urllib.error.HTTPError as e:
            return e.code, e.read()

    def form(self, path: str, fields: dict) -> int:
        """Portal-Formular wie ein Browser: Seite laden (CSRF), dann absenden."""
        status, html = self.request("GET", path)
        match = re.search(rb'name="csrf" value="([^"]+)"', html)
        if status != 200 or not match:
            raise RuntimeError(f"Portal {path}: {status}")
        return self.request("POST", path, {**fields, "csrf": match.group(1).decode()})[0]

    def tenant(self) -> str:
        out = self.cli("tenant-create", self.slug, TENANT, "--show")
        return re.search(r"Firmencode: (\S+)", out).group(1)

    def device_id(self) -> str:
        line = self.cli("device-list", self.slug).strip().splitlines()[-1]
        return line.split("\t")[0]


def policy_fields(policy_path_values: dict[str, tuple[str, str]]) -> dict:
    """Formular der Richtlinie: alle Pfade „free“ außer den genannten (Stufe, Wert)."""
    fields: dict = {}
    for path, (level, value) in policy_path_values.items():
        fields["s:" + path] = level
        fields["w:" + path] = value
    return fields


def read_json(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def run(args, work: Path, report, smoke) -> None:
    """smoke = Modul tools/smoke.py (AppRun, fresh_dir, write_test_wav); FakeApi aus smoke_full."""
    import smoke_full
    import smoke_platform as plat

    remote_url = getattr(args, "server", None)
    if not args.server_local and not remote_url:
        print("SMOKE enroll SKIP ohne --server-local bzw. --server")
        report.check("server-local", False, "--server-local fehlt")
        return
    if remote_url and not getattr(args, "admin_cli", None):
        report.check("admin-cli", False, "--server braucht --admin-cli (Befehl für python -m whisperloom_server)")
        return
    data = args.data_dir or work / args.id / "smoke-data-enroll"
    smoke.fresh_dir(data)
    server_data = work / args.id / "smoke-server-enroll"
    smoke.fresh_dir(server_data)
    wav = work / args.id / "smoke-test.wav"
    out = work / args.id / "smoke-output-enroll.txt"
    smoke.write_test_wav(wav)
    out.unlink(missing_ok=True)
    prefix = f"WhisperLoom-Test-{args.id}:"
    (data / "settings.json").write_text(json.dumps({"schemaVersion": 1, "language": "en", "engine": "online"}), encoding="utf-8")
    secrets_seen: list[str] = []
    if remote_url:
        import smoke_enroll_remote as remote

        server_cm = remote.RemoteServer(remote_url, args.admin_cli, args.id)
        fake_cm = remote.RemoteFake(server_cm)
    else:
        server_dir = Path(args.server_dir) if getattr(args, "server_dir", None) else SERVER_DIR
        server_cm = LocalServer(server_python(args.server_python), server_data, server_dir)
        fake_cm = smoke_full.FakeApi()
    with server_cm as server, fake_cm as fake:
        app = smoke.AppRun(args.app, data, prefix, ["--test-audio", str(wav), "--output-file", str(out)])
        try:
            code = server.tenant()
            secrets_seen.extend([code, code.replace("-", "")])
            app.start()
            r = app.cli("join", "--server", server.base, "--code", code.lower(), "--device-name", "Smoke-PC")
            s = app.status() or {}
            m = s.get("management") or {}
            token, where = plat.read_secret(args.platform, prefix, "device", data, s.get("secretStore"))
            if token:
                secrets_seen.append(token)
            settings_text = (data / "settings.json").read_text(encoding="utf-8")
            reasons = []
            if r.returncode != 0 or "state=pending" not in r.stdout:
                reasons.append(f"rc={r.returncode} {r.stdout.strip()[:120]} {r.stderr.strip()[:160]}")
            if m.get("state") != "pending" or m.get("tenantName") != TENANT:
                reasons.append(f"management={m}")
            if not token or not token.startswith("wld_"):
                reasons.append(f"kein Token in der Ablage ({where})")
            if token and (token in settings_text or token in r.stdout + r.stderr or token in json.dumps(s)):
                reasons.append("Token in settings.json, Ausgabe oder status")
            if code in r.stdout + r.stderr or code.replace("-", "") in settings_text.upper():
                reasons.append("Firmencode in Ausgabe bzw. settings.json")
            report.check("join-pending", not reasons, "; ".join(reasons) or where)

            device = server.device_id()
            server.cli("device-approve", device)
            sync = app.cli("sync", timeout=30)
            s = app.status() or {}
            m = s.get("management") or {}
            settings = read_json(data / "settings.json")
            policy = read_json(data / "policy.json")
            reasons = []
            if "sync=synced" not in sync.stdout or m.get("state") != "approved" or m.get("lastSync", 0) <= 0:
                reasons.append(f"sync={sync.stdout.strip()} management={m}")
            # Zugang = Eintrag „Firma“ (company:proxy, Adresse <server>/api/v1/proxy, Key = Geräte-Token), Fest.
            if (settings.get("stt") or {}).get("providerId") != PROXY_ENTRY or "stt.providerId" not in policy.get("locked", []):
                reasons.append(f"Proxy-Zugang nicht Fest: stt={settings.get('stt')} locked={policy.get('locked')}")
            report.check("approve-sync", not reasons, "; ".join(reasons) or f"device={device}")

            status = server.form("/richtlinie", policy_fields({
                "refine.stage": ("fixed", "beautify"),
                "output.alwaysCopy": ("default", "on"),
                "language": ("fixed", "en"),
            }))
            sync = app.cli("sync", timeout=30)
            settings = read_json(data / "settings.json")
            policy = read_json(data / "policy.json")
            stage = app.cli("stage", "next", timeout=20)
            reasons = []
            if status != 303:
                reasons.append(f"Portal-Richtlinie: HTTP {status}")
            if (settings.get("refine") or {}).get("stage") != "beautify" or "refine.stage" not in policy.get("locked", []):
                reasons.append(f"refine={settings.get('refine')} locked={policy.get('locked')}")
            if (settings.get("output") or {}).get("alwaysCopy") is not True or "output.alwaysCopy" not in (policy.get("defaults") or {}):
                reasons.append(f"Vorgabe output.alwaysCopy fehlt: {settings.get('output')} defaults={policy.get('defaults')}")
            if stage.returncode == 0 or "stage-locked" not in stage.stdout:
                reasons.append(f"Stufe per CLI änderbar: {stage.stdout.strip()}")
            report.check("fixed-values", not reasons, "; ".join(reasons) or sync.stdout.strip())

            # Anbieter hinter dem Proxy: Key der Firma für den Fake (im Portal), Diktat über den Proxy des Servers.
            server.form("/schluessel", {"name": "stt", "anbieter": "custom", "basis_url": fake.base + "/v1", "key": smoke.FAKE_KEY})
            app.cli("sync", timeout=30)
            fake.post("/_reset", {})
            s = smoke_full.dictate(app)
            text = out.read_text(encoding="utf-8") if out.exists() else ""
            reqs = [q for q in fake.requests() if q["path"].endswith("/audio/transcriptions")]
            reasons = []
            if not text or text != s.get("lastResult"):
                reasons.append(f"Ausgabe={text[:60]!r} lastError={s.get('lastError')}")
            if not reqs or not reqs[-1].get("has_key"):
                reasons.append(f"Fake-Anbieter ohne Anfrage mit Key ({len(reqs)} Anfragen)")
            report.check("proxy-dictation", not reasons, "; ".join(reasons))

            server.cli("device-block", device)
            sync = app.cli("sync", timeout=30)
            s = app.status() or {}
            m = s.get("management") or {}
            settings = read_json(data / "settings.json")
            reasons = []
            if m.get("state") != "blocked" or not m.get("notice") or TENANT not in str(m.get("notice")):
                reasons.append(f"sync={sync.stdout.strip()} management={m}")
            if (settings.get("refine") or {}).get("stage") != "beautify":
                reasons.append("Einstellungen nach Sperre verändert")
            report.check("blocked-notice", not reasons, "; ".join(reasons) or str(m.get("notice"))[:80])
        finally:
            app.stop()
            log_dir = data / "logs"
            logs = "".join(p.read_text(encoding="utf-8", errors="replace") for p in log_dir.glob("*")) if log_dir.exists() else ""
            leaks = [t for t in (*secrets_seen, smoke.FAKE_KEY) if t and t in logs]
            report.check("logs-clean", logs != "" and not leaks, f"{len(leaks)} Treffer im Log" if logs else "kein Log")
            plat.cleanup_secrets(args.platform, prefix, data)
