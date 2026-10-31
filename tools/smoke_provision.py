"""Suite provision (WLD-P5a): IT-Provisioning gegen eine gebaute App, ohne echte Eingabe und ohne Zwischenablage.

- provision-import: `provision <datei>` bei laufender Instanz → Keys in der Ablage (Credential Manager unter
  "WhisperLoom-Test-<ID>:*", Linux Secret Service bzw. secrets.json mit 600), nicht in settings.json; die
  Eingabedatei ist unverändert (SHA256 vorher/nachher); die Instanz meldet die Keys nach dem reload.
- policy-lock: Richtlinie aktiv (status), gesperrte Stufe lässt sich per CLI nicht ändern, ein Import des Nutzers
  lässt gesperrte Felder stehen. Das Ausgrauen im Fenster prüft der Headless-Test SyncUiTest.
- export-no-secrets: `export` enthält Einstellungen, aber weder secrets noch policy noch einen Key-Wert.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

FIXTURE = Path(__file__).resolve().parent.parent / "tests" / "fixtures" / "settings-v1-provisioning.json"
KEY_MARK = "test-key-not-real"
LOCKED_EXPECTED = ("stt.providerId", "refine.stage", "secrets.stt", "secrets.llm")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def fixture_keys() -> dict[str, str]:
    """Anbieter → Fake-Key aus der Fixture (Werte bleiben im Smoke, nur Vergleiche)."""
    return json.loads(FIXTURE.read_text(encoding="utf-8"))["secrets"]


def policy_problems(status: dict) -> list[str]:
    """Was am status --json nach dem Provisioning der Fixture nicht stimmt."""
    p = status.get("policy") or {}
    problems = []
    if p.get("active") is not True:
        problems.append("policy nicht aktiv")
    missing = [x for x in LOCKED_EXPECTED if x not in (p.get("locked") or [])]
    if missing:
        problems.append("nicht gesperrt: " + ",".join(missing))
    if p.get("historyEnabled") is not False or (status.get("history") or {}).get("enabled") is not False:
        problems.append("Verlauf nicht per Richtlinie aus")
    if p.get("updatesEnabled") is not False:
        problems.append("updatesEnabled nicht false")
    if p.get("allowedProviders") != ["groq", "openai"]:
        problems.append(f"allowedProviders={p.get('allowedProviders')}")
    return problems


def export_problems(text: str, provider: str) -> list[str]:
    try:
        data = json.loads(text)
    except ValueError:
        return ["Export ist kein JSON"]
    problems = []
    for section in ("secrets", "policy"):
        if section in data:
            problems.append(f"Abschnitt {section} im Export")
    if KEY_MARK in text:
        problems.append("Key-Wert im Export")
    if (data.get("stt") or {}).get("providerId") != provider:
        problems.append("stt.providerId fehlt")
    if data.get("schemaVersion") != 1:
        problems.append("schemaVersion fehlt")
    return problems


def run(args, work: Path, report, smoke) -> None:
    """smoke = Modul tools/smoke.py (AppRun, fresh_dir); plat = smoke_platform."""
    import smoke_platform as plat

    data = args.data_dir or work / args.id / "smoke-data-provision"
    smoke.fresh_dir(data)
    source = work / args.id / "provision-input.json"
    source.write_bytes(FIXTURE.read_bytes())
    before = sha256(source)
    prefix = f"WhisperLoom-Test-{args.id}:"
    app = smoke.AppRun(args.app, data, prefix, [])
    try:
        app.start()
        r = app.cli("provision", str(source))
        s = app.status() or {}
        keys = fixture_keys()
        stored = {name: plat.read_secret(args.platform, prefix, name, data, s.get("secretStore")) for name in ("stt", "llm")}
        settings = (data / "settings.json").read_text(encoding="utf-8") if (data / "settings.json").exists() else ""
        output = r.stdout + r.stderr
        reasons = []
        if r.returncode != 0 or "ok provision" not in r.stdout:
            reasons.append(f"rc={r.returncode} {r.stderr.strip()[:200]}")
        if stored["stt"][0] != keys["groq"] or stored["llm"][0] != keys["openai"]:
            reasons.append(f"Keys nicht in der Ablage ({stored['stt'][1]})")
        if KEY_MARK in settings or not settings:
            reasons.append("settings.json fehlt oder enthält einen Key")
        if KEY_MARK in output or KEY_MARK in json.dumps(s):
            reasons.append("Key in Ausgabe oder status")
        if sha256(source) != before:
            reasons.append("Eingabedatei verändert")
        if (s.get("keys") or {}).get("stt") is not True or (s.get("keys") or {}).get("llm") is not True:
            reasons.append("Instanz meldet die Keys nicht")
        report.check("provision-import", not reasons, "; ".join(reasons) or stored["stt"][1])

        problems = policy_problems(s)
        stage = app.cli("stage", "next", timeout=20)
        after = app.status() or {}
        if stage.returncode == 0 or "stage-locked" not in stage.stdout or after.get("stage") != s.get("stage"):
            problems.append(f"Stufe änderbar: rc={stage.returncode} {stage.stdout.strip()} stage={after.get('stage')}")
        user = work / args.id / "provision-user.json"
        user.write_text(json.dumps({"stt": {"providerId": "openai", "vocabulary": "Kubernetes"}}), encoding="utf-8")
        imp = app.cli("import", str(user))
        current = json.loads((data / "settings.json").read_text(encoding="utf-8"))
        if imp.returncode != 0 or "locked stt.providerId" not in imp.stdout:
            problems.append(f"import: rc={imp.returncode} {imp.stdout.strip()[:200]} {imp.stderr.strip()[:200]}")
        if current["stt"]["providerId"] != "groq" or current["stt"]["vocabulary"] != "Kubernetes":
            problems.append(f"nach Import stt={current['stt']['providerId']}/{current['stt']['vocabulary']!r}")
        report.check("policy-lock", not problems, "; ".join(problems))

        target = work / args.id / "provision-export.json"
        target.unlink(missing_ok=True)
        ex = app.cli("export", str(target))
        text = target.read_text(encoding="utf-8") if target.exists() else ""
        problems = ([] if ex.returncode == 0 else [f"rc={ex.returncode}"]) + export_problems(text, "groq")
        report.check("export-no-secrets", not problems, "; ".join(problems))
    finally:
        app.stop()
        plat.cleanup_secrets(args.platform, prefix, data)
