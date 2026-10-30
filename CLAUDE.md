# WhisperLoom Desktop — Claude-Kontext

> Globale Regeln: `~/.claude/CLAUDE.md` + `~/.claude/rules/`. Hier nur Projekt-Spezifisches. Stand 2026-10-30 (project-init).
> Plan, Phasen, Akzeptanz: `~/.claude/goals/2026-10-30-whisperloom-desktop.md`. Arbeitsordner: `~/.claude/backups/whisperloom-desktop-work/`.

## Zweck
Port des WhisperLoom-Kerns (Android) als Hintergrund-App für Windows und Linux: Diktat → Erkennung → Text-Stufe → Ausgabe.

## Stack
- C# / .NET 10 (`global.json`), Solution `WhisperLoom.slnx`, Paketversionen zentral in `Directory.Packages.props`.
- `src/WhisperLoom.Core` — Fachlogik, OS-frei. `src/WhisperLoom.App` — Avalonia 12 (Exe `WhisperLoom`), OS-Code unter `Platform/Windows|Linux`.
- `tests/WhisperLoom.Core.Tests` (xUnit v3), `tests/WhisperLoom.App.Tests` (Avalonia.Headless.XUnit).
- `tools/` — Python-Werkzeuge (Paritäts-Checks, Fake-API, Smoke), Tests unter `tools/tests/`.
- Paritätsquelle: Android-Repo `../whisperloom-android` (nur lesen).

## Befehle
| Zweck | Befehl |
|---|---|
| Build | `dotnet build WhisperLoom.slnx -c Release` |
| Tests | `dotnet test WhisperLoom.slnx` |
| Tool-Tests | `py -3 -X utf8 -m unittest discover -s tools/tests` |
| Test-Parität | `py -3 -X utf8 tools/test_parity.py --android ../whisperloom-android --mapping docs/test-mapping-contracts.md` |
| Start (Dev) | `dotnet run --project src/WhisperLoom.App` |
| Smoke | `py -3 -X utf8 tools/smoke.py --platform win --app <exe>` (ab Phase 2) |

## Regeln
- Core bleibt OS-frei: keine `using Avalonia|System.Windows|Microsoft.Win32` in `src/WhisperLoom.Core`.
- Verträge (`src/WhisperLoom.Core/Contracts/`, `docs/contracts.md`) nur additiv ändern.
- Keine Keys in Code, Tests, Fixtures, Logs, Commits. Fake-Keys: `test-key-not-real-<n>`. Tests nur gegen lokale Fakes.
- Logs nur Metadaten (Dauer, Anbieter, Status, Fehlerklasse), nie Diktattext, Keys oder Header. Audio nie auf die Platte.
- Prompts aus `RefinePrompt.kt` nur portieren, nicht verbessern; Abweichungen nur über `docs/parity-exceptions.md`.
- Dateien ≤ ~400 Zeilen, ein Zweck je Datei. Warnungen sind Fehler (`Directory.Build.props`).
- Bibliotheks-Doku per Context7 bzw. offizieller Doku, Quelle als Kommentar `// Doku: <quelle> (abgerufen <datum>)`.

## Repo-Etikette
- Default-Branch `main`, öffentlich (MIT). Je Phase ein Branch + PR; Merge nur nach Christofs OK.

## Projekt-Gotchas
- Christof arbeitet am selben PC: Smokes mit echter Zwischenablage/SendInput (`ui`, `input`) nur mit Lock `W/ui-smoke.lock`, sonst `--output-file`, eigener `--data-dir`, Credential-Präfix `WhisperLoom-Test-<id>:`.
- WSLg-Zwischenablage ist mit Windows gekoppelt → zählt als echte Zwischenablage.
- Prozesse (App, Fake-API, whisper-server) nur per gespeicherter PID beenden.

## Doku
- `docs/contracts.md` (Schnittstellen), `docs/test-mapping-*.md` (Kotlin→C#-Testzuordnung), README.
