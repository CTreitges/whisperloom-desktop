"""Betrieb: Smoke-Skript als echter Prozess, Dockerfile-Grundregeln, Migrationen, Hauptschlüssel aus Env."""

import subprocess
import sys
from pathlib import Path

import pytest

from whisperloom_server import db
from whisperloom_server.config import Einstellungen, KonfigFehler, lade_master

SERVER = Path(__file__).parent.parent


def test_smoke_skript_laeuft_durch():
    r = subprocess.run([sys.executable, "-X", "utf8", str(SERVER / "tools" / "smoke_server.py"), "--python", sys.executable],
                       capture_output=True, timeout=180)
    aus = r.stdout.decode("utf-8")
    assert r.returncode == 0, aus + r.stderr.decode("utf-8", "replace")
    assert aus.strip().splitlines()[-1] == "SERVER SMOKE OK"


def test_dockerfile_nicht_root_mit_healthcheck():
    text = (SERVER / "Dockerfile").read_text(encoding="utf-8")
    assert "USER whisperloom" in text and "HEALTHCHECK" in text and "/healthz" in text
    assert text.index("USER whisperloom") < text.index("CMD [")


def _pins(datei: Path) -> dict[str, str]:
    """name==version je Zeile (Fortsetzungen, Hashes, Kommentare und Marker ignoriert), Namen nach PEP 503."""
    import re

    pins = {}
    for zeile in datei.read_text(encoding="utf-8").splitlines():
        m = re.match(r"^([A-Za-z0-9._-]+)==([^\s;\\]+)", zeile)
        if m:
            pins[re.sub(r"[-_.]+", "-", m.group(1)).lower()] = m.group(2)
    return pins


def test_dockerfile_digest_und_gehashte_abhaengigkeiten():
    """Review N-16: Basis-Image per Digest, pip nur mit Hashes; die Hash-Datei folgt requirements.txt."""
    import re

    text = (SERVER / "Dockerfile").read_text(encoding="utf-8")
    assert re.search(r"^FROM python:3\.12-slim@sha256:[0-9a-f]{64}$", text, re.M), "Basis-Image ohne Digest"
    assert "pip install --require-hashes --no-deps -r requirements-hashes.txt" in text
    assert "COPY requirements-hashes.txt ." in text
    gehasht = SERVER / "requirements-hashes.txt"
    assert _pins(gehasht) == _pins(SERVER / "requirements.txt")
    bloecke = re.split(r"\n(?=[A-Za-z0-9])", gehasht.read_text(encoding="utf-8").split("\n", 4)[-1])
    assert all("--hash=sha256:" in b for b in bloecke if "==" in b)


def test_migrationen_idempotent(tmp_path):
    pfad = tmp_path / "x.db"
    db.einrichten(pfad)
    db.einrichten(pfad)
    con = db.verbinden(pfad)
    assert con.execute("PRAGMA user_version").fetchone()[0] == db.SCHEMA_VERSION
    con.execute(f"PRAGMA user_version = {db.SCHEMA_VERSION + 1}")
    con.close()
    with pytest.raises(RuntimeError):
        db.einrichten(pfad)


def test_hauptschluessel_aus_env_und_fehler(tmp_path):
    e = Einstellungen(daten=tmp_path, master_hex="ab" * 32)
    assert lade_master(e) == bytes.fromhex("ab" * 32)
    for falsch in ("zz" * 32, "ab" * 16):
        with pytest.raises(KonfigFehler):
            lade_master(Einstellungen(daten=tmp_path, master_hex=falsch))
    with pytest.raises(KonfigFehler):
        lade_master(Einstellungen(daten=tmp_path / "leer"))
