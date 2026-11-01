# Komfort-Parität Android → Desktop (W2-OUT)

Stand 2026-11-01. Ziel: kein Diktat geht verloren; Text, der nicht eingefügt werden kann, liegt verlässlich in der
Zwischenablage, mit Hinweis. Quelle Android: `whisperloom-android` (main). Status je Punkt: **übernommen** (mit Test),
**war schon da** (Datei:Zeile) oder **nicht sinnvoll am Desktop** (Grund).

## Ausgabe-Entscheidung

Core, OS-frei: `src/WhisperLoom.Core/Output/OutputDecision.cs`. Eingaben: Fokus-Klasse des Ziels (Windows: UI Automation,
Rückfall MSAA; Linux: immer unbekannt), Ergebnis des Einfügens, `output.alwaysCopy` und ob das Ziel „vorsichtig“ zu behandeln ist
(`OutputDecision.IsCautious`: Browser aus `OutputDecision.BrowserProcesses` – chrome, msedge, firefox, brave, opera, vivaldi, msedgewebview2; Prozessname ohne Groß-/Kleinschreibung – oder Electron-App, erkannt an der Fensterklasse `Chrome_WidgetWin_1`). Eingefügt wird nur in ein
Eingabefeld oder bei unbekanntem Fokus (`OutputDecision.ShouldInsert`); bei Passwortfeld und „kein Eingabefeld“ wird es gar
nicht versucht (Spalte „Einfügen“ = nicht versucht). Jede Zeile hat einen xUnit-Fall (`OutputDecisionTest.JedeZeileDerTabelle`).

| Fokus | Einfügen | alwaysCopy | Aktion | Zwischenablage | Hinweis | Verlauf |
|---|---|---|---|---|---|---|
| Eingabefeld | eingefügt | aus | einfügen | alte wiederhergestellt | – | ja |
| Eingabefeld | eingefügt | an | einfügen | Text bleibt | – | ja |
| Eingabefeld | gescheitert / nicht versucht | egal | Zwischenablage | Text | „Kopiert – Strg+V“ | ja |
| Passwortfeld | egal | egal | nichts | unverändert | „Passwortfeld – nicht eingefügt“ | **nein** |
| kein Eingabefeld | nicht versucht / gescheitert | egal | Zwischenablage | Text | „Kopiert – Strg+V“ | ja |
| kein Eingabefeld | eingefügt¹ | aus / an | einfügen | alte / Text | – | ja |
| unbekannt (kein Browser, kein Electron) | eingefügt | egal | einfügen | **Text bleibt** | – | ja |
| unbekannt, **Browser oder Electron** | eingefügt | aus | einfügen | **alte wiederhergestellt** | – | ja |
| unbekannt, **Browser oder Electron** | eingefügt | an | einfügen | Text bleibt (Wahl des Nutzers gewinnt) | – | ja |
| unbekannt | gescheitert / nicht versucht (Linux) | egal | Zwischenablage | Text | „Kopiert – Strg+V“ | ja |

¹ kommt im Ablauf nicht vor (ohne Eingabefeld wird nicht eingefügt), der Vollständigkeit halber in der Tabelle und im Test.

„Gescheitert“ = Ziel weg, Admin-Fenster (UIPI), nicht nach vorn holbar oder SendInput abgewiesen
(`TextInserter.cs`, `InsertOutcome`). Zwischenablage belegt → bis zu 6 Versuche mit wachsender Pause (≈ 1,5 s,
`ClipboardCopier`).

### Was bei „unbekannt“ passiert – gemessen
Messung mit `smoke.py --suite input` (Teilprüfung `focus-measure`, nur lesende Abfrage `WhisperLoom focus-probe`,
2026-11-01, Windows 11, Edge und Chrome mit eigenem Temp-Profil, Seiten per `data:`-URL mit `autofocus`):

| Ziel | UIA (Control-Type) | Klasse | mit MSAA-Rückfall |
|---|---|---|---|
| TestTarget-Textfeld (WinForms Edit) | 50004 Edit | Eingabefeld | – |
| TestTarget-Passwortfeld | 50004, IsPassword | Passwortfeld | – |
| TestTarget-Knopf | 50000 Button | kein Eingabefeld | – |
| Notepad (Windows 11) | 50030 Document mit Wert | Eingabefeld | – |
| Edge: Textarea, Passwort, Knopf, Seite ohne Feld | 50033 Pane (auch nach 4 s) | unbekannt | unbekannt |
| Chrome: Textarea, Knopf, Seite ohne Feld | 50033 Pane (einmal 50022) | unbekannt | unbekannt |
| Chrome: Passwort | 50033 Pane, in einem von drei Läufen nach 1,5 s 50004 + IsPassword | meist unbekannt | unbekannt |

