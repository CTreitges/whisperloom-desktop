"""Teilprüfungen der Suite "input" für W2-OUT (nur Windows, nur mit UI-Lock; aufgerufen aus smoke_input.paste_checks).

- focus-textfield: TestTarget-Textfeld → eingefügt, Fokus-Klasse Editable
- focus-password: TestTarget-Passwortfeld → nichts eingefügt, nichts kopiert, kein Verlaufseintrag, Hinweis
- focus-button: TestTarget-Knopf mit Fokus → Zwischenablage enthält den Text, Hinweis erschienen
- ctrl-enter-no-enter: Strg+Enter im Overlay sendet nicht (kein Einfügen, kein Enter von der App)
- history-copy-20: 20× „Kopieren“ aus dem Verlauf (CLI history-copy = Weg des Verlaufsfensters) → Zwischenablage gleich Erwartung
- leading-space: TestTarget mit Text „Wort“, Schreibmarke ans Ende → Diktat wird mit Leerzeichen davor angefügt (UIA TextPattern)
- focus-measure: Fokus-Klasse (nur lesen) an Notepad und Edge/Chrome (Textarea, Passwort, Knopf, Seite ohne Feld) – Messung für
  den Fall „unbekannt“; Ergebnis steht im Bericht, die Prüfung scheitert nur, wenn die Abfrage selbst scheitert.

Fremde Programme (Notepad, Browser) startet die Prüfung selbst mit eigenem Profil und beendet nur deren gespeicherte PID.
Läuft schon ein Notepad des Nutzers, wird Notepad übersprungen (Windows 11 öffnet dort sonst einen Tab im fremden Fenster).
"""
from __future__ import annotations

import os
import shutil
import subprocess
import time
import urllib.parse
from pathlib import Path

import smoke_input as si
import smoke_platform as plat
import smoke_winput as wi


def hold(keys) -> None:
    keys.down("ctrl")
    keys.down("win")
    time.sleep(0.6)
    keys.up("win")
    keys.up("ctrl")


def hint_seen(run, seconds: float = 3) -> dict:
    return si.wait_for(lambda: (lambda s: s if ((s.get("input") or {}).get("hint") or {}).get("kind") == "toast" else None)(run.status() or {}),
                       seconds) or {}


def focus_checks(run, keys, exe, out: Path, report) -> None:
    with si.Target(exe, out, keys) as t:
        hold(keys)
        s, d = si.inserted(t, run)
        focus = (s.get("input") or {}).get("lastFocus")
        report.check("focus-textfield", d["_landed"] and focus == "Editable", f"{d['_where']} fokus={focus}")

    sentinel = f"whisperloom-pw-{int(time.time())}"
    wi.set_clipboard(sentinel)
    before = ((run.status() or {}).get("history") or {}).get("count")
    with si.Target(exe, out, keys, ["--mode", "password"]) as t:
        hold(keys)
        hint = hint_seen(run)
        s = si.idle(run)
        time.sleep(0.6)
        d = t.read()
    clip = plat.read_clipboard("win")
    last = (s.get("input") or {}).get("lastInsert")
    report.check("focus-password", d.get("text") == "" and clip == sentinel and (s.get("history") or {}).get("count") == before
                 and last == "PasswordField" and bool(hint) and not s.get("lastResult"),
                 f"ziel={d.get('text')!r} clip-unverändert={clip == sentinel} verlauf {before}→{(s.get('history') or {}).get('count')} "
                 f"insert={last} hinweis={bool(hint)}")

    wi.set_clipboard(f"whisperloom-btn-{int(time.time())}")
    with si.Target(exe, out, keys, ["--mode", "button"]) as t:
        hold(keys)
        hint = hint_seen(run)
        s = si.idle(run)
        time.sleep(0.4)
        d = t.read()
    clip = plat.read_clipboard("win")
    want = s.get("lastResult") or ""
    last = (s.get("input") or {}).get("lastInsert")
    report.check("focus-button", bool(want) and clip == want and d.get("text") == "" and last == "NoTextField" and bool(hint)
                 and s.get("lastHint") in ("copied", "none"),
                 f"clip==ergebnis={clip == want} ziel={d.get('text')!r} insert={last} hinweis={((hint.get('input') or {}).get('hint') or {}).get('text')!r}")


