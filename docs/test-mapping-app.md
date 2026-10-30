# Test-Mapping: App (Phase 2 und WLD-INT)

Die App-Tests (`tests/WhisperLoom.App.Tests`) haben kein Kotlin-Gegenstück; sie prüfen Hülle, Fenster, Key-Ablage und
die Verdrahtung der echten Pipeline (`AppPipelineTest`). Diese Datei begründet die Fälle, die je nach Umgebung
übersprungen werden — sonst gibt es keine Skips.

## Begründete Skips

| C#-Fall | Wann übersprungen | Grund |
|---|---|---|
| `ScreenshotTest.Render_stage_and_settings` | ohne Umgebungsvariable `WLD_SCREENSHOT_DIR` | erzeugt nur Screenshots für die Doku (A2.5), prüft sonst nichts Eigenes |
| `SecretStoreTest.File_store_tightens_loosened_rights_and_replaces_a_stale_tmp` | unter Windows | Unix-Dateirechte (600) gibt es nur unter Linux; läuft in der CI auf `ubuntu-latest` |
| `SecretStoreTest.Windows_credential_manager_round_trip` | unter Linux | Credential Manager gibt es nur unter Windows; läuft in der CI auf `windows-latest` |

Je Betriebssystem bleiben damit zwei Fälle übersprungen (Windows: Screenshot, Unix-Rechte; Linux: Screenshot,
Credential Manager).
