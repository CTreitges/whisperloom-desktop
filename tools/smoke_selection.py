"""Suite selection (tools/smoke.py --suite selection, W2-SEL; nur Windows, nur mit UI-Lock).

Echte Eingaben per SendInput gegen tests/WhisperLoom.TestTarget (Textfeld mit markiertem Text), App mit --hotkeys gegen
tools/fake_api.py, Stufe "off", Starter Kombi, Knopf und Strg+Rechtsklick an. Eigener Datenordner, Key-Präfix
WhisperLoom-Test-<ID>:; die Zwischenablage des Nutzers (Text) wird am Ende wiederhergestellt. Jede Taste prüft vorher, dass
das TestTarget vorn ist (Keyboard.guard), jeder Klick, dass unter dem Punkt das TestTarget bzw. ein Fenster der App liegt.

Prüfungen:
- selection-hotkey: Strg+Alt+Umschalt+S → Stufen-Wähler (Glätten, Verschönern, Zusammenfassen, Nach Anweisung …) ohne
  Fokusklau → ↓ (Stufe 2 = Verschönern) → Enter → Markierung durch die Antwort der Fake-API ersetzt, Text-Anfrage mit der
  Markierung, Verlaufseintrag mit Quelle "selection" und Stufe beautify.
- selection-ctrl-right-click: Strg+Rechtsklick auf die Markierung → Wähler an der Maus → 1 → ersetzt.
- selection-button: Text mit der Maus markieren (Ziehen) → schwebender Knopf → Klick → Wähler → Enter → ersetzt.
- selection-clipboard: nach jedem Weg steht wieder der Wächter-Text in der Zwischenablage.
- selection-front: das TestTarget war vor jeder Taste und jedem Klick vorn und blieb es während des Wählers.
"""
from __future__ import annotations

import json
import time
from pathlib import Path

import smoke_full
import smoke_input as si
import smoke_platform as plat
import smoke_winput as wi

TEXT = "das ist ein test fuer die markierung"
NAMES = ["selection-hotkey", "selection-ctrl-right-click", "selection-button", "selection-clipboard", "selection-front"]
STAGES = ["polish", "beautify", "summarize", "instruction"]


def selection_settings(fake) -> dict:
    settings = smoke_full.fake_settings(fake, via_proxy=False)
    settings["language"] = "de"
    settings["refine"] = {"stage": "off", "promptEnabled": False}
    settings["desktop"] = {"insertMode": "paste"}
    settings["selection"] = {"hotkeyCombo": "Ctrl+Alt+Shift+S", "floatingButton": True, "ctrlRightClick": True}
    return settings


def chats(fake) -> list[dict]:
    return [r for r in fake.requests() if r["path"].endswith("/chat/completions")]


def selection_status(run) -> dict:
    return ((run.status() or {}).get("input") or {}).get("selection") or {}


def picker_open(run, highlighted: str | None = None) -> dict:
    """Wartet auf den offenen Wähler (optional mit dieser markierten Stufe)."""
    def probe():
        s = selection_status(run)
        return s if s.get("pickerOpen") and (highlighted is None or s.get("highlighted") == highlighted) else None
    return si.wait_for(probe, 5, 0.15) or {}


def replaced_ok(fake, run, t, llm_before: int, want: str) -> tuple[bool, dict, str]:
    """Markierung ersetzt: Text im TestTarget = Antwort, laut App dorthin eingefügt, genau eine Text-Anfrage mit der Markierung."""
    s = si.idle(run)
    d = si.wait_for(lambda: (lambda x: x if x.get("text") == want else None)(t.read()), 5) or t.read()
    went, where = si.went_to(s, t.hwnd)
    requests = chats(fake)[llm_before:]
    sent = len(requests) == 1 and TEXT in str(requests[0].get("user") or "")
    latest = (s.get("history") or {}).get("latest") or {}
    ok = d.get("text") == want and went and sent and latest.get("source") == "selection"
    return ok, latest, f"ziel={d.get('text')!r:.40} {where} anfragen={len(requests)}/{sent} quelle={latest.get('source')}"


def run_suite(args, work: Path, report, smoke) -> None:
    wi.dpi_aware()
    keys = wi.Keyboard()
    exe = si.target_exe(args, work)
    saved = plat.read_clipboard("win")
    try:
        with smoke_full.FakeApi() as fake:
            si.guarded(report, NAMES, lambda: checks(args, work, report, smoke, fake, keys, exe))
    finally:
        keys.release_all()
        if saved is not None:
            wi.set_clipboard(saved)


def start_app(args, work: Path, smoke, fake):
    data = work / args.id / "smoke-data-selection"
    smoke.fresh_dir(data)
    (data / "settings.json").write_text(json.dumps(selection_settings(fake)), encoding="utf-8")
    prefix = f"WhisperLoom-Test-{args.id}:"
    run = smoke.AppRun(args.app, data, prefix, ["--hotkeys"])
    run.start()
    return run, data, prefix