def leading_space_check(run, keys, exe, out: Path, report, fake=None) -> None:
    """Leerzeichen davor (Android commitDictation): direkt hinter „Wort“ diktiert → „Wort <Diktat>“. Das Diktat muss mit einem
    Buchstaben beginnen (sonst gilt die Regel nicht) – die Fake-KI antwortet dafür fest mit „Hallo Welt.“."""
    if fake is not None:
        fake.post("/_control", {"llm_text": "Hallo Welt."})
    try:
        _leading_space(run, keys, exe, out, report)
    finally:
        if fake is not None:
            fake.post("/_control", {"llm_text": None})


def _leading_space(run, keys, exe, out: Path, report) -> None:
    with si.Target(exe, out, keys, ["--text", "Wort"]) as t:
        keys.combo("end")
        time.sleep(0.2)
        hold(keys)
        s = si.idle(run)
        want = "Wort " + (s.get("lastResult") or "")
        d = si.wait_for(lambda: (lambda x: x if x.get("text") == want else None)(t.read()), 5) or t.read()
    report.check("leading-space", bool(s.get("lastResult")) and d.get("text") == want, f"ziel={d.get('text')!r:.50} erwartet={want!r:.50}")


def ctrl_enter_check(run, keys, exe, out: Path, report) -> None:
    """Strg+Enter im Overlay: kein Senden mehr (W2-OUT). Die Taste geht ans Ziel; danach verwerfen."""
    with si.Target(exe, out, keys) as t:
        before = (run.status() or {}).get("lastResult")
        keys.combo("ctrl", "space")
        si.overlay_open(run)
        time.sleep(0.4)
        keys.combo("ctrl", "enter")
        time.sleep(1.0)
        s = run.status() or {}
        still_open = bool((s.get("input") or {}).get("overlay")) and s.get("state") == "recording"
        d = t.read()
        keys.combo("back")
        s2 = si.idle(run)
        report.check("ctrl-enter-no-enter", still_open and d.get("text") == "" and s2.get("lastResult") == before,
                     f"overlay-offen={still_open} ziel={d.get('text')!r} enter-im-ziel={d.get('enter')} (Taste des Smokes, nicht der App)")


def history_copy_check(run, report, rounds: int = 20) -> None:
    latest = (((run.status() or {}).get("history") or {}).get("latest") or {}).get("text") or ""
    fails = []
    for i in range(rounds):
        wi.set_clipboard(f"whisperloom-copy-{i}-{int(time.time())}")
        r = run.cli("history-copy", "0")
        clip = plat.read_clipboard("win")
        if r.returncode != 0 or clip != latest:
            fails.append(f"#{i} rc={r.returncode} {r.stdout.strip()[:30]} clip-ok={clip == latest}")
    copies = (run.status() or {}).get("copies")
    report.check(f"history-copy-{rounds}", bool(latest) and not fails, f"fehler={fails[:3]} copies={copies}")