Begründung der Entscheidung: Browser liefern ihren Webinhalt per UIA nur als „Pane“. Würde „unbekannt“ wie „kein
Eingabefeld“ behandelt, ginge in Browsern gar nichts mehr direkt ins Feld – der häufigste Einsatzort. Deshalb: **einfügen
und den Text danach in der Zwischenablage lassen** (statt die alte Zwischenablage wiederherzustellen). Kam das Einfügen
nicht an (z. B. kein Feld auf der Webseite), genügt Strg+V; es geht nichts verloren. Kein Hinweis, weil meist eingefügt
wurde. Bei „unbekannt“ fragt die App zusätzlich MSAA (`MsaaFocusSource`: Rolle und `STATE_SYSTEM_PROTECTED`); das hilft
bei Programmen mit MSAA, in der Nachmessung an Edge/Chrome aber nicht (weiter „unbekannt“).

**Entschieden (Christof, 2026-11-01): „Vorsichtig im Browser“.** Passwortfelder auf Webseiten sind per UIA nicht
erkennbar. Bei Fokus „unbekannt“ in einem Browser-Prozess wird eingefügt und danach die vorherige Zwischenablage
wiederhergestellt – der Text bleibt **nicht** in der Zwischenablage; der Eintrag kommt in den Verlauf. In allen anderen
Apps bleibt „unbekannt“ wie oben (Text bleibt in der Zwischenablage). `output.alwaysCopy = an` gewinnt immer. Tests:
`OutputDecisionTest.BrowserZeilen`, `ImBrowserAendertSichNurUnbekanntEingefuegtOhneAlwaysCopy`, `BrowserErkennungAmProzessnamen`,
`FocusDecisionTest.UnbekannterFokusImBrowser…`, `ImBrowserGewinntImmerKopieren`, `UnbekannterFokusAusserhalbDesBrowsers…`.

**Erweitert (Christof, 2026-11-01, umgesetzt in W2-SEL): Electron-Apps wie Browser.** Slack, Discord, VS Code und andere
Electron-Apps haben eigene Prozessnamen, melden ihren Inhalt aber ebenso nur als „Pane“. Erkannt werden sie an der Klasse
des Zielfensters `Chrome_WidgetWin_1` (`IWindowAdapter.WindowClass`); ohne lesbare Klasse wird nicht am Prozessnamen geraten.
Gleiches Verhalten wie im Browser: einfügen, alte Zwischenablage zurück, Verlauf ja, `alwaysCopy` gewinnt. Tests:
`OutputDecisionTest.VorsichtigBeiBrowsernUndElectronFenstern`, `FocusDecisionTest.UnbekannterFokusInElectronApp…`,
`InElectronAppGewinntImmerKopieren`.
Einschränkung der Messung: ob `autofocus` in einer `data:`-Seite im neuen Fenster wirklich das Feld fokussiert, ist nicht belegt.

## Android-Punkte

