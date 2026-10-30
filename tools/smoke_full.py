"""Suite "full" von tools/smoke.py: echte Pipeline gegen tools/fake_api.py.

Setzt die Verdrahtung aus WLD-INT voraus (vorher scheitern die Prüfungen, weil die App die Fake-Pipeline nutzt).
Erwartete Einstellungen: stt.providerId "custom" mit baseUrl auf die Fake-API, Keys über "key set", Proxy über
den Abschnitt desktop (proxyMode "manual"). Prüfungen: toggle-output, language-en, fallback-stt, proxy.
"""
from __future__ import annotations

import json
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

TOOLS = Path(__file__).resolve().parent


def _get(url: str) -> dict:
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(url, timeout=10) as r:
        return json.loads(r.read())


def _post(url: str, payload: dict) -> None:
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    req = urllib.request.Request(url, data=json.dumps(payload).encode(), headers={"Content-Type": "application/json"}, method="POST")
    opener.open(req, timeout=10).read()


def run(args, work: Path, report, app_run, fresh_dir, write_test_wav) -> None:
    fake = subprocess.Popen([sys.executable, str(TOOLS / "fake_api.py")], stdout=subprocess.PIPE, text=True)
    try:
        line = fake.stdout.readline().split()
        port, proxy = int(line[1].split("=")[1]), int(line[2].split("=")[1])
        base = f"http://127.0.0.1:{port}"
        data = args.data_dir or work / args.id / "smoke-data-full"
        fresh_dir(data)
        wav = work / args.id / "smoke-test.wav"
        out = work / args.id / "smoke-output-full.txt"
        write_test_wav(wav)
        out.unlink(missing_ok=True)
        settings = {
            "language": "en",
            "stt": {"providerId": "custom", "baseUrl": f"{base}/v1", "model": "fake-stt"},
            "sttFallback": {"enabled": True, "providerId": "custom", "baseUrl": f"{base}/fallback/v1", "model": "fake-stt"},
            "llm": {"providerId": "custom", "baseUrl": f"{base}/v1", "model": "fake-llm"},
            "refine": {"stage": "prompt", "promptEnabled": True},
            "desktop": {"proxyMode": "manual", "proxyUrl": f"http://127.0.0.1:{proxy}"},
        }
        (data / "settings.json").write_text(json.dumps(settings), encoding="utf-8")
        prefix = f"WhisperLoom-Test-{args.id}:"
        run = app_run(args.app, data, prefix, ["--test-audio", str(wav), "--output-file", str(out)])
        try:
            run.start()
            for name in ("stt", "stt_fallback", "llm"):
                run.cli("key", "set", name, stdin="test-key-not-real-2\n")

            def dictate() -> dict:
                run.cli("toggle")
                time.sleep(0.3)
                run.cli("toggle")
                return run.status() or {}

            s = dictate()
            text = out.read_text(encoding="utf-8") if out.exists() else ""
            report.check("toggle-output", "[fake-llm]" in text and text == s.get("lastResult"), f"out={text[:60]!r}")
            reqs = _get(f"{base}/_requests")["requests"]
            stt = [r for r in reqs if r["path"].endswith("/audio/transcriptions")]
            chat = [r for r in reqs if r["path"].endswith("/chat/completions")]
            report.check("language-en", bool(stt) and stt[-1].get("language") == "en" and bool(chat)
                         and not chat[-1].get("system_start", "").startswith("Du "), f"stt={stt[-1:]} chat={chat[-1:]}")
            report.check("proxy", any(r.get("via_proxy") for r in reqs), "keine Anfrage über den Proxy")

            _post(f"{base}/_reset", {})
            _post(f"{base}/_control", {"fail": {"/v1/audio/transcriptions": {"status": 500, "count": 1}}})
            s = dictate()
            reqs = _get(f"{base}/_requests")["requests"]
            used_fallback = any(r["path"] == "/fallback/v1/audio/transcriptions" for r in reqs)
            report.check("fallback-stt", used_fallback and bool(s.get("lastResult")), f"fallback={used_fallback}")
        finally:
            run.stop()
            from smoke_platform import cleanup_secrets  # noqa: PLC0415
            cleanup_secrets(args.platform, prefix, data)
    finally:
        fake.kill()  # eigener Kindprozess (gespeicherte Popen-PID)
        fake.wait(timeout=10)