def checks(args, work, report, smoke, fake, keys, exe) -> None:
    run, data, prefix = start_app(args, work, smoke, fake)
    out = work / args.id / "testtarget-selection.json"
    clips: list[bool] = []
    fronts: list[bool] = []
    try:
        inp = (run.status() or {}).get("input") or {}
        if not (inp.get("hook") and inp.get("mouseHook")):
            report.check("hooks-on", False, f"hook={inp.get('hook')} mouseHook={inp.get('mouseHook')}")
            return
        hotkey_check(run, keys, exe, out, report, fake, clips, fronts)
        ctrl_right_click_check(run, keys, exe, out, report, fake, clips, fronts)
        button_check(run, keys, exe, out, report, fake, clips, fronts)
        report.check("selection-clipboard", len(clips) == 3 and all(clips), f"wiederhergestellt je Weg: {clips}")
        report.check("selection-front", len(fronts) == 3 and all(fronts), f"TestTarget vorn je Weg: {fronts}")
    finally:
        run.stop()
        plat.cleanup_secrets("win", prefix, data)


def sentinel(name: str) -> str:
    value = f"whisperloom-selection-{name}-{int(time.time())}"
    wi.set_clipboard(value)
    time.sleep(0.2)
    return value


def hotkey_check(run, keys, exe, out, report, fake, clips, fronts) -> None:
    want = "Ersetzt per Kombi."
    fake.post("/_control", {"llm_text": want, "record_text": True})
    guard = sentinel("hotkey")
    llm = len(chats(fake))
    with si.Target(exe, out, keys, ["--text", TEXT]) as t:
        keys.combo("ctrl", "a")
        time.sleep(0.2)
        keys.combo("ctrl", "alt", "shift", "s")
        opened = picker_open(run, "polish")
        front_open = wi.foreground() == t.hwnd  # Wähler nimmt dem Ziel den Fokus nicht
        keys.combo("down")
        moved = picker_open(run, "beautify")
        keys.combo("enter")
        ok, latest, why = replaced_ok(fake, run, t, llm, want)
        fronts.append(front_open and wi.foreground() == t.hwnd)
    clips.append(plat.read_clipboard("win") == guard)
    report.check("selection-hotkey",
                 ok and opened.get("options") == STAGES and bool(moved) and latest.get("stage") == "beautify" and front_open,
                 f"optionen={opened.get('options')} markiert={moved.get('highlighted')} stufe={latest.get('stage')} vorn={front_open} {why}")
    fake.post("/_reset", {})


def ctrl_right_click_check(run, keys, exe, out, report, fake, clips, fronts) -> None:
    want = "Ersetzt per Rechtsklick."
    fake.post("/_control", {"llm_text": want, "record_text": True})
    guard = sentinel("ctrl")
    llm = len(chats(fake))
    with si.Target(exe, out, keys, ["--text", TEXT]) as t:
        keys.combo("ctrl", "a")
        time.sleep(0.2)
        left, top, right, _ = t.read().get("box") or wi.window_rect(t.hwnd)
        x, y = left + 40, top + 16
        keys.down("ctrl")
        try:
            keys.right_click(x, y, allowed=lambda root: root == t.hwnd)
        finally:
            keys.up("ctrl")
        opened = picker_open(run, "polish")
        front_open = wi.foreground() == t.hwnd
        keys.combo("1")
        ok, latest, why = replaced_ok(fake, run, t, llm, want)
        fronts.append(front_open and wi.foreground() == t.hwnd)
    clips.append(plat.read_clipboard("win") == guard)
    rect = opened.get("pickerRect") or []
    near = len(rect) == 4 and abs(rect[0] - x) < 400 and abs(rect[1] - y) < 400
    report.check("selection-ctrl-right-click", ok and bool(opened) and near and latest.get("stage") == "polish",
                 f"wähler={bool(opened)} lage={rect} maus=({x},{y}) stufe={latest.get('stage')} {why}")
    fake.post("/_reset", {})


def button_check(run, keys, exe, out, report, fake, clips, fronts) -> None:
    want = "Ersetzt per Knopf."
    fake.post("/_control", {"llm_text": want, "record_text": True})
    guard = sentinel("button")
    llm = len(chats(fake))

    def app_window(root: int) -> bool:
        return root != 0 and wi.window_pid(root) == run.proc.pid

    with si.Target(exe, out, keys, ["--text", TEXT]) as t:
        left, top, right, _ = t.read().get("box") or wi.window_rect(t.hwnd)
        keys.click(right - 20, top + 60, allowed=lambda root: root == t.hwnd)  # Markierung aufheben
        time.sleep(0.2)
        keys.drag(left + 4, top + 16, right - 10, top + 16, allowed=lambda root: root == t.hwnd)
        shown = si.wait_for(lambda: (lambda s: s if s.get("buttonVisible") and s.get("button") else None)(selection_status(run)), 5) or {}
        if not shown:
            report.check("selection-button", False, f"kein Knopf nach der Markierung: {selection_status(run)}")
            fronts.append(wi.foreground() == t.hwnd)
            clips.append(plat.read_clipboard("win") == guard)
            return
        keys.click(*shown["button"], allowed=app_window)
        opened = picker_open(run)
        front_open = wi.foreground() == t.hwnd
        keys.combo("enter")
        ok, latest, why = replaced_ok(fake, run, t, llm, want)
        fronts.append(front_open and wi.foreground() == t.hwnd)
    clips.append(plat.read_clipboard("win") == guard)
    report.check("selection-button", ok and bool(opened) and front_open, f"knopf={shown.get('button')} wähler={bool(opened)} vorn={front_open} {why}")
    fake.post("/_reset", {})