| Android (Fundstelle) | Status | Desktop / Begründung | Test |
|---|---|---|---|
| FieldGone-Hinweis „Text liegt im Verlauf“ (`ime/FieldGone.kt:18-26`) | übernommen | Statt „liegt im Verlauf“ immer Zwischenablage + „Kopiert – Strg+V“ und Verlauf (Entscheidung Christof); `TextInserter`, `InputFlow.ShowFallback` | `FocusDecisionTest.OhneEingabefeldInDieZwischenablageMitHinweis`, `CopyOutputTest.Diktat_ohne_Eingabefeld_…` |
| Passwort „nicht eingefügt“, nicht in den Verlauf (`FieldGone.kt`, `history/HistoryPolicy.kt`) | übernommen | Fokus Passwortfeld → nichts einfügen, nichts kopieren, kein Verlauf, kein „erneut einfügen“ (`DictationController.ForgetIfPrivate`) | `CopyOutputTest.Diktat_ins_Passwortfeld_landet_nirgends`, `FocusDecisionTest.PasswortfeldWeder…`, Smoke `focus-password` |
| Placeholder-Erkennung leeres Feld (`a11y/TextInserterAccessibilityService.kt:69-75`) | nicht sinnvoll am Desktop | Der Desktop liest den Feldinhalt nicht und ersetzt nichts – er fügt per Strg+V bzw. SendInput an der Schreibmarke ein; ein Platzhalter kann nicht im Ergebnis landen | – |
| Leerzeichen nach dem Diktat (`trailingSpace`, `Prefs.kt:294`, `WhisperLoomInputMethodService.kt:953`) | übernommen | `polish.trailingSpace` (ab Werk an) war im Desktop wirkungslos; jetzt beim Diktat per Kürzel (`OutputDecision.WithTrailingSpace`), nicht bei „erneut einfügen“/Umschreiben | `OutputDecisionTest.LeerzeichenDanachWieAndroid`, `OverlayStageTest.Leerzeichen_danach_nur_beim_Diktat_wie_Android` |
| Leerzeichen davor, wenn direkt an ein Wort angefügt (`WhisperLoomInputMethodService.kt:945-952`) | übernommen | Zeichen vor der Schreibmarke per UIA TextPattern (`UiaCaretSource`: Auswahl auf ihren Anfang ziehen, ein Zeichen zurück, nur dieses Zeichen lesen; 300 ms Grenze), Regel `OutputDecision.WithLeadingSpace`; nur beim Diktat, nie im Passwortfeld. Ohne TextPattern (Browser melden „Pane“, eigene Oberflächen) gibt es keine Auskunft → kein Leerzeichen (lieber keins als ein falsches) | `OutputDecisionTest.LeerzeichenDavorWieAndroid`, `PruefRunde1Test.B1f_*` (4), Smoke `leading-space` |
| Verlauf: Ursprung nie überschrieben (`history/HistoryEntry.kt:81-96`) | übernommen | `HistoryEntry.Raw` bleibt; Bearbeiten des Ursprungs legt Fassung `edited` an (`HistoryVersions`) | `HistoryVersionsTest.UrsprungWirdNieUeberschrieben`, `HistoryWindowTest.Ursprung_bearbeiten_…` |
| Fassungen je Verarbeitung + „bearbeitet“ (`HistoryEntry.kt:65-111`) | übernommen | `HistoryEntry.Versions` (Vertrag additiv), Ersetzen am Platz | `HistoryVersionsTest` (7 Fälle) |
| Bearbeiten (`ui/history/HistoryEditScreen.kt`, `History.edit`) | übernommen | F2 im Verlauf, Strg+Enter speichert, Esc verwirft | `HistoryWindowTest.F2_bearbeitet_…`, `Esc_verwirft_…` |
| „Andere Stufe“ im Hintergrund (`History.kt:164-187`, `ui/history/HistoryJobs.kt`) | übernommen | Stufen-Wähler im Detail; `DictationController.ReprocessVersionAsync` (ohne Ausgabe, ohne Diktat-Zustand); gespeichert/ohne KI/„kept“/weg | `ReprocessVersionTest` (6 Fälle), `HistoryWindowTest.Neu_verarbeiten_…` |
| Rückfrage vor Ersetzen bearbeiteter Fassung | übernommen | Inline „Diese Fassung wurde bearbeitet …“ mit Abbrechen/Ersetzen | `HistoryWindowTest.Bearbeitete_Fassung_ersetzen_fragt_erst` |
| Löschen mit Rückgängig (`ui/history/HistoryListScreen.kt:84-95`) | übernommen | Entf löscht, „Rückgängig“ bzw. Strg+Z holt unter alter Id zurück | `HistoryWindowTest.Entf_loescht_und_StrgZ_holt_zurueck` |
| Aufbewahrung 10–500 (`HistorySettings`) | war schon da (`src/WhisperLoom.Core/Contracts/Settings/WhisperLoomSettings.cs:207`) | – | bestehende Tests |
| Rückfrage beim Verkleinern (`History.excess`, `History.kt:226`) | übernommen | Seite „Datenschutz“: „N ältere Diktate werden dabei gelöscht“, kürzt sofort nach Ja (`FileHistoryStore.TrimAsync`) | `OutputSettingsTest.Verkleinern_…` (2 Fälle) |
| Liste nach Tagen mit Meta + Stufen-Etikett (`HistoryListScreen.kt`) | übernommen | Heute/Gestern/Datum, Uhrzeit · Dauer · Erkennung, Etikett | `HistoryWindowTest.Liste_nach_Tagen_neueste_zuerst` |
| Detail mit Chips Ursprung/Fassungen (`ui/history/HistoryDetailScreen.kt`) | übernommen | `HistoryDetailViewModel.Chips` | `HistoryWindowTest.Neu_verarbeiten_…` |
| DictationRescue: Audio bei Abbruch trotzdem erkennen (`ime/DictationRescue.kt`) | nicht sinnvoll am Desktop | Android rettet Audio, wenn die Tastatur während der Aufnahme verschwindet (App-Wechsel schließt die IME). Am Desktop hängt die Aufnahme an keinem Feld: Fokuswechsel beendet sie nicht, ein Ende von außen (Tray/CLI) erkennt das Audio und gibt es in die Zwischenablage (`InputFlow.OnControllerChanged`); scheitert die Erkennung wiederholbar, hilft „Erneut senden“ (unten) | – |
| Gepuffertes Audio „Erneut senden“ bei Netz/408/429/5xx (`api/ApiErrors.kt:104-108`) | übernommen | Bei wiederholbarem Fehler (`ApiErrors.IsRetryable`) bleibt das Audio nur im Speicher (`DictationController.Comfort.cs`); „Erneut senden“ im Overlay (gleiches Ziel), im Tray (nur sichtbar, wenn etwas zu senden ist) und per CLI `retry`; neue Aufnahme verwirft es | `ComfortTest.Netzfehler_haelt_das_Audio_…` (auch: kein .wav im Datenordner), `Nicht_wiederholbarer_Fehler_…`, `Neue_Aufnahme_verwirft_…`, `Erneut_senden_im_Overlay_…`, `InputFlowTest.Hold_error_shows_toast` |
| Menschenlesbare Fehler | war schon da (`src/WhisperLoom.App/Services/DictationController.Extras.cs:299`) | Fehlerart → `Error_*`-Text; bei wiederholbaren Fehlern neu der Zusatz „Erneut senden im Tray-Menü“ | bestehende Tests, `InputFlowTest.Hold_error_shows_toast` |
| Fallback mit 8-s-Verbindungs-Timeout | war schon da für die KI (`src/WhisperLoom.Core/Pipeline/RefinePlan.cs:32`); Erkennung ohne eigenes Timeout (`FallbackBackend.cs:22`) | Erkennung gehört zu W2-LOCAL | bestehende Tests |
| KI scheitert → Rohtext mit Hinweis | übernommen | Rohtext war schon da (`src/WhisperLoom.Core/Pipeline/DictationPipeline.cs:208`); neu der Hinweis „Ohne KI eingefügt – Textverbesserung nicht erreichbar“ direkt nach dem Diktat | `OverlayStageTest.KI_gescheitert_Rohtext_eingefuegt_mit_Hinweis`, `Mit_KI_kein_Hinweis` |
| Während die KI läuft: Tipp = sofort ohne KI (`api/TextRefiner.kt:204-236`) | übernommen | Kürzel (Umschalten oder Halten) während „Wird verarbeitet …“ überspringt die Verbesserung (`RefineSkip` jetzt durchgereicht, `AppPipeline` als `ISkippablePipeline`); eingefügt wird der Text ohne KI, ohne Hinweis (wie Android); Overlay zeigt „Kürzel erneut drücken = sofort ohne KI einfügen“ | `ComfortTest.Kuerzel_waehrend_der_Verbesserung_…`, `Ohne_laufende_Verbesserung_…` |
| Antwort-statt-Bearbeitung verwerfen (`TextRefiner.kt:204-236`) | war schon da (`src/WhisperLoom.Core/Api/TextCleanup.cs:101`) | – | bestehende Tests (`TextCleanupTest`) |
| < 0,3 s verwerfen | übernommen | `RecordingLimits.TooShort` (Audiolänge); Fehlgriff: nichts erkennen, nichts ausgeben, kein Verlauf (`DictationController.DiscardIfTooShort`) | `RecordingLimitsTest.ZuKurz`, `ComfortTest.Kuerzer_als_300_ms_…`, `Ab_300_ms_wird_erkannt` |
| 12-Minuten-Deckel mit Warnung ab 10 min (`ime/DictationSession.kt:110-132`) | übernommen | `RecordingLimits` (nur Online-Erkennung, offline kein Deckel): ab 10 min Hinweis, bei 12 min hält die Aufnahme an, „Weiter“ gesperrt; beim Halten-Kürzel wird gesendet. Takt aus `InputFlow.Tick` bzw. für Tray/CLI/Linux aus der App (`StartLengthWatch`) | `RecordingLimitsTest.Stufen`, `ComfortTest.Hinweis_ab_10_min_…`, `Offline_ohne_Deckel`, `Halten_ueber_12_min_…`, `Umschalten_ueber_12_min_…` |
| Audio an leisen Stellen schneiden | nicht sinnvoll am Desktop | Android schneidet nur beim **Teilen von Audiodateien** (`SharedAudioTranscriber`, `TranscriptionEngine.kt:294`), nicht beim Diktat. Den Teilen-Weg gibt es am Desktop nicht (`share.*` ist in `SettingsCatalog.Exceptions` als „nur Android“ geführt); ein Diktat ist durch den 12-min-Deckel (oben) unter der 25-MB-Grenze der Anbieter, die lokale Erkennung verarbeitet lange Aufnahmen selbst. `AudioChunks.Plan` bleibt portiert für einen späteren Datei-Import | – |
| VPN-bewusste Netzprüfung | war schon da (`src/WhisperLoom.Core/Pipeline/NetworkAvailability.cs:34`) | – | `NetworkCheckTest` |

