"""Suite "full" von tools/smoke.py: echte Pipeline gegen tools/fake_api.py (Goal-File A2.2).

Alle Anfragen laufen über den Fake-Proxy: die Fake-API heißt für die App "fake-api.invalid" (den Namen löst nur der
Proxy auf), Loopback-Ziele würden jeden Proxy umgehen. Ausgabe-Senke --output-file, eigener Datenordner,
Key-Präfix WhisperLoom-Test-<ID>: (Testeinträge werden gelöscht), nur der Fake-Key test-key-not-real-1.

Prüfungen: toggle-output, language-en, proxy, history, fallback-stt, secret-store, anthropic-prompt, logs-clean,
no-audio-stored. Hilfen für die Suite "shell": FakeApi, fake_settings.
"""
from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import time
import urllib.request
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
FAKE_HOST = "fake-api.invalid"
EN_PROMPT = (TOOLS.parent / "src/WhisperLoom.Core/Prompts/en/prompt.txt").read_text(encoding="utf-8")[:40]


def _opener():
    return urllib.request.build_opener(urllib.request.ProxyHandler({}))


class FakeApi:
    """tools/fake_api.py als eigener Kindprozess; Beenden über die gespeicherte Popen-PID."""

    def __enter__(self) -> FakeApi:
        self.proc = subprocess.Popen([sys.executable, str(TOOLS / "fake_api.py")], stdout=subprocess.PIPE, text=True)
        line = self.proc.stdout.readline().split()
        self.port, self.proxy_port = int(line[1].split("=")[1]), int(line[2].split("=")[1])
        self.base = f"http://127.0.0.1:{self.port}"
        return self

    def __exit__(self, *exc) -> None:
        self.proc.kill()
        self.proc.wait(timeout=10)

    def app_base(self, via_proxy: bool) -> str:
        return f"http://{FAKE_HOST}:{self.port}" if via_proxy else self.base

    def requests(self) -> list[dict]:
        with _opener().open(f"{self.base}/_requests", timeout=10) as r:
            return [q for q in json.loads(r.read())["requests"] if not q["path"].startswith("proxy:")]

    def post(self, path: str, payload: dict) -> None:
        req = urllib.request.Request(f"{self.base}{path}", data=json.dumps(payload).encode(),
                                     headers={"Content-Type": "application/json"}, method="POST")
        _opener().open(req, timeout=10).read()


def fake_settings(fake: FakeApi, via_proxy: bool, llm: dict | None = None) -> dict:
    """Englisch, Stufe Prompt, Erkennung und Text über den eigenen Server (Fake), Fallback-Erkennung unter /fallback."""
    base = fake.app_base(via_proxy)
    settings = {
        "language": "en",
        "stt": {"providerId": "custom", "baseUrl": f"{base}/v1", "model": "fake-stt"},
        "sttFallback": {"enabled": True, "providerId": "custom", "baseUrl": f"{base}/fallback/v1", "model": "fake-stt"},
        "llm": llm or {"providerId": "custom", "baseUrl": f"{base}/v1", "model": "fake-llm"},
        "refine": {"stage": "prompt", "promptEnabled": True},
    }
    if via_proxy:
        settings["desktop"] = {"proxyMode": "manual", "proxyUrl": f"http://127.0.0.1:{fake.proxy_port}"}
    return settings


def dictate(run) -> dict:
    run.cli("toggle")
    time.sleep(0.3)
    run.cli("toggle")
    return run.status() or {}


