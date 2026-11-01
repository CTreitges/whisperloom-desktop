"""Kommandozeile: Hilfe, Firma anlegen, Code zeigen/rotieren, Admin einladen, Geräte freigeben; Geheimnisse nur in Datei."""

import re
import subprocess
import sys
from pathlib import Path

from whisperloom_server import cli

from .conftest import enroll, sql

SERVER = Path(__file__).parent.parent


def test_hilfe_als_prozess():
    r = subprocess.run([sys.executable, "-m", "whisperloom_server", "--help"], cwd=SERVER, capture_output=True, timeout=60)
    assert r.returncode == 0
    text = r.stdout.decode("utf-8")
    assert "tenant-create" in text and "serve" in text and "Hauptschlüssel" in text


def test_hilfe_aus_fremdem_ordner(tmp_path):
    """Akzeptanzbefehl `<venv>/python -m whisperloom_server --help` ohne cwd=server/ (Paket per `pip install -e`)."""
    r = subprocess.run([sys.executable, "-m", "whisperloom_server", "--help"], cwd=tmp_path, capture_output=True, timeout=60)
    assert r.returncode == 0, r.stderr.decode("utf-8", "replace")


def test_firma_anlegen_mit_show(einst, capsys):
    assert cli.main(["tenant-create", "muster", "Muster GmbH", "--owner", "chef", "--auto-approve", "--show"], einst) == 0
    aus = capsys.readouterr().out
    code = re.search(r"Firmencode: (\S+)", aus).group(1)
    assert re.search(r"/einladung/\S+", aus)
    assert tuple(sql(einst, "SELECT auto_freigabe, key_modus FROM firma WHERE slug = 'muster'")[0]) == (1, "proxy")
    assert sql(einst, "SELECT rolle FROM admin WHERE benutzer = 'chef'")[0][0] == "owner"
    assert len(code) == 19


def test_geheimnisse_ohne_show_nur_in_datei(einst, capsys):
    assert cli.main(["tenant-create", "muster", "Muster GmbH"], einst) == 0
    aus = capsys.readouterr().out.strip()
    assert aus.startswith("geschrieben: ") and "Firmencode" not in aus
    inhalt = Path(aus.removeprefix("geschrieben: ")).read_text(encoding="utf-8")
    assert "Firmencode: " in inhalt


def test_code_rotieren_und_geraet_freigeben(einst, client, capsys):
    cli.main(["tenant-create", "muster", "Muster GmbH", "--show"], einst)
    alt = re.search(r"Firmencode: (\S+)", capsys.readouterr().out).group(1)
    g = enroll(client, alt)
    cli.main(["code-rotate", "muster", "--show"], einst)
    neu = re.search(r"Firmencode: (\S+)", capsys.readouterr().out).group(1)
    assert neu != alt
    assert client.post("/api/v1/enroll", json={"code": alt, "platform": "x"}).status_code == 404
    cli.main(["code-show", "muster", "--show"], einst)
    assert neu in capsys.readouterr().out
    cli.main(["device-list", "muster"], einst)
    assert g["deviceId"] in capsys.readouterr().out
    assert cli.main(["device-approve", g["deviceId"]], einst) == 0
    assert sql(einst, "SELECT status FROM geraet WHERE id = ?", (g["deviceId"],))[0][0] == "approved"
    assert cli.main(["device-block", "d_gibtsnicht"], einst) == 1


def test_instanz_admin_und_fehler(einst, capsys):
    assert cli.main(["instance-admin-create", "root", "--show"], einst) == 0
    assert tuple(sql(einst, "SELECT rolle, firma_id FROM admin WHERE benutzer = 'root'")[0]) == ("instance", None)
    assert cli.main(["admin-create", "gibtsnicht", "x"], einst) == 1
    assert "fehler: unbekannte Firma" in capsys.readouterr().err
    assert cli.main(["tenant-create", "A B", "x"], einst) == 1


