"""Teil der Suite "input" (W3-NEXT): Diktat-Taste während der KI-Verbesserung.

Die Fake-API verzögert jede Textverbesserung (llm_delay). Geprüft wird mit echtem SendInput gegen das TestTarget:
- next-during-refine: Strg+Leertaste während der Verbesserung startet Aufnahme 2; Diktat 1 kommt verbessert im Testfenster
  an, während Aufnahme 2 noch läuft; Aufnahme 2 normal gesendet → am Ende beide verbessert.
- next-double-press-raw: Doppeldruck (< 400 ms) während der Verbesserung → Diktat 1 als Rohtext, keine Aufnahme 2, nur
  ein neuer Verlaufseintrag.
"""
from __future__ import annotations

import time

import smoke_input as si
import smoke_platform as plat

NEXT_REFINE_DELAY = 4.0
REFINED_MARK = "fake-llm"  # Antwort der Fake-API: "[fake-llm] <nutzer>" (Groß-/Kleinschreibung je nach Regeln)


def refined_count(text: str) -> int:
    return (text or "").lower().count(REFINED_MARK)


def next_verdict(during: str | None, still: str | None, first: str, final: str) -> tuple[bool, str]:
    """Aufnahme 2 lief schon und noch, als Diktat 1 verbessert ankam; am Ende stehen beide verbessert im Ziel."""
    ok = during == "recording" and still == "recording" and refined_count(first) == 1 and refined_count(final) == 2
    return ok, f"zustand={during}/{still} nach1={first!r:.50} ende={final!r:.80}"


def double_verdict(status: dict, text: str, history_before: int | None) -> tuple[bool, str]:
    """Rohtext im Ziel, keine KI, keine laufende Aufnahme, kein Overlay, genau ein neuer Verlaufseintrag."""
    count = (status.get("history") or {}).get("count")
    overlay = (status.get("input") or {}).get("overlay")
    ok = (bool(text) and refined_count(text) == 0 and status.get("state") == "idle" and not overlay
          and history_before is not None and count == history_before + 1)
    return ok, f"ziel={text!r:.50} zustand={status.get('state')} overlay={overlay} verlauf {history_before}→{count}"


def state_is(run, want: str, seconds: float) -> str | None:
    s = si.wait_for(lambda: (lambda x: x if x.get("state") == want else None)(run.status() or {}), seconds) or {}
    return s.get("state")


def record_and_send(run, keys) -> bool:
    """Diktat per Umschalten aufnehmen und senden, bis die (verzögerte) Verbesserung läuft."""
    keys.combo("ctrl", "space")
    si.overlay_open(run)
    time.sleep(0.6)
    keys.combo("ctrl", "space")
    processing = state_is(run, "processing", 3) == "processing"
    time.sleep(1.0)  # Erkennung der Fake-API ist sofort da, die Verbesserung wartet llm_delay
    return processing


def next_checks(args, work, report, smoke, fake, keys, exe) -> None:
    fake.post("/_control", {"llm_delay": NEXT_REFINE_DELAY})
    run, data, prefix = si.start_app(args, work, smoke, fake, "input-next", "paste", stage="polish")
    out = work / args.id / "testtarget.json"
    try:
        with si.Target(exe, out, keys) as t:
            processing = record_and_send(run, keys)
            keys.combo("ctrl", "space")  # während der Verbesserung: nächste Aufnahme
            during = state_is(run, "recording", 3)
            first = si.wait_for(lambda: (lambda x: x if refined_count(x) else None)(t.read().get("text") or ""),
                                NEXT_REFINE_DELAY + 10) or ""
            still = (run.status() or {}).get("state")
            time.sleep(0.6)
            keys.combo("ctrl", "space")  # Aufnahme 2 normal senden
            final = si.wait_for(lambda: (lambda x: x if refined_count(x) == 2 else None)(t.read().get("text") or ""),
                                NEXT_REFINE_DELAY + 10) or (t.read().get("text") or "")
            si.idle(run)
            ok, why = next_verdict(during, still, first, final)
            report.check("next-during-refine", ok and processing, f"verarbeitet={processing} {why}")

        with si.Target(exe, out, keys) as t:
            before = ((run.status() or {}).get("history") or {}).get("count")
            processing = record_and_send(run, keys)
            keys.combo("ctrl", "space")
            time.sleep(0.12)
            keys.combo("ctrl", "space")  # Doppeldruck
            s = si.idle(run)
            text = si.wait_for(lambda: t.read().get("text"), 5) or ""
            time.sleep(0.5)
            ok, why = double_verdict(run.status() or s, text, before)
            report.check("next-double-press-raw", ok and processing, f"verarbeitet={processing} {why}")
    finally:
        run.stop()
        plat.cleanup_secrets("win", prefix, data)
        fake.post("/_reset", {})