def run(args, work: Path, report, smoke) -> None:
    data = args.data_dir or work / args.id / "smoke-data-full"
    smoke.fresh_dir(data)
    wav = work / args.id / "smoke-test.wav"
    out = work / args.id / "smoke-output-full.txt"
    smoke.write_test_wav(wav)
    out.unlink(missing_ok=True)
    prefix = f"WhisperLoom-Test-{args.id}:"
    since = time.time() - 1
    extra = ["--test-audio", str(wav), "--output-file", str(out)]
    last: dict = {}
    with FakeApi() as fake:
        try:
            (data / "settings.json").write_text(json.dumps(fake_settings(fake, via_proxy=True)), encoding="utf-8")
            app = smoke.AppRun(args.app, data, prefix, extra)
            try:
                app.start()
                for name in ("stt", "stt_fallback", "llm"):
                    app.cli("key", "set", name, stdin=smoke.FAKE_KEY + "\n")

                s = dictate(app)
                text = out.read_text(encoding="utf-8") if out.exists() else ""
                report.check("toggle-output", text.startswith("[fake-llm]") and text == s.get("lastResult")
                             and s.get("lastModel") == "fake-llm", f"out={text[:60]!r} model={s.get('lastModel')}")
                reqs = fake.requests()
                stt = [r for r in reqs if r["path"].endswith("/audio/transcriptions")]
                chat = [r for r in reqs if r["path"].endswith("/chat/completions")]
                report.check("language-en", bool(stt) and stt[-1].get("language") == "en" and bool(chat)
                             and chat[-1].get("system_start", "").startswith(EN_PROMPT)
                             and chat[-1].get("user_start", "").startswith("<dictation>"), f"stt={stt[-1:]} chat={chat[-1:]}")
                report.check("proxy", bool(reqs) and all(r.get("via_proxy") for r in reqs),
                             f"{sum(not r.get('via_proxy') for r in reqs)} von {len(reqs)} Anfragen ohne Proxy")
                h = s.get("history", {})
                latest = h.get("latest") or {}
                report.check("history", h.get("count") == 1 and latest.get("raw") == s.get("lastRaw") != ""
                             and latest.get("text") == s.get("lastResult") and latest.get("stage") == "prompt"
                             and any((data / "history").glob("*.json")), f"history={h}")

                fake.post("/_reset", {})
                fake.post("/_control", {"fail": {"/v1/audio/transcriptions": {"status": 500, "count": 1}}})
                s = dictate(app)
                paths = [r["path"] for r in fake.requests()]
                used = "/fallback/v1/audio/transcriptions" in paths
                report.check("fallback-stt", used and "/v1/audio/transcriptions" in paths
                             and str(s.get("lastResult", "")).startswith("[fake-llm]"), f"pfade={paths}")
                report.check(*smoke.check_secret_store(args.platform, app, data, prefix))
            finally:
                app.stop()

            # Stufe Prompt über den Anthropic-kompatiblen Fake: Modell aus dem Katalog, eigener Key.
            llm = {"providerId": "anthropic", "baseUrl": f"{fake.app_base(True)}/anthropic/v1"}
            (data / "settings.json").write_text(json.dumps(fake_settings(fake, via_proxy=True, llm=llm)), encoding="utf-8")
            fake.post("/_reset", {})
            app = smoke.AppRun(args.app, data, prefix, extra)
            try:
                app.start()
                for name in ("stt", "llm"):
                    app.cli("key", "set", name, stdin=smoke.FAKE_KEY + "\n")
                s = dictate(app)
                chat = [r for r in fake.requests() if r["path"] == "/anthropic/v1/chat/completions"]
                model = chat[-1].get("model", "") if chat else ""
                report.check("anthropic-prompt", bool(chat) and chat[-1]["has_key"] and chat[-1]["via_proxy"]
                             and model.startswith("claude-") and chat[-1].get("system_start", "").startswith(EN_PROMPT)
                             and str(s.get("lastResult", "")).startswith("[fake-llm]"), f"chat={chat[-1:]}")
                last = s
            finally:
                app.stop()

            log_dir = data / "logs"
            logs = "".join(p.read_text(encoding="utf-8", errors="replace") for p in log_dir.glob("*")) if log_dir.exists() else ""
            leaks = [t for t in (last.get("lastRaw"), last.get("lastResult"), smoke.FAKE_KEY) if t and t in logs]
            report.check("logs-clean", logs != "" and not leaks, f"{len(leaks)} Treffer im Log" if logs else "kein Log")
            found = smoke.audio_files([data, Path(tempfile.gettempdir())], since)
            report.check("no-audio-stored", not found, ", ".join(found[:5]))
        finally:
            from smoke_platform import cleanup_secrets  # noqa: PLC0415
            cleanup_secrets(args.platform, prefix, data)
