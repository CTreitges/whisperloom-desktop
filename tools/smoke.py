"""Smoke-Tests gegen eine gebaute WhisperLoom-App (Windows oder Linux).

Aufruf: py -3 -X utf8 tools/smoke.py --platform win|linux --suite shell|full|offline|provision|ui|input --id <ID> --app <exe>
Je Prüfung eine Zeile "SMOKE <name> OK" bzw. "SMOKE <name> FAIL <grund>", am Ende "SMOKE SUITE <suite> OK|FAIL n/m".

Suites:
- shell: App-Hülle gegen tools/fake_api.py (direkt, ohne Proxy) — ipc-toggle, single-instance, status-json,
  secret-store, no-audio-stored, logs-clean, history, test-audio. Ausgabe-Senke --output-file, eigener Datenordner,
  Key-Präfix WhisperLoom-Test-<ID>: (Testeinträge werden gelöscht). Keine echte Eingabe, keine Zwischenablage.
- full: echte Pipeline gegen tools/fake_api.py über den Fake-Proxy (Goal-File A2.2, tools/smoke_full.py):
  toggle-output, language-en, proxy, history, fallback-stt, secret-store, anthropic-prompt, logs-clean, no-audio-stored.
- offline: lokale Erkennung (whisper-server, Modell tiny, CPU) und Ollama-Fake ohne Netz-Fakes (tools/smoke_offline.py):
  offline-stt, offline-refine-ollama, raw-plus-rules. Die Vorbereitung lädt Modell, Begleitprozess und jfk.wav einmal.
- provision: IT-Provisioning (tools/smoke_provision.py) — provision-import, policy-lock, export-no-secrets.
  Eigener Datenordner, Key-Präfix WhisperLoom-Test-<ID>: (wird gelöscht), keine Eingabe, keine Zwischenablage.
- ui: echte Pipeline gegen tools/fake_api.py, Ausgabe in die echte Zwischenablage — clipboard-real, unter Windows
  zusätzlich tray-visible (Symbol der App-PID beim Shell registriert).
- input (nur Windows): echte Tastenkürzel per SendInput gegen tests/WhisperLoom.TestTarget (tools/smoke_input.py,
  Goal-File A3.1); App mit --test-audio --hotkeys gegen tools/fake_api.py. --target <exe> statt Build des TestTargets.
- ui / input: echte Zwischenablage bzw. Eingaben — nur mit gültigem Lock <work>/ui-smoke.lock für --id
  ({"id": "<ID>", "until": "<ISO-Zeit>"}), sonst Abbruch mit Exit 3.

Prozesse startet und beendet das Skript nur über die eigene PID.
"""
from __future__ import annotations

import argparse
import datetime as dt
import glob
import json
import math
import os
import shutil
import struct
import subprocess
import sys
import tempfile
import time
import wave
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import smoke_full  # noqa: E402
import smoke_offline  # noqa: E402
import smoke_platform as plat  # noqa: E402
import smoke_provision  # noqa: E402

AUDIO_EXT = (".wav", ".pcm", ".opus")
FAKE_KEY = "test-key-not-real-1"
TEST_SECONDS = 1.5


def default_work_dir(platform: str) -> Path:
    env = os.environ.get("WLD_WORK")
    if env:
        return Path(env)
    candidate = Path.home() / ".claude" / "backups" / "whisperloom-desktop-work"
    if platform == "win" and candidate.is_dir():
        return candidate
    return Path.home() / ".whisperloom-smoke"


def default_lock_path(platform: str, work: Path) -> Path:
    env = os.environ.get("WLD_UI_LOCK")
    if env:
        return Path(env)
    if platform == "linux":
        # Unter WSL teilt sich die Zwischenablage mit Windows: derselbe Lock wie dort.
        for d in glob.glob("/mnt/c/Users/*/.claude/backups/whisperloom-desktop-work"):
            return Path(d) / "ui-smoke.lock"
    return work / "ui-smoke.lock"


