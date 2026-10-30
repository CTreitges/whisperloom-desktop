# Test-Mapping: Text, Prompts, Audio, Verlauf (Phase 1, WLD-P1a)

Zuordnung Kotlin-Testklasse (Android-Repo `app/src/test*`) → C#-Testklasse, geprüft mit `tools/test_parity.py`
(Format und Regeln: `test-mapping-contracts.md`). Testvektoren 1:1 aus Kotlin; zusätzliche Desktop-Fälle stehen
in den C#-Klassen unter „Desktop:“. `AudioConvert` und `AudioUtils` haben auf Android keine Tests — eigene Fälle in
`AudioConvertTest`/`AudioUtilsTest`.

| Kotlin-Klasse | C#-Klasse | Status | Begründung |
|---|---|---|---|
| TextPolisherTest | WhisperLoom.Core.Tests.Text.TextPolisherTest | portiert | |
| PolishPlanTest | WhisperLoom.Core.Tests.Text.PolishPlanTest | portiert | |
| ParagrapherTest | WhisperLoom.Core.Tests.Text.ParagrapherTest | portiert | |
| VocabularyTest | WhisperLoom.Core.Tests.Text.VocabularyTest | portiert | |
| api/RefinePromptTest | WhisperLoom.Core.Tests.Prompts.RefinePromptTest | portiert | |
| AudioChunksTest | WhisperLoom.Core.Tests.Audio.AudioChunksTest | portiert | |
| WavEncoderTest | WhisperLoom.Core.Tests.Audio.WavEncoderTest | portiert | |
| WavHeaderTest | WhisperLoom.Core.Tests.Audio.WavHeaderTest | portiert | |
| WavSamplesTest | WhisperLoom.Core.Tests.Audio.WavSamplesTest | portiert | |
| agent/AutoStopDetectorTest | WhisperLoom.Core.Tests.Audio.AutoStopDetectorTest | portiert | |
| history/HistoryTest | WhisperLoom.Core.Tests.History.FileHistoryStoreTest | ausgenommen | Teilweise portiert: 18 der 27 Fälle (Rundreise, Kürzen, Aus, Reste, tolerantes Lesen, Fremd-Ids, Gleichzeitigkeit, Löschen/Rückgängig) in FileHistoryStoreTest, angepasst an den Vertrag IHistoryStore (eine Fassung je Eintrag, Ablagefehler als IOException). Nicht übertragbar, weil nicht im Vertrag: Fassungen bearbeiten (2), Neu verarbeiten über die Pipeline (6, Desktop erst P5b), Änderungs-Signal (1). |
