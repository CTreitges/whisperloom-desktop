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
