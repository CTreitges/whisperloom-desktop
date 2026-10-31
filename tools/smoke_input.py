"""Suite "input" von tools/smoke.py (Goal-File A3.1, nur Windows, nur mit UI-Lock).

Echte Tastenkürzel per SendInput gegen tests/WhisperLoom.TestTarget (Fenster mit Textfeld, schreibt Text, Enter- und
Rücktasten-Zähler als JSON). Die App läuft mit --test-audio und --hotkeys gegen tools/fake_api.py (Stufe "off"),
eigener Datenordner, Key-Präfix WhisperLoom-Test-<ID>:. Die Zwischenablage des Nutzers (Text) wird am Ende
wiederhergestellt. Prozesse nur über die eigene PID.

Prüfungen: hold-insert, start-menu-suppressed, toggle-second-press, no-focus-steal, toggle-enter, ctrl-enter,
backspace-discard, pause, clipboard-restore, clipboard-history-excluded, stage-toast, type-mode.
"""
from __future__ import annotations

import json
import subprocess
import time
from pathlib import Path

import smoke_full
import smoke_platform as plat
import smoke_winput as wi

REPO = Path(__file__).resolve().parent.parent
UMLAUT_TEXT = "Grüße aus Köln: Äpfel, Öl und Übermut, ß."


def target_exe(args, work: Path) -> str:
    """--target oder TestTarget aus dem Repo bauen (Release, in den Arbeitsordner)."""
    if getattr(args, "target", None):
        return str(Path(args.target).resolve())
    out = work / args.id / "testtarget"
    subprocess.run(["dotnet", "build", str(REPO / "tests/WhisperLoom.TestTarget"), "-c", "Release", "-o", str(out)],
                   check=True, capture_output=True, text=True)
    return str(out / "WhisperLoom.TestTarget.exe")


