# Paritäts-Ausnahmen

Bewusste Abweichungen des Desktop-Ports von Android, je Prüfskript ein Abschnitt. Jede Zeile braucht eine
Begründung; ungenutzte Zeilen sind ein Fehler.

## Prompts

Geprüft von `tools/check_prompt_parity.py`. Fragment exakt wie in der Ausgabe des Skripts; Seite `android` = nur in
`RefinePrompt.kt`, `desktop` = nur in `src/WhisperLoom.Core/Prompts/`.

| Fragment | Seite | Begründung |
|---|---|---|

## Anbieter

Geprüft von `tools/check_provider_parity.py` (`ProviderCatalog.kt` ↔ `src/WhisperLoom.Core/Api/providers.json`).
Schlüssel: `<id>.<feld>`, `<id>.<sttModels|llmModels>[<modell>].<feld>`, `<id>.<sttModels|llmModels>.order`, `<id>`,
`providers.order`, `catalogDate`.

| Unterschied | Begründung |
|---|---|
