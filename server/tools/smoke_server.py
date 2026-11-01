"""Smoke-Test des Firmen-Managers ohne Docker: echter Prozess auf freiem Port, nur stdlib im Aufrufer.

Ablauf: init → Firma per CLI (mit erstem owner) → Server starten → Einladung annehmen → TOTP aus dem angezeigten
Test-Geheimnis einrichten → abmelden → Login mit Passwort + TOTP → Gerät per API enrollen → im Portal freigeben →
Konfiguration abrufen (200, dann 304) → Prozess über die gespeicherte PID beenden. Letzte Zeile: SERVER SMOKE OK.
Mit --proxy zusätzlich (W2-SRV2): lokaler Fake-Anbieter (fake_upstream.py, eigener Prozess, Freigabe per
WL_UPSTREAM_ALLOW=127.0.0.1/32), Erkennung und Text über den Proxy, Nutzung, CSV, Budget (smoke_proxy.py).

Aufruf: py -3 -X utf8 server/tools/smoke_server.py [--python <venv-python>] [--keep] [--proxy]
"""

import argparse
import http.cookiejar
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

SERVER = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(SERVER))
from whisperloom_server import totp  # noqa: E402 (nur stdlib)

sys.path.insert(0, str(SERVER / "tools"))
import smoke_proxy  # noqa: E402 (nur stdlib)

PASSWORT = "smoke-passwort-nicht-echt-1"


class OhneWeiterleitung(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


class Sitzung:
    def __init__(self, basis: str):
        self.basis = basis
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar), OhneWeiterleitung)

    def anfrage(self, methode: str, pfad: str, formular: dict | None = None, json_body: dict | None = None,
                headers: dict | None = None, daten: bytes | None = None) -> tuple[int, dict, bytes]:
        kopf = dict(headers or {})
        if methode == "POST":
            kopf.setdefault("Origin", self.basis)  # wie ein Browser; die Portal-Formulare prüfen die Herkunft
        if formular is not None:
            daten = urllib.parse.urlencode(formular).encode()
            kopf["Content-Type"] = "application/x-www-form-urlencoded"
        elif json_body is not None:
            daten = json.dumps(json_body).encode()
            kopf["Content-Type"] = "application/json"
        req = urllib.request.Request(self.basis + pfad, data=daten, headers=kopf, method=methode)
        try:
            with self.opener.open(req, timeout=15) as r:
                return r.status, dict(r.headers), r.read()
        except urllib.error.HTTPError as e:
            return e.code, dict(e.headers), e.read()


def pruefe(bedingung: bool, text: str) -> None:
    if not bedingung:
        raise SystemExit(f"SERVER SMOKE FEHLER: {text}")
    print(f"ok  {text}")


def csrf(html: bytes) -> str:
    return re.search(rb'name="csrf" value="([^"]+)"', html).group(1).decode()


