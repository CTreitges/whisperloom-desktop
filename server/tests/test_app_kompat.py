"""App-Kompatibilität (W2-SRV2, Akzeptanz 3): Der unveränderte OpenAI-kompatible Client der App (C#, WhisperLoom.Core)
schickt mit Anbieter `custom`, Base-URL = Proxy und Key = Geräte-Token eine Erkennung und eine Text-Stufe durch den
Proxy gegen Fake-Anbieter. Der Server läuft dafür als echter uvicorn-Prozess (die App braucht einen TCP-Port).

Braucht das .NET-SDK aus global.json; ohne `dotnet` wird der Test übersprungen (CI: server-tests richtet es ein).
"""

import os
import shutil
import socket
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

import pytest

from .conftest import FAKE_KEY, enroll, geraet_setzen, neue_einst, neue_firma, zugang

KOMPAT = Path(__file__).parent / "app_kompat" / "kompat.cs"
REPO = Path(__file__).resolve().parents[2]


def _freier_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


@pytest.mark.skipif(shutil.which("dotnet") is None, reason="dotnet-SDK fehlt")
def test_app_client_spricht_mit_dem_proxy(tmp_path, uhr, fake):
    port = _freier_port()
    basis = f"http://127.0.0.1:{port}"
    einst = neue_einst(tmp_path, uhr, basis_url=basis)
    f = neue_firma(einst, "kompat", key_modus="proxy")
    zugang(einst, f["id"], "stt", "groq", fake.basis + "/v1")
    zugang(einst, f["id"], "llm", "anthropic", fake.basis + "/v1")
    env = {**os.environ, "WL_DATA_DIR": str(einst.daten), "WL_BASE_URL": basis, "WL_UPSTREAM_ALLOW": "127.0.0.1/32"}
    server = subprocess.Popen(  # noqa: S603 (feste Argumente)
        [sys.executable, "-m", "whisperloom_server", "serve", "--host", "127.0.0.1", "--port", str(port)],
        env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(100):
            try:
                with urllib.request.urlopen(basis + "/healthz", timeout=1) as r:  # noqa: S310 (lokal)
                    if r.status == 200:
                        break
            except OSError:
                time.sleep(0.1)
        from fastapi.testclient import TestClient

        from whisperloom_server.app import create_app

        with TestClient(create_app(einst), base_url=basis) as c:  # gleiche DB, nur fürs Enrollment
            g = enroll(c, f["code"], name="App-Kompat")
        geraet_setzen(einst, g["deviceId"], status="approved")
        lauf = subprocess.run(  # noqa: S603
            ["dotnet", "run", str(KOMPAT), "--", basis + "/api/v1/proxy", g["deviceToken"]],  # noqa: S607
            cwd=REPO, capture_output=True, text=True, timeout=600, env={**os.environ, "DOTNET_CLI_TELEMETRY_OPTOUT": "1"})
    finally:
        server.terminate()  # gespeicherter Prozess, nur dieser
        server.wait(timeout=10)
    print(lauf.stdout)  # im Log der Testläufe sichtbar (-s): Ausgabe des App-Clients
    assert lauf.returncode == 0, lauf.stdout[-2000:] + lauf.stderr[-2000:]
    assert "STT Hallo von haupt" in lauf.stdout and "LLM Hallo von haupt" in lauf.stdout
    erkennung = next(a for a in fake.anfragen if a["pfad"].endswith("/audio/transcriptions"))
    text = next(a for a in fake.anfragen if a["pfad"].endswith("/messages"))
    assert erkennung["header"]["authorization"] == f"Bearer {FAKE_KEY}" and text["header"]["x-api-key"] == FAKE_KEY
    assert erkennung["felder"]["model"] == ["whisper-x"] and erkennung["datei"] == 44 + 32_000
    assert "temperature" not in text["json"]  # Anthropic-Adapter lässt sie weg (API-Version 2026-10-21)
    assert g["deviceToken"] not in str(fake.anfragen)