class Target:
    """Ein TestTarget-Fenster; Fokus per echtem Klick ins Textfeld."""

    def __init__(self, exe: str, out: Path, keys: wi.Keyboard):
        self.exe, self.out, self.keys = exe, out, keys
        self.proc: subprocess.Popen | None = None
        self.hwnd = 0

    def read(self) -> dict:
        try:
            return json.loads(self.out.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return {}

    def __enter__(self) -> Target:
        self.out.unlink(missing_ok=True)
        self.proc = subprocess.Popen([self.exe, "--out", str(self.out), "--seconds", "90"])
        state = wait_for(lambda: self.read().get("hwnd"), 15)
        if not state:
            raise RuntimeError("TestTarget meldet sich nicht")
        self.hwnd = int(state)
        time.sleep(0.3)
        l, t, r, b = wi.window_rect(self.hwnd)
        self.keys.click((l + r) // 2, (t + b) // 2)
        wait_for(lambda: wi.foreground() == self.hwnd, 3, 0.05)
        return self

    def __exit__(self, *exc) -> None:
        if self.proc:
            self.proc.kill()  # nur der eigene Prozess (gespeicherte PID)
            self.proc.wait(timeout=10)


def wait_for(probe, seconds: float, step: float = 0.1):
    deadline = time.time() + seconds
    while True:
        value = probe()
        if value or time.time() >= deadline:
            return value
        time.sleep(step)


def idle(run) -> dict:
    """Wartet, bis die App fertig ist (state idle, kein Hotkey-Ablauf)."""
    def probe():
        s = run.status() or {}
        return s if s.get("state") == "idle" and (s.get("input") or {}).get("mode") == "none" else None
    return wait_for(probe, 20, 0.2) or (run.status() or {})


def inserted(target: Target, run) -> tuple[dict, dict]:
    s = idle(run)
    want = s.get("lastResult") or ""
    t = wait_for(lambda: (lambda d: d if d.get("text") == want and want else None)(target.read()), 5) or target.read()
    return s, t


def overlay_open(run) -> dict:
    return wait_for(lambda: (lambda s: s if ((s or {}).get("input") or {}).get("overlay") else None)(run.status()), 5) or {}


def run_suite(args, work: Path, report, smoke) -> None:
    wi.dpi_aware()
    keys = wi.Keyboard()
    exe = target_exe(args, work)
    saved = plat.read_clipboard("win")
    try:
        with smoke_full.FakeApi() as fake:
            paste_checks(args, work, report, smoke, fake, keys, exe)
            type_checks(args, work, report, smoke, fake, keys, exe)
    finally:
        keys.release_all()
        if saved is not None:
            wi.set_clipboard(saved)


def start_app(args, work: Path, smoke, fake, name: str, insert_mode: str):
    data = work / args.id / f"smoke-data-{name}"
    smoke.fresh_dir(data)
    wav = work / args.id / "smoke-test.wav"
    smoke.write_test_wav(wav)
    settings = smoke_full.fake_settings(fake, via_proxy=False)
    settings["refine"] = {"stage": "off", "promptEnabled": True}
    settings["desktop"] = {"insertMode": insert_mode}
    (data / "settings.json").write_text(json.dumps(settings), encoding="utf-8")
    prefix = f"WhisperLoom-Test-{args.id}:"
    run = smoke.AppRun(args.app, data, prefix, ["--test-audio", str(wav), "--hotkeys"])
    run.start()
    return run, data, prefix


def paste_checks(args, work, report, smoke, fake, keys, exe) -> None:
    run, data, prefix = start_app(args, work, smoke, fake, "input", "paste")
    out = work / args.id / "testtarget.json"
    try:
        hook = ((run.status() or {}).get("input") or {}).get("hook")
        if not hook:
            report.check("hotkeys-on", False, "Hook nicht aktiv")
            return

        # Halten: Strg+Win gedrückt halten, loslassen → einfügen; dabei kein Startmenü.
        with Target(exe, out, keys) as t:
            keys.down("ctrl")
            keys.down("win")
            time.sleep(0.8)
            keys.up("win")
            keys.up("ctrl")
            seen = set()
            deadline = time.time() + 1.5
            while time.time() < deadline:
                seen.add(wi.foreground())
                time.sleep(0.02)
            s, d = inserted(t, run)
            report.check("hold-insert", d.get("text") == s.get("lastResult") and s.get("lastResult") and d.get("enter") == 0,
                         f"ziel={d.get('text')!r:.40} result={s.get('lastResult')!r:.40} error={s.get('lastError')}")
            foreign = [h for h in seen if h != t.hwnd]
            names = sorted({wi.process_name(h) for h in foreign})
            report.check("start-menu-suppressed", not foreign and wi.foreground() == t.hwnd,
                         f"fremde Vordergrundfenster: {names}")

        # Umschalten: Strg+Leertaste öffnet das Overlay, zweiter Druck sendet.
        with Target(exe, out, keys) as t:
            keys.combo("ctrl", "space")
            s = overlay_open(run)
            fg_open = wi.foreground() == t.hwnd
            time.sleep(0.5)
            keys.combo("ctrl", "space")
            s, d = inserted(t, run)
            report.check("toggle-second-press", s.get("lastResult") and d.get("text") == s.get("lastResult") and d.get("enter") == 0,
                         f"ziel={d.get('text')!r:.40} result={s.get('lastResult')!r:.40}")
            focus_open = fg_open

        # Enter im Overlay = einfügen (das Ziel bekommt kein Enter).
        with Target(exe, out, keys) as t:
            keys.combo("ctrl", "space")
            overlay_open(run)
            time.sleep(0.4)
            keys.combo("enter")
            s, d = inserted(t, run)
            report.check("toggle-enter", d.get("text") == s.get("lastResult") and d.get("enter") == 0,
                         f"ziel={d.get('text')!r:.40} enter={d.get('enter')}")

        # Strg+Enter = einfügen + Enter.
        with Target(exe, out, keys) as t:
            keys.combo("ctrl", "space")
            overlay_open(run)
            time.sleep(0.4)
            keys.combo("ctrl", "enter")
            s, d = inserted(t, run)
            d = wait_for(lambda: (lambda x: x if x.get("enter") == 1 else None)(t.read()), 3) or t.read()
            report.check("ctrl-enter", d.get("text") == s.get("lastResult") and d.get("enter") == 1,
                         f"ziel={d.get('text')!r:.40} enter={d.get('enter')}")

        # Rücktaste = verwerfen: Ziel bleibt unverändert, die Rücktaste kommt dort nicht an.
        with Target(exe, out, keys) as t:
            before = (run.status() or {}).get("history", {}).get("count")
            keys.combo("ctrl", "space")
            overlay_open(run)
            time.sleep(0.4)
            keys.combo("back")
            s = idle(run)
            time.sleep(1.0)
            d = t.read()
            report.check("backspace-discard", d.get("text") == "" and d.get("backspace") == 0
                         and s.get("history", {}).get("count") == before and not s["input"].get("overlay"),
                         f"ziel={d.get('text')!r} backspace={d.get('backspace')} overlay={s.get('input', {}).get('overlay')}")

        pause_check(run, keys, exe, out, report, focus_open)
        clipboard_checks(run, keys, exe, out, report)
        stage_check(run, keys, exe, out, report)
    finally:
        run.stop()
        plat.cleanup_secrets("win", prefix, data)


def pause_check(run, keys, exe, out, report, focus_open: bool) -> None:
    """Pause per Knopf: Dauer steht still; Klicks auf Overlay-Knöpfe nehmen dem Ziel nicht den Fokus."""
    with Target(exe, out, keys) as t:
        keys.combo("ctrl", "space")
        s = overlay_open(run)
        time.sleep(0.6)
        buttons = (s.get("input") or {}).get("buttons") or {}
        if "pause" not in buttons:
            report.check("pause", False, f"keine Knopf-Position: {s.get('input')}")
            report.check("no-focus-steal", False, "kein Overlay")
            return
        keys.click(*buttons["pause"])
        paused = wait_for(lambda: (lambda x: x if x.get("state") == "paused" else None)(run.status() or {}), 3) or {}
        fg_click = wi.foreground() == t.hwnd
        e1 = paused.get("elapsedMs", -1)
        time.sleep(1.2)
        e2 = (run.status() or {}).get("elapsedMs", -2)
        keys.click(*buttons["pause"])
        time.sleep(0.5)
        resumed = (run.status() or {}).get("state")
        s2 = run.status() or {}
        keys.click(*((s2.get("input") or {}).get("buttons") or buttons)["send"])
        s, d = inserted(t, run)
        fg_after = wi.foreground() == t.hwnd
        report.check("pause", e1 >= 0 and e1 == e2 and resumed == "recording" and d.get("text") == s.get("lastResult"),
                     f"elapsed {e1}→{e2} resumed={resumed} ziel={d.get('text')!r:.30}")
        report.check("no-focus-steal", focus_open and fg_click and fg_after,
                     f"overlay={focus_open} klick={fg_click} danach={fg_after}")


def clipboard_checks(run, keys, exe, out, report) -> None:
    sentinel = f"whisperloom-sentinel-{int(time.time())}"
    wi.set_clipboard(sentinel)
    time.sleep(0.2)
    with Target(exe, out, keys) as t, wi.ClipboardWatch() as watch:
        keys.down("ctrl")
        keys.down("win")
        time.sleep(0.6)
        keys.up("win")
        keys.up("ctrl")
        s, d = inserted(t, run)
        time.sleep(0.8)
    clip = plat.read_clipboard("win")
    report.check("clipboard-restore", d.get("text") == s.get("lastResult") and clip == sentinel,
                 f"clip={clip!r:.40} ziel={d.get('text')!r:.30}")
    ok, why = wi.clipboard_private(watch.observations)
    report.check("clipboard-history-excluded", ok, why)


def stage_check(run, keys, exe, out, report) -> None:
    with Target(exe, out, keys) as t:
        before = (run.status() or {}).get("stage")
        keys.combo("ctrl", "alt", "space")
        s = wait_for(lambda: (lambda x: x if ((x.get("input") or {}).get("hint") or {}).get("kind") == "stage" else None)(run.status() or {}), 3) or {}
        hint = (s.get("input") or {}).get("hint") or {}
        fg = wi.foreground() == t.hwnd
        time.sleep(2.0)
        gone = not ((run.status() or {}).get("input") or {}).get("hint")
        d = t.read()
        report.check("stage-toast", before == "off" and s.get("stage") == "polish" and hint.get("text") and fg and gone
                     and d.get("text") == "", f"stage {before}→{s.get('stage')} hint={hint} fokus={fg} weg={gone}")


def type_checks(args, work, report, smoke, fake, keys, exe) -> None:
    fake.post("/_control", {"stt_text": UMLAUT_TEXT})
    run, data, prefix = start_app(args, work, smoke, fake, "input-type", "type")
    out = work / args.id / "testtarget.json"
    try:
        sentinel = f"whisperloom-type-{int(time.time())}"
        wi.set_clipboard(sentinel)
        with Target(exe, out, keys) as t:
            keys.down("ctrl")
            keys.down("win")
            time.sleep(0.6)
            keys.up("win")
            keys.up("ctrl")
            s, d = inserted(t, run)
        result = s.get("lastResult") or ""
        report.check("type-mode", d.get("text") == result and "Grüße" in result and "ß" in result
                     and plat.read_clipboard("win") == sentinel,
                     f"ziel={d.get('text')!r:.50} result={result!r:.50}")
    finally:
        run.stop()
        plat.cleanup_secrets("win", prefix, data)
        fake.post("/_reset", {})
