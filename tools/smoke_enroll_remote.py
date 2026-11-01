"""Suite enroll gegen eine laufende Instanz (W2-DEMO): --server <URL> --admin-cli "<Befehl>".

--admin-cli ist der Befehl, der auf dem Server `python -m whisperloom_server` mit der Umgebung des Dienstes aufruft, für
die Demo `ssh oracle-vps whisperloom-admin/app/server/deploy/wl-admin` (server/deploy/wl-admin). Ist das erste Wort `ssh`, gehen die
Argumente shell-sicher als ein String an die entfernte Shell; sonst als einzelne Argumente. Der Wrapper der Demo kennt
zusätzlich demo-fake-url, demo-fake-requests und demo-fake-reset für den Fake-Anbieter, der dort nur auf Loopback lauscht.

Ablauf: eigene Testfirma „Smoke GmbH“ (Slug smoke-<id>-<zeit>) mit owner per CLI, Einladung über das Portal annehmen,
TOTP einrichten (Anmeldeart password_totp), danach dieselben Prüfungen wie lokal. Am Ende löscht die Suite die Firma per
`tenant-delete` (samt Gerät, owner und Key), auch nach einem Fehler. Passwort und TOTP-Geheimnis sind Wegwerf-Testwerte.
"""
from __future__ import annotations

import base64
import hashlib
import hmac
import http.cookiejar
import json
import re
import secrets
import shlex
import struct
import subprocess
import time
import urllib.parse
import urllib.request
from pathlib import Path

import smoke_enroll as se


def admin_command(admin_cli: str, args: tuple[str, ...] | list[str]) -> list[str]:
    parts = shlex.split(admin_cli)
    if not parts:
        raise ValueError("--admin-cli ist leer")
    if Path(parts[0]).stem.lower() == "ssh":
        return [*parts, shlex.join(args)]
    return [*parts, *args]


def totp_code(secret: str, now: float | None = None) -> str:
    """RFC 6238 (SHA-1, 6 Ziffern, 30 s) wie server/whisperloom_server/totp.py."""
    # Doku: https://datatracker.ietf.org/doc/html/rfc6238 (abgerufen 2026-11-01)
    secret = "".join(secret.split()).upper()
    key = base64.b32decode(secret + "=" * (-len(secret) % 8))
    digest = hmac.new(key, struct.pack(">Q", int((time.time() if now is None else now) // 30)), hashlib.sha1).digest()
    offset = digest[-1] & 0x0F
    return str((struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7FFFFFFF) % 1_000_000).zfill(6)


def make_slug(run_id: str, stamp: str | None = None) -> str:
    raw = f"smoke-{run_id}-{stamp or time.strftime('%Y%m%d%H%M%S')}".lower()
    return re.sub(r"[^a-z0-9-]", "-", raw)[:40]


class RemoteServer(se.LocalServer):
    """Laufende Instanz (https hinter Caddy); Verwaltung über --admin-cli, Portal mit Passwort und TOTP."""

    def __init__(self, base: str, admin_cli: str, run_id: str):  # noqa: D107 (kein super().__init__: nichts zu starten)
        self.base = base.rstrip("/")
        self.admin_cli = admin_cli
        self.slug = make_slug(run_id)
        self.owner = self.slug
        self.password = "test-passwort-nicht-echt-" + secrets.token_hex(6)
        self.created = False
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPCookieProcessor(self.jar),
                                                  se.NoRedirect)

    def cli(self, *args: str, stdin: str | None = None) -> str:
        # Ohne Eingabe stdin schließen: ssh liest sonst das Terminal des Aufrufers mit.
        feed = {"input": stdin.encode()} if stdin is not None else {"stdin": subprocess.DEVNULL}
        r = subprocess.run(admin_command(self.admin_cli, args), capture_output=True, timeout=90, **feed)
        if r.returncode != 0:
            raise RuntimeError(f"Admin-CLI {args[0]}: rc={r.returncode} {r.stderr.decode('utf-8', 'replace').strip()[:200]}")
        return r.stdout.decode("utf-8")

    def __enter__(self) -> RemoteServer:
        status = self.request("GET", "/healthz")[0]
        if status != 200:
            raise RuntimeError(f"{self.base}/healthz: HTTP {status}")
        return self

    def __exit__(self, *exc) -> None:
        if self.created:
            self.cli("tenant-delete", self.slug, "--confirm", self.slug)
            self.created = False

    def tenant(self) -> str:
        out = self.cli("tenant-create", self.slug, se.TENANT, "--owner", self.owner, "--show")
        self.created = True
        code = re.search(r"Firmencode: (\S+)", out).group(1)
        invite = re.search(r"(/einladung/\S+)", out)
        if not invite:
            raise RuntimeError("tenant-create ohne Einladungs-Link")
        self.portal_login(urllib.parse.urlsplit(invite.group(1)).path)
        return code

    def portal_login(self, invite_path: str) -> None:
        """Einladung annehmen (Passwort), dann TOTP einrichten → volle Sitzung für Richtlinie und Keys."""
        status = self.form(invite_path, {"passwort": self.password, "passwort2": self.password})
        if status != 303:
            raise RuntimeError(f"Einladung: HTTP {status}")
        status, html = self.request("GET", "/login/totp-einrichten")
        secret = re.search(rb'id="totp-geheimnis">([^<]+)<', html)
        csrf = re.search(rb'name="csrf" value="([^"]+)"', html)
        if status != 200 or not secret or not csrf:
            raise RuntimeError(f"TOTP einrichten: HTTP {status}")
        fields = {"code": totp_code(secret.group(1).decode()), "csrf": csrf.group(1).decode()}
        status = self.request("POST", "/login/totp-einrichten", fields)[0]
        if status != 303:
            raise RuntimeError(f"TOTP bestätigen: HTTP {status}")


class RemoteFake:
    """Fake-Anbieter auf dem Server (nur Loopback dort); Adresse und Aufzeichnung über den Admin-Wrapper."""

    def __init__(self, server: RemoteServer):
        self.server = server
        self.base = ""

    def __enter__(self) -> RemoteFake:
        self.base = self.server.cli("demo-fake-url").strip().removesuffix("/v1")
        if not re.fullmatch(r"http://127\.\d+\.\d+\.\d+:\d+", self.base):
            raise RuntimeError(f"demo-fake-url unerwartet: {self.base[:60]!r}")
        return self

    def __exit__(self, *exc) -> None:
        pass

    def requests(self) -> list[dict]:
        data = json.loads(self.server.cli("demo-fake-requests"))
        return [q for q in data["requests"] if not q["path"].startswith("proxy:")]

    def post(self, path: str, payload: dict) -> None:
        if path != "/_reset" or payload:
            raise ValueError(f"nur /_reset ohne Nutzdaten über den Wrapper: {path}")
        self.server.cli("demo-fake-reset")