def _browser() -> list[tuple[str, str]]:
    found = []
    for name, paths in (("edge", [r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"]),
                        ("chrome", [r"C:\Program Files\Google\Chrome\Application\chrome.exe",
                                    os.path.expandvars(r"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe")])):
        exe = next((p for p in paths if Path(p).exists()), None)
        if exe:
            found.append((name, exe))
    return found


def _window_of(pids: set[int], seconds: float = 15) -> int:
    def probe():
        for hwnd, pid, title in plat.win_windows():
            if pid in pids and title:
                return hwnd
        return 0
    return si.wait_for(probe, seconds, 0.2) or 0


def _children(pid: int) -> set[int]:
    """PID und alle Nachfahren (Browser zeichnen Fenster im Hauptprozess, zur Sicherheit alle)."""
    r = subprocess.run(["powershell", "-NoProfile", "-Command",
                        f"Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId | ConvertTo-Json -Compress"],
                       capture_output=True, text=True, timeout=30)
    import json
    rows = json.loads(r.stdout or "[]")
    tree, todo = {pid}, [pid]
    while todo:
        p = todo.pop()
        for row in rows:
            if row.get("ParentProcessId") == p and row["ProcessId"] not in tree:
                tree.add(row["ProcessId"])
                todo.append(row["ProcessId"])
    return tree


def _probe(run, hwnd: int) -> str:
    wi.force_foreground(hwnd)
    time.sleep(1.0)
    if wi.foreground() != hwnd:
        return "nicht-vorn"
    # Zweimal: Chromium baut seinen Barrierefreiheits-Baum erst nach der ersten UIA-Anfrage auf.
    first = run.cli("focus-probe").stdout.strip()
    time.sleep(4.0)  # ≈ Aufnahmedauer: die Vorab-Abfrage beim Diktatstart hat dem Browser so viel Zeit gegeben
    second = run.cli("focus-probe").stdout.strip()
    if not first or not second:
        return "?"
    return first.splitlines()[-1].replace(" ", ":") + "/" + second.splitlines()[-1].replace(" ", ":")


PAGES = {
    "textarea": "<textarea autofocus rows=5 cols=40></textarea>",
    "password": "<input type=password autofocus>",
    "button": "<button autofocus>OK</button>",
    "page": "<p>Nur Text, kein Feld.</p>",
}


def focus_measure(run, work: Path, report) -> None:
    results = []
    ok = True
    # Notepad: nur, wenn der Nutzer keins offen hat (sonst öffnet Windows 11 einen Tab im fremden Fenster).
    if any(n.lower().startswith("notepad") for n in _process_names()):
        results.append("notepad=übersprungen (Notepad des Nutzers offen)")
    else:
        before = {pid for _, pid, _ in plat.win_windows()}
        proc = subprocess.Popen(["notepad.exe"])
        hwnd = si.wait_for(lambda: next((h for h, pid, title in plat.win_windows()
                                         if pid not in before and "notepad" in wi.process_name(h).lower()), 0), 10, 0.2)
        if hwnd:
            results.append(f"notepad={_probe(run, hwnd)}")
            pid = wi.window_pid(hwnd)
            subprocess.run(["taskkill", "/PID", str(pid), "/F"], capture_output=True)  # nur das eben gestartete Notepad
        else:
            results.append("notepad=kein-fenster")
        if proc.poll() is None:
            proc.kill()
    for name, exe in _browser():
        profile = work / f"{name}-profile"
        shutil.rmtree(profile, ignore_errors=True)
        for page, html in PAGES.items():
            url = "data:text/html;charset=utf-8," + urllib.parse.quote(f"<title>WL {page}</title>{html}")
            proc = subprocess.Popen([exe, f"--user-data-dir={profile}", "--no-first-run", "--no-default-browser-check",
                                     "--new-window", url])
            try:
                hwnd = _window_of(_children(proc.pid))
                results.append(f"{name}-{page}={_probe(run, hwnd) if hwnd else 'kein-fenster'}")
            finally:
                subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"], capture_output=True)  # nur der eigene Browser
                proc.wait(timeout=20)
        shutil.rmtree(profile, ignore_errors=True)
    if any("?" in r for r in results):
        ok = False
    print("SMOKE-INFO focus-measure " + " ".join(results), flush=True)
    report.check("focus-measure", ok, " ".join(results))


def _process_names() -> list[str]:
    return list({wi.process_name(h) for h, _, _ in plat.win_windows()})
