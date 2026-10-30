# Test-Mapping: Verträge (Phase 0)

Zuordnung Kotlin-Testklasse (Android-Repo `app/src/test*`) → C#-Testklasse, geprüft mit `tools/test_parity.py`.
Status `portiert` verlangt mindestens so viele C#-Fälle wie Kotlin-`@Test`; `ausgenommen` verlangt eine Begründung.
Weitere Mapping-Dateien (Phase 1): `test-mapping-text.md`, `test-mapping-api.md`. Vollständigkeit (A1.2) prüft
`--require-file docs/test-parity-required.txt`. Mehrere C#-Klassen je Zeile kommagetrennt; `partial`-Teile zählen zusammen.

| Kotlin-Klasse | C#-Klasse | Status | Begründung |
|---|---|---|---|
| RefinementTest | WhisperLoom.Core.Tests.Contracts.RefinementTest | portiert | |
