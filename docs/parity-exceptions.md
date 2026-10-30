# Paritäts-Ausnahmen

Bewusste Abweichungen des Desktops von Android, je mit Begründung. Die Paritäts-Skripte lesen ihren Abschnitt und
schlagen fehl, wenn eine Ausnahme fehlt, keine Begründung hat oder keinen echten Unterschied mehr trifft.

## Anbieter

Geprüft von `tools/check_provider_parity.py` (`ProviderCatalog.kt` ↔ `src/WhisperLoom.Core/Api/providers.json`).
Schlüssel: `<id>.<feld>`, `<id>.<sttModels|llmModels>[<modell>].<feld>`, `<id>.<sttModels|llmModels>.order`, `<id>`,
`providers.order`, `catalogDate`.

| Unterschied | Begründung |
|---|---|