## Desktop-eigene Ergänzungen

| Ergänzung | Test |
|---|---|
| UI-Automation-Fokusprüfung mit Zeitgrenze 600 ms, MSAA-Rückfall für Browser | `FocusDecisionTest` (Klassen, Zeitgrenze, COM-Fehler, Rückfall, echte UIA-/MSAA-Abfrage) |
| `output.alwaysCopy`: Text nach dem Einfügen zusätzlich in der Zwischenablage (Seite „Ausgabe“) | `FocusDecisionTest.ImmerKopieren…`, `OutputSettingsTest` |
| Enter nur noch als Einstellung: Strg+Enter und „Senden + Enter“ entfernt; `output.pressEnter` global, App-Profil Erben/An/Aus | `HotkeyStateMachineTest` (Strg+Enter = keine Aktion, nicht geschluckt), `FocusDecisionTest.EnterFolgtProfilSonstEinstellung`, Smoke `ctrl-enter-no-enter` |
| Kopieren mit Wiederholung und Gegenprobe (Win32 statt Avalonia-OLE), Bestätigung „Kopiert“ | `CopyOutputTest` (Copier), Smoke `history-copy-20` |
| Tray-Klick fügt ein und lässt den Text immer auch in der Zwischenablage | `CopyOutputTest.Tray_klick_…` (3 Fälle) |
| Stufen-Wähler (Klick, ↑/↓, Tab/Umschalt+Tab, 1–9, Enter, Esc) – Verlauf, Overlay, wiederverwendbar für W2-SEL | `StagePickerTest` (8), `OverlayStageTest`, `HistoryWindowTest.Stufen_waehler_per_Tastatur_im_Verlauf` |
| Verlauf komplett per Tastatur, Volltextsuche (auch Ursprung und Fassungen) | `HistoryWindowTest` (12) |
| Modus „nur Zwischenablage“ respektiert Passwortfelder (Ziel noch vorn → Fokus prüfen, nie kopieren, kein Verlauf) | `PruefRunde1Test.B2_*` (3) |
| WebView2-Apps (`msedgewebview2`, z. B. neues Teams) gelten als Browser („Vorsichtig im Browser“). Electron-Apps (eigener Prozessname je App, Fensterklasse `Chrome_WidgetWin_1`) ebenso (W2-SEL, Entscheidung Christof 2026-11-01) | `PruefRunde1Test.B3_WebView2_App_gilt_als_Browser`, `OutputDecisionTest.BrowserErkennungAmProzessnamen`, `OutputDecisionTest.VorsichtigBeiBrowsernUndElectronFenstern`, `FocusDecisionTest.UnbekannterFokusInElectronApp…` |
| Linux: gleiche Entscheidung (Fokus unbekannt, kein Einfügen) → Zwischenablage + Hinweis per `notify-send` | `CopyOutputTest.Ohne_Einfuegen_…`, Smoke `output-hint` (Suite full, beide Plattformen) |

## Warum Kopieren bei Christof unzuverlässig war (Ursachen)
1. **Tray-Klick**: fügte ins Ziel des letzten Diktats ein und stellte danach die alte Zwischenablage wieder her. Hatte
   dort kein Eingabefeld den Fokus, ging Strg+V ins Leere – der Text war weder im Ziel noch in der Zwischenablage.
2. **Verlauf „Kopieren“**: lief über die Avalonia-12-Zwischenablage (`OleSetClipboard` mit verzögertem Rendern aus unserem
   Prozess, 10 Versuche à 100 ms), aus einem `async void`-Handler ohne Fehlerbehandlung und ohne Bestätigung – ein
   Fehlschlag war unsichtbar.
3. **Win32-Weg** (Tray/CLI, Fallback): nur 10 × 50 ms Öffnen-Versuche, keine Gegenprobe; hält ein Zwischenablage-Manager
   oder Remote-Desktop die Zwischenablage länger, scheiterte das Kopieren.

Fix: ein Kopierweg für Verlauf, Tray und Fallback – Win32 mit sofortigem Rendern, bis zu 6 Versuche mit wachsender Pause,
Gegenprobe durch Zurücklesen, „Kopiert“ erst danach.