def freier_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def venv_python() -> str:
    for kandidat in (SERVER / ".venv" / "Scripts" / "python.exe", SERVER / ".venv" / "bin" / "python"):
        if kandidat.exists():
            return str(kandidat)
    raise SystemExit("SERVER SMOKE FEHLER: keine venv unter server/.venv (siehe server/README.md)")


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--python", default=None, help="Python der Server-venv (Standard: server/.venv)")
    p.add_argument("--keep", action="store_true", help="Datenordner nicht löschen")
    p.add_argument("--proxy", action="store_true", help="zusätzlich Proxy, Nutzung und Budget gegen einen Fake-Anbieter")
    a = p.parse_args()
    py = a.python or venv_python()
    port = freier_port()
    daten = Path(tempfile.mkdtemp(prefix="wl-smoke-"))
    basis = f"http://127.0.0.1:{port}"
    env = {**os.environ, "WL_DATA_DIR": str(daten), "WL_BASE_URL": basis, "WL_AUTH_MODES": "password_totp",
           "PYTHONIOENCODING": "utf-8"}
    for k in ("WL_MASTER_KEY", "WL_MASTER_KEY_FILE", "WL_CONFIG_FILE", "WL_TRUSTED_PROXIES", "WL_UPSTREAM_ALLOW"):
        env.pop(k, None)
    fake = None
    if a.proxy:
        env["WL_UPSTREAM_ALLOW"] = "127.0.0.1/32"  # der Fake-Anbieter lauscht lokal (README „SSRF-Schutz“)
        fake_port = freier_port()
        fake = subprocess.Popen([sys.executable, str(SERVER / "tools" / "fake_upstream.py"), "--port", str(fake_port),
                                 "--key", smoke_proxy.FAKE_KEY, "--name", "smoke-fake"], stdout=subprocess.DEVNULL)
        (daten / "fake.pid").write_text(str(fake.pid), encoding="ascii")

    def cli(*args: str) -> str:
        r = subprocess.run([py, "-m", "whisperloom_server", *args], cwd=SERVER, env=env, capture_output=True, timeout=60)
        if r.returncode != 0:
            raise SystemExit(f"SERVER SMOKE FEHLER: CLI {args[0]}: {r.stderr.decode('utf-8', 'replace')}")
        return r.stdout.decode("utf-8")

    pruefe("bereit:" in cli("init"), "init")
    aus = cli("tenant-create", "smoke", "Smoke GmbH", "--owner", "smoke-owner", "--show")
    code = re.search(r"Firmencode: (\S+)", aus).group(1)
    einladung = re.search(r"(/einladung/\S+)", aus).group(1)
    pruefe(len(code) == 19, "Firma per CLI angelegt")

    log = open(daten / "server.log", "wb")  # noqa: SIM115 (bleibt bis zum Prozessende offen)
    proc = subprocess.Popen([py, "-m", "whisperloom_server", "serve", "--port", str(port)], cwd=SERVER, env=env,
                            stdout=log, stderr=subprocess.STDOUT)
    pid = proc.pid
    (daten / "server.pid").write_text(str(pid), encoding="ascii")
    try:
        s = Sitzung(basis)
        for _ in range(100):
            try:
                if s.anfrage("GET", "/healthz")[0] == 200:
                    break
            except OSError:
                pass
            time.sleep(0.2)
        pruefe(s.anfrage("GET", "/healthz")[0] == 200, f"Server läuft (PID {pid}, Port {port})")

        st, _, html = s.anfrage("GET", einladung)
        st, kopf, _ = s.anfrage("POST", einladung, {"passwort": PASSWORT, "passwort2": PASSWORT, "csrf": csrf(html)})
        pruefe(st == 303 and kopf["location"] == "/login/totp-einrichten", "Einladung angenommen → TOTP einrichten")
        st, _, html = s.anfrage("GET", "/login/totp-einrichten")
        geheimnis = re.search(rb'id="totp-geheimnis">([A-Z2-7 ]+)<', html).group(1).decode().replace(" ", "")
        jetzt = int(time.time() * 1000)
        st, kopf, _ = s.anfrage("POST", "/login/totp-einrichten", {"code": totp.code_fuer(geheimnis, jetzt), "csrf": csrf(html)})
        pruefe(st == 303 and kopf["location"].startswith("/?ok=totp_ein"), "TOTP eingerichtet")
        st, _, html = s.anfrage("GET", "/geraete")
        s.anfrage("POST", "/logout", {"csrf": csrf(html)})
        pruefe(s.anfrage("GET", "/geraete")[0] == 303, "abgemeldet")

        st, _, html = s.anfrage("GET", "/login")
        st, kopf, _ = s.anfrage("POST", "/login", {"benutzername": "smoke-owner", "passwort": PASSWORT, "csrf": csrf(html)})
        pruefe(st == 303 and kopf["location"] == "/login/totp", "Passwort ok → TOTP verlangt")
        st, _, html = s.anfrage("GET", "/login/totp")
        naechster = totp.code_fuer(geheimnis, jetzt + totp.SCHRITT_S * 1000)  # derselbe Schritt gilt nicht zweimal
        st, kopf, _ = s.anfrage("POST", "/login/totp", {"code": naechster, "csrf": csrf(html)})
        pruefe(st == 303 and kopf["location"] == "/", "Admin-Login mit TOTP")

        api = Sitzung(basis)
        st, _, body = api.anfrage("POST", "/api/v1/enroll", json_body={"code": code, "deviceName": "Smoke-PC",
                                                                        "platform": "windows", "appVersion": "1.0.0"})
        g = json.loads(body)
        pruefe(st == 201 and g["state"] == "pending", f"Gerät enrollt ({g.get('deviceId')}, pending)")
        h = {"Authorization": "Bearer " + g["deviceToken"]}
        pruefe(api.anfrage("GET", "/api/v1/config", headers=h)[0] == 403, "Konfig vor Freigabe → 403")
        st, _, html = s.anfrage("GET", "/geraete")
        st, kopf, _ = s.anfrage("POST", f"/geraete/{g['deviceId']}/freigeben", {"csrf": csrf(html)})
        pruefe(st == 303, "Gerät im Portal freigegeben")
        st, kopf, body = api.anfrage("GET", "/api/v1/config", headers=h)
        k = json.loads(body)
        pruefe(st == 200 and k["keyMode"] == "proxy" and k["tenantName"] == "Smoke GmbH", "Konfiguration abgerufen")
        st, _, _ = api.anfrage("GET", "/api/v1/config", headers={**h, "If-None-Match": kopf["etag"]})
        pruefe(st == 304, "ETag → 304")
        if fake is not None:
            fake_basis = f"http://127.0.0.1:{fake_port}"
            for _ in range(100):
                try:
                    if Sitzung(fake_basis).anfrage("GET", "/healthz")[0] == 200:
                        break
                except OSError:
                    time.sleep(0.1)
            pruefe(fake.poll() is None, f"Fake-Anbieter läuft (PID {fake.pid}, Port {fake_port})")
            smoke_proxy.pruefen(s, api, g["deviceToken"], fake_basis, pruefe, csrf)
    finally:
        # Nur die eigenen Prozesse über die gespeicherte PID beenden (AP8).
        if fake is not None and fake.poll() is None:
            fake.terminate()
            fake.wait(timeout=10)
            print(f"ok  Fake-Anbieter {int((daten / 'fake.pid').read_text())} beendet")
        if proc.poll() is None:
            proc.terminate()
            try:
                proc.wait(timeout=10)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait(timeout=10)
        log.close()
        print(f"ok  Server-Prozess {int((daten / 'server.pid').read_text())} beendet")
        if not a.keep:
            # Windows gibt Dateigriffe eines beendeten Prozesses mitunter erst kurz danach frei.
            for _ in range(20):
                try:
                    shutil.rmtree(daten)
                    break
                except OSError:
                    time.sleep(0.25)
            else:
                print(f"hinweis: Datenordner nicht gelöscht: {daten}")
    print("SERVER SMOKE OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