def lock_valid(lock: Path, smoke_id: str, now: dt.datetime | None = None) -> tuple[bool, str]:
    """Gültig = Datei lesbar, id passt, until in der Zukunft."""
    try:
        data = json.loads(lock.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return False, f"kein gültiger Lock unter {lock}"
    if data.get("id") != smoke_id:
        return False, f"Lock gehört {data.get('id')!r}, nicht {smoke_id!r}"
    try:
        until = dt.datetime.fromisoformat(str(data.get("until")))
    except ValueError:
        return False, "Lock ohne gültiges until"
    if until.tzinfo is None:
        until = until.astimezone()
    if until <= (now or dt.datetime.now(dt.timezone.utc)):
        return False, f"Lock abgelaufen ({data.get('until')})"
    return True, "ok"


def write_test_wav(path: Path, seconds: float = TEST_SECONDS) -> None:
    """Sinus 440 Hz, 16 kHz mono PCM16 — ein Testsignal, keine Sprache."""
    path.parent.mkdir(parents=True, exist_ok=True)
    rate = 16000
    frames = b"".join(struct.pack("<h", int(8000 * math.sin(2 * math.pi * 440 * i / rate))) for i in range(int(rate * seconds)))
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(frames)


class AppRun:
    """Eine App-Instanz mit eigenem Datenordner; Beenden nur über die eigene PID."""

    def __init__(self, app: str, data_dir: Path, prefix: str, extra: list[str]):
        self.app = app
        self.base = ["--data-dir", str(data_dir), "--secret-prefix", prefix]
        self.extra = extra
        self.proc: subprocess.Popen | None = None

    def cli(self, *args: str, stdin: str | None = None, timeout: float = 60) -> subprocess.CompletedProcess:
        return subprocess.run([self.app, *args, *self.base], input=stdin, capture_output=True, text=True,
                              encoding="utf-8", timeout=timeout)

    def status(self) -> dict | None:
        r = self.cli("status", "--json", timeout=20)
        try:
            data = json.loads(r.stdout.strip().splitlines()[-1]) if r.stdout.strip() else None
        except ValueError:
            return None
        return data if data and data.get("running") else None

    def start(self, timeout: float = 30) -> dict:
        self.proc = subprocess.Popen([self.app, *self.base, *self.extra], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        deadline = time.time() + timeout
        while time.time() < deadline:
            if self.proc.poll() is not None:
                raise RuntimeError(f"App beendet sich sofort (Exit {self.proc.returncode})")
            s = self.status()
            if s:
                return s
            time.sleep(0.3)
        raise RuntimeError("App meldet sich nicht über die IPC")

    def stop(self) -> None:
        if not self.proc:
            return
        try:
            self.cli("quit", timeout=15)
            self.proc.wait(timeout=15)
        except (subprocess.TimeoutExpired, OSError):
            self.proc.kill()  # nur der eigene Prozess (gespeicherte PID)
            self.proc.wait(timeout=10)


class Report:
    def __init__(self) -> None:
        self.results: list[tuple[str, bool]] = []

    def check(self, name: str, ok: object, reason: str = "") -> bool:
        ok = bool(ok)
        self.results.append((name, ok))
        print(f"SMOKE {name} OK" if ok else f"SMOKE {name} FAIL {reason}", flush=True)
        return ok

    def finish(self, suite: str) -> int:
        good = sum(ok for _, ok in self.results)
        ok = good == len(self.results) and good > 0
        print(f"SMOKE SUITE {suite} {'OK' if ok else 'FAIL'} {good}/{len(self.results)}")
        return 0 if ok else 1


def audio_files(dirs: list[Path], since: float) -> list[str]:
    found = []
    for d in dirs:
        for root, _, files in os.walk(d):
            for f in files:
                p = Path(root) / f
                try:
                    if p.suffix.lower() in AUDIO_EXT and p.stat().st_mtime >= since:
                        found.append(str(p))
                except OSError:
                    pass
    return found


def fresh_dir(path: Path) -> None:
    """Leert nur den eigenen Smoke-Datenordner (Markierungsdatei), nie fremde Ordner."""
    marker = path / ".whisperloom-smoke"
    if path.exists():
        if not marker.exists() and any(path.iterdir()):
            raise RuntimeError(f"{path} ist kein Smoke-Datenordner (Markierung fehlt) — nicht angefasst")
        shutil.rmtree(path)
    path.mkdir(parents=True)
    marker.write_text("smoke\n", encoding="utf-8")


def suite_shell(args, work: Path, report: Report) -> None:
    with smoke_full.FakeApi() as fake:
        shell_checks(args, work, report, fake)


def shell_checks(args, work: Path, report: Report, fake: smoke_full.FakeApi) -> None:
    data = args.data_dir or work / args.id / "smoke-data"
    fresh_dir(data)
    wav = work / args.id / "smoke-test.wav"
    out = work / args.id / "smoke-output.txt"
    write_test_wav(wav)
    out.unlink(missing_ok=True)
    (data / "settings.json").write_text(json.dumps(smoke_full.fake_settings(fake, via_proxy=False)), encoding="utf-8")
    prefix = f"WhisperLoom-Test-{args.id}:"
    since = time.time() - 1
    run = AppRun(args.app, data, prefix, ["--test-audio", str(wav), "--output-file", str(out)])
    try:
        first = run.start()
        s = run.status() or {}
        report.check("status-json", s.get("pid") == first.get("pid") and s.get("state") == "idle"
                     and s.get("stage") == "prompt" and s.get("language") == "en" and "keys" in s,
                     f"unerwartet: {s}")

        second = run.cli("status", "--json", timeout=20)
        other = json.loads(second.stdout.strip() or "{}")
        report.check("single-instance", second.returncode == 0 and other.get("pid") == first.get("pid")
                     and run.proc.poll() is None, f"zweiter Start: rc={second.returncode} pid={other.get('pid')}")

        t1 = run.cli("toggle")
        mid = run.status() or {}
        time.sleep(0.5)
        t2 = run.cli("toggle")
        after = run.status() or {}
        result = out.read_text(encoding="utf-8") if out.exists() else ""
        report.check("ipc-toggle", t1.returncode == 0 and mid.get("state") == "recording" and t2.returncode == 0
                     and after.get("state") == "idle" and result != "" and result == after.get("lastResult"),
                     f"t1={t1.stdout.strip()} mid={mid.get('state')} t2={t2.stdout.strip()} out={len(result)} Zeichen")

        log_dir = data / "logs"
        logs = "".join(p.read_text(encoding="utf-8", errors="replace") for p in log_dir.glob("*")) if log_dir.exists() else ""
        dictation = [line for line in logs.splitlines() if " dictation " in line and "status=ok" in line]
        duration = next((int(f.split("=")[1]) for f in dictation[-1].split() if f.startswith("durationMs=")), -1) if dictation else -1
        report.check("test-audio", abs(duration - TEST_SECONDS * 1000) <= 50, f"durationMs={duration}")

        h = after.get("history", {})
        latest = h.get("latest") or {}
        report.check("history", h.get("count") == 1 and latest.get("raw") == after.get("lastRaw")
                     and latest.get("text") == after.get("lastResult") and latest.get("raw"), f"history={h}")

        report.check(*check_secret_store(args.platform, run, data, prefix))

        logs = "".join(p.read_text(encoding="utf-8", errors="replace") for p in log_dir.glob("*")) if log_dir.exists() else ""
        leaks = [t for t in (after.get("lastRaw"), after.get("lastResult"), FAKE_KEY) if t and t in logs]
        report.check("logs-clean", logs != "" and not leaks, f"{len(leaks)} Treffer im Log" if logs else "kein Log")

        found = audio_files([data, Path(tempfile.gettempdir())], since)
        report.check("no-audio-stored", not found, ", ".join(found[:5]))
    finally:
        run.stop()
        plat.cleanup_secrets(args.platform, prefix, data)


def check_secret_store(platform: str, run: AppRun, data: Path, prefix: str) -> tuple[str, bool, str]:
    name = "secret-store"
    r = run.cli("key", "set", "stt", stdin=FAKE_KEY + "\n")
    if r.returncode != 0:
        return name, False, f"key set: rc={r.returncode} {r.stderr.strip()}"
    s = run.status() or {}
    stored, where = plat.read_secret(platform, prefix, "stt", data, s.get("secretStore"))
    settings = (data / "settings.json").read_text(encoding="utf-8")
    if stored != FAKE_KEY:
        return name, False, f"nicht in der Ablage ({where})"
    if FAKE_KEY in settings:
        return name, False, "Key steht in settings.json"
    if (s.get("keys") or {}).get("stt") is not True:
        return name, False, "status meldet den Key nicht"
    if FAKE_KEY in json.dumps(s):
        return name, False, "status zeigt den Key"
    run.cli("key", "delete", "stt")
    gone, _ = plat.read_secret(platform, prefix, "stt", data, s.get("secretStore"))
    if gone is not None:
        return name, False, "key delete hat nicht gelöscht"
    return name, True, where


def suite_ui(args, work: Path, report: Report) -> None:
    with smoke_full.FakeApi() as fake:
        ui_checks(args, work, report, fake)


def wait_for(probe, seconds: float, step: float = 0.3):
    """Ruft probe() bis zu <seconds> lang auf, bis es etwas liefert; sonst das letzte Ergebnis (None/False)."""
    deadline = time.time() + seconds
    while True:
        value = probe()
        if value or time.time() >= deadline:
            return value
        time.sleep(step)


def ui_checks(args, work: Path, report: Report, fake: smoke_full.FakeApi) -> None:
    """Echte Pipeline gegen die Fake-API (direkt), Ausgabe in die echte Zwischenablage; Windows zusätzlich das Tray-Symbol."""
    data = args.data_dir or work / args.id / "smoke-data-ui"
    fresh_dir(data)
    wav = work / args.id / "smoke-test.wav"
    write_test_wav(wav)
    (data / "settings.json").write_text(json.dumps(smoke_full.fake_settings(fake, via_proxy=False)), encoding="utf-8")
    prefix = f"WhisperLoom-Test-{args.id}:"
    run = AppRun(args.app, data, prefix, ["--test-audio", str(wav)])
    try:
        run.start()
        if args.platform == "win":
            where = wait_for(lambda: plat.tray_icon(run.proc.pid), 10)
            report.check("tray-visible", where, f"kein Tray-Symbol der PID {run.proc.pid} beim Shell registriert")
        s = smoke_full.dictate(run)
        result = str(s.get("lastResult") or "")
        clip = plat.read_clipboard(args.platform)
        report.check("clipboard-real", result.startswith("[fake-llm]") and clip == result,
                     f"clip={clip!r:.40} result={result!r:.40} error={s.get('lastError')}")
    finally:
        run.stop()
        plat.cleanup_secrets(args.platform, prefix, data)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="WhisperLoom-Smokes")
    parser.add_argument("--platform", choices=["win", "linux"], required=True)
    parser.add_argument("--suite", choices=["shell", "full", "offline", "provision", "ui", "input"], default="shell")
    parser.add_argument("--id", required=True)
    parser.add_argument("--app", required=True)
    parser.add_argument("--work-dir", type=Path)
    parser.add_argument("--data-dir", type=Path)
    parser.add_argument("--lock", type=Path)
    parser.add_argument("--target", help="TestTarget-Exe für --suite input (sonst aus dem Repo gebaut)")
    args = parser.parse_args(argv)
    args.app = str(Path(args.app).resolve())
    work = args.work_dir or default_work_dir(args.platform)

    if args.suite in ("ui", "input"):
        ok, why = lock_valid(args.lock or default_lock_path(args.platform, work), args.id)
        if not ok:
            print(f"SMOKE VERWEIGERT {args.suite}: {why}")
            return 3
    report = Report()
    if args.suite == "shell":
        suite_shell(args, work, report)
    elif args.suite == "ui":
        suite_ui(args, work, report)
    elif args.suite == "provision":
        smoke_provision.run(args, work, report, sys.modules[__name__])
    elif args.suite == "full":
        smoke_full.run(args, work, report, sys.modules[__name__])
    elif args.suite == "offline":
        smoke_offline.run(args, work, report, sys.modules[__name__])
    elif args.platform != "win":
        print("SMOKE input SKIP nur Windows (Linux-Hotkeys sind Out-of-Scope)")
        return 0
    else:
        import smoke_input
        smoke_input.run_suite(args, work, report, sys.modules[__name__])
    return report.finish(args.suite)


if __name__ == "__main__":
    sys.exit(main())