def test_key_set_liest_key_von_stdin(einst, capsys, monkeypatch):
    """W2-SRV2: Zugang per CLI (Docker-CI, Server ohne Portal); der Key kommt nie als Argument."""
    import io

    cli.main(["tenant-create", "muster", "Muster GmbH"], einst)
    monkeypatch.setattr("sys.stdin", io.StringIO("test-key-not-real-5\n"))
    assert cli.main(["key-set", "muster", "stt", "--provider", "groq", "--model", "whisper-large-v3-turbo"], einst) == 0
    zeile = sql(einst, "SELECT anbieter, modell, letzte4 FROM schluessel WHERE zugang = 'stt'")[0]
    assert tuple(zeile) == ("groq", "whisper-large-v3-turbo", "al-5")
    assert "test-key-not-real-5" not in capsys.readouterr().out
    monkeypatch.setattr("sys.stdin", io.StringIO("\n"))  # eigener Server ohne Key, aber internes Ziel ohne Freigabe
    assert cli.main(["key-set", "muster", "llm", "--provider", "custom", "--base-url", "http://127.0.0.1:9/v1"], einst) == 1
    assert "WL_UPSTREAM_ALLOW" in capsys.readouterr().err
    monkeypatch.setattr("sys.stdin", io.StringIO("\n"))
    assert cli.main(["key-set", "muster", "stt", "--provider", "groq"], einst) == 1  # groq braucht einen Key
    assert "kein_key" in capsys.readouterr().err


def test_firma_loeschen_nur_mit_bestaetigung(einst, client, capsys):
    """W2-DEMO: Testfirma der Live-Suite wieder entfernen; alles der Firma weg, andere Firmen und Audit bleiben."""
    cli.main(["tenant-create", "weg", "Weg GmbH", "--owner", "weg-chef", "--show"], einst)
    code = re.search(r"Firmencode: (\S+)", capsys.readouterr().out).group(1)
    cli.main(["tenant-create", "bleibt", "Bleibt GmbH"], einst)
    g = enroll(client, code)
    assert cli.main(["tenant-delete", "weg"], einst) == 1
    assert "--confirm" in capsys.readouterr().err
    assert cli.main(["tenant-delete", "weg", "--confirm", "bleibt"], einst) == 1
    assert sql(einst, "SELECT COUNT(*) FROM firma WHERE slug = 'weg'")[0][0] == 1
    assert cli.main(["tenant-delete", "weg", "--confirm", "weg"], einst) == 0
    assert [r[0] for r in sql(einst, "SELECT slug FROM firma")] == ["bleibt"]
    assert sql(einst, "SELECT COUNT(*) FROM geraet WHERE id = ?", (g["deviceId"],))[0][0] == 0
    assert sql(einst, "SELECT COUNT(*) FROM admin WHERE benutzer = 'weg-chef'")[0][0] == 0
    assert sql(einst, "SELECT COUNT(*) FROM audit WHERE aktion = 'firma_geloescht'")[0][0] == 1
    assert client.post("/api/v1/enroll", json={"code": code, "platform": "x"}).status_code == 404
    assert cli.main(["tenant-delete", "weg", "--confirm", "weg"], einst) == 1  # schon gelöscht


def test_admin_mit_passwort_von_stdin(einst, client, capsys, monkeypatch):
    """W2-DEMO: Startpasswort per stdin statt Einladung (Deploy-Skript); Regel wie im Portal, nie in der Ausgabe."""
    import io

    from .conftest import passwort_login

    pw = "test-passwort-nicht-echt-9"
    monkeypatch.setattr("sys.stdin", io.StringIO(pw + "\n"))
    assert cli.main(["instance-admin-create", "root", "--password-stdin"], einst) == 0
    aus = capsys.readouterr().out
    assert "Passwort gesetzt" in aus and pw not in aus and "/einladung/" not in aus
    assert sql(einst, "SELECT COUNT(*) FROM einladung")[0][0] == 0
    r = passwort_login(client, "root", pw)
    assert r.status_code == 303 and r.headers["location"] == "/login/totp-einrichten"

    cli.main(["tenant-create", "muster", "Muster GmbH"], einst)
    monkeypatch.setattr("sys.stdin", io.StringIO(pw + "\n"))
    assert cli.main(["admin-create", "muster", "chef", "--password-stdin"], einst) == 0
    assert "Passwort gesetzt" in capsys.readouterr().out
    monkeypatch.setattr("sys.stdin", io.StringIO("kurz\n"))
    assert cli.main(["admin-create", "muster", "chef2", "--password-stdin"], einst) == 1
    assert "pw_kurz" in capsys.readouterr().err
    assert sql(einst, "SELECT COUNT(*) FROM admin WHERE benutzer = 'chef2'")[0][0] == 0
