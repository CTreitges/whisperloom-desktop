# Test-Mapping: Lokal und Netz-Vorprüfung (WLD-P4b)

Zuordnung Kotlin-Testklasse (Android `main`, `app/src/test*`) → C#-Testklasse, geprüft mit `tools/test_parity.py`.

| Kotlin-Klasse | C#-Klasse | Status | Begründung |
|---|---|---|---|
| whisper/ModelCatalogTest | WhisperLoom.Core.Tests.Offline.ModelCatalogTest | portiert | Desktop ohne Text-Katalog (Ollama statt LiteRT): IDs und Dateien nur im whisper-Katalog eindeutig; `Prefs.DEFAULT_OFFLINE_MODEL` → `OfflineSettings.Model`. |
| whisper/ModelDownloaderTest | WhisperLoom.Core.Tests.Offline.ModelDownloaderTest | portiert | JDK-HttpServer → `RangeServer` (TcpListener); `isCancelled` → `CancellationToken`; Textmodell-Fall → Archiv des Begleitprozesses; zusätzlich A4.5 mit falscher Prüfsumme nach Fortsetzung. |
| api/NetworkCheckTest | WhisperLoom.Core.Tests.Pipeline.NetworkCheckTest | portiert | Je URL ein Theory-Fall; zusätzlich Loopback ohne Schnittstelle (Desktop). |
