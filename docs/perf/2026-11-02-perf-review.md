# Performance-Review WhisperLoom Desktop

Stand 2026-11-02 · Skill `desktop-perf-review` · Stack Avalonia 12.1.4, .NET 10 · Ausgangsstand main `caf0ed7` (+ Messzugang
`settings`/`help`, #31) · Run `2026-11-02-whisperloom-perf-release`

Messumgebung (alle Vorher/Nachher-Paare gleich): PC Christof, Netzbetrieb, Energieplan Höchstleistung, 179 Hz, 2560×1440,
96 DPI (100 %), AMD Ryzen 5 5600, Windows 11 26100. Testbetrieb: eigener `--data-dir` je Messung, `--output-file`,
Test-Credential-Präfix, keine globalen Hotkeys, Lock-Datei, Beenden nur per eigener PID. Hover/Klick: `measure.py hover`
(15 Läufe nach 2 Warmups; Klick: jede Seite einmal = erstes Öffnen). Seitenwechsel: `pageswitch.py` (Klick reihum auf 11
Seiten, 5 Runden, Ruhe = 250 ms ohne Pixeländerung im Client-Bereich). Start: `measure.py startup` (10 Warmstarts, Verb `show`).
Untergrenze der Abtastung in dieser Sitzung ≈ 34 ms (für alle Fenster gleich, Anzeige-/Abtastpfad).

## Ampel je Faktorgruppe

Baseline = main caf0ed7 + Messzugang (`dotnet build`), Endstand = main 9f62039 als Publish win-x64 mit den Schaltern aus
`build/pack.py` (wie das Release), Abschlussmessung unten.

| Faktorgruppe | Baseline | Endstand | Grundlage Endstand |
|---|---|---|---|
| UI-Thread / Dispatcher | rot | 🟡 gelb | Klick „Erkennung“ UI-Blockade 54,9 ms; Vorbau nach dem Öffnen 76,5 ms (Ausnahme A1) |
| async/await-Fallen | grün | 🟢 grün | 2 Befunde niedrig |
| I/O auf dem UI-Thread | grün | 🟢 grün | 0 Befunde |
| GC / Allokationen | gelb | 🟢 grün | 0 Befunde (PERF-4), GC während Hover 0–2 Sammlungen |
| Binding / Compiled Bindings | grün | 🟢 grün | 1 Befund niedrig |
| Layout / Measure / Arrange | grün | 🟢 grün | 0 Befunde |
| Styles / Selektoren / Ressourcen | grün | 🟢 grün | 0 Befunde |
| Virtualisierung | grün | 🟢 grün | nur kurze, feste Listen (herabgestuft) |
| Rendering / Compositor / GPU | grün | 🟢 grün | 0 Befunde |
| Hover-/Pointer-Pfad, Transitions | rot | 🟢 grün | langsamstes Element erste Reaktion p95 34,7 ms; Klick „Erkennung“ 34,2 ms |
| Startzeit (JIT/R2R/AOT/Trimming) | gelb | 🟡 gelb | bereit p50 1002,2 ms, p95 1098,5 ms (Ausnahme A2) |
| DPI / Multi-Monitor | grün | 🟢 grün | 0 Befunde |
| Treiber / Energieprofil | grün | 🟢 grün | Endlos-Animationen nur bei `.recording` (herabgestuft) |
| Linux (alle Messgruppen) | nicht gemessen | ⚪ nicht gemessen | Skill-Lücke: keine UI-Messung unter Linux |

Hover Baseline (erste Reaktion p95 des langsamsten Elements): Bühne 34,6 ms · Einrichtung 34,7 ms · Einstellungen 34,8 ms ·
Hilfe 34,6 ms — überall grün, UI-Blockade ≤ 8 ms.

## Fixes mit Vorher/Nachher

### PERF-2 Einstellungen: Seiten-Views cachen, im Leerlauf vorbauen, Seite einen Durchlauf nach der Markierung

Ursache: `src/WhisperLoom.App/Views/SettingsWindow.axaml:95` (`ContentControl` + `Window.DataTemplates`) baute die Seite
bei jedem Klick neu; das erste Öffnen kostete zusätzlich JIT und Template-Erzeugung. Fix: `Views/Settings/PageViewCache.cs`
(eine View je Seiten-ViewModel; abgehängt ohne DataContext, damit Auswahl-Listen nichts zurückschreiben), Vorbau in kleinen
Schritten nach dem Öffnen (`SettingsWindow.Prebuild`), Seitenwechsel mit Hintergrund-Priorität nach der Markierung.

Vorher = Baseline (`dotnet build`, 6d6337a), Nachher = PERF-2 (`dotnet build`, a1b248e), gleiche Umgebung:

| Messung | Vorher | Nachher |
|---|---|---|
| Klick „Erkennung“ (erstes Öffnen): erste Reaktion / Ruhe | 266,5 / 266,5 ms | 33,8 / 33,8 ms |
| Klick „Erkennung“: UI-Blockade max | 141,4 ms | 85,6 ms |
| Klick „Text & Stufen“: erste Reaktion / UI-Blockade | 133,4 / 58,1 ms | 17,3 / 51,0 ms |
| Seitenwechsel erstes Öffnen (max über 11 Seiten): Ruhe / Blockade | 316,5 / 158,2 ms | 199,5 / 100,4 ms |
| Seitenwechsel wiederholt (alle Seiten): Ruhe p50 / p95 | 34,8 / 117,6 ms | 51,2 / 134,6 ms |
| Seitenwechsel wiederholt „Erkennung“: Ruhe p50 / p95 | 117,7 / 130,8 ms | 134,6 / 150,5 ms |
| UI-Blockade nach dem Öffnen (Vorbau, 3 s) | – | 156,5 ms |
| Hover Einstellungen: erste Reaktion p95 (max) | 34,8 ms | 34,5 ms |

Lesart (Prüfer PR #32): Die Klick-Messung startet ≥ 3 s nach dem Öffnen, trifft also eine **vorgebaute** Seite. Die Arbeit
des ersten Öffnens ist nicht verschwunden, sondern läuft im Leerlauf direkt nach dem Öffnen (Zeile „UI-Blockade nach dem
Öffnen“, ohne R2R 156,5 ms – mehr als die 141 ms der Baseline beim Klick). Ein Klick in der ersten Sekunde trifft eine noch
nicht vorgebaute Seite und kostet wie vorher. Wiederholte Wechsel werden nicht schneller: Wiederanhängen (Styles) + neu
Binden kostet bei „Erkennung“ etwa so viel wie der Neubau, dazu ein Frame Verzögerung durch die Hintergrund-Priorität.
Sichtbare Nebenwirkung: Scroll-Stand und aufgeklappte „Erweitert“-Bereiche bleiben über Seitenwechsel erhalten.

Ampel-Wirkung allein (ohne R2R): Hover-/Pointer-Pfad rot → grün (Klick erste Reaktion ≤ 34 ms); UI-Thread bleibt rot
(Vorbau 156,5 ms nach dem Öffnen). Erst mit PERF-3 (ReadyToRun) sinkt der Vorbau auf 77,6 ms (gelb, siehe Abschlussmessung).

### PERF-3 ReadyToRun im Publish (Windows ohne, Linux mit Composite)

Ursache: kein ReadyToRun (`build/pack.py:42`), Start und erstes Öffnen jeder Seite JIT-lastig. Fix: `build/pack.py`
`readytorun_args` (Release-Pakete) und CI-Publish. Composite nur unter Linux: unter Windows entfernt Composite die
Microsoft-Signatur von ~175 Laufzeit-DLLs (WDAC/AppLocker, `docs/signing.md`) – Entscheidung Christof 2026-11-02.

Vorher = Publish win-x64 ohne R2R, Nachher = Publish mit den Schaltern aus `pack.py`, gleicher Commit (main 3da9c25 +
PERF-3/4), gleiche Umgebung:

| Messung | Vorher | Nachher |
|---|---|---|
| Start sichtbar p50 | 1205,3 ms | 651,8 ms |
| Start bereit p50 / p95 | 1572,1 / 1609,7 ms | 979,3 / 991,6 ms |
| Klick „Erkennung“: erste Reaktion / UI-Blockade | 34,4 / 81,9 ms | 34,2 / 56,5 ms |
| Klick „Text & Stufen“: UI-Blockade | 66,3 ms | 32,2 ms |
| Publish-Größe win-x64 | 211 MB | 228 MB |

Verworfen: Composite unter Windows (bereit p50 828–872 ms, 257 MB; Signaturen, s. o.); `TieredPGO=false` ohne Wirkung
(978 ms gegenüber 982 ms).

### PERF-4 Hilfe-Illustration: Pinsel und Stifte einmal je Theme/Quelle

Ursache: `src/WhisperLoom.App/Views/Help/HelpIllustration.cs:118/126` – je Render zweimal `TryFindResource` und ein neuer
`Pen` je Shape. Fix: `Paint()`-Cache, verworfen bei Theme-, Ressourcen- und Quellwechsel.

Vorher/Nachher headless (1000 × `Render` der Illustration „recognition“, 18 Shapes, gleicher PC):

| Messung | Vorher | Nachher |
|---|---|---|
| Allokation je Render | 11 072 Bytes | 688 Bytes |
| Zeit je Render | 285,1 µs | 184,4 µs |
| Hover Hilfe: erste Reaktion p95 (max) / Allokation während der Messung | 34,6 ms / 419 KB | 34,3 ms / 448 KB |

Die Hover-Messung zeichnet die Illustration nicht neu (Allokation dort unverändert, Rauschen); der Fix wirkt beim Scrollen,
bei Größenänderung und beim Theme-Wechsel.

## Abschlussmessung (main 9f62039, Publish win-x64 wie Release)

| Messung | Baseline | Endstand |
|---|---|---|
| Start sichtbar p50 | 1200,3 ms | 672,8 ms |
| Start bereit p50 / p95 | 1567,2 / 1696,5 ms | 1002,2 / 1098,5 ms |
| Hover Bühne / Einrichtung / Einstellungen / Hilfe: erste Reaktion p95 (max) | 34,6 / 34,7 / 34,8 / 34,6 ms | 34,7 / 34,4 / 34,4 / 34,3 ms |
| Hover: UI-Blockade max (alle Fenster) | 8,3 ms | 25,8 ms |
| Klick „Erkennung“ (erstes Öffnen nach ≥ 3 s): erste Reaktion / UI-Blockade | 266,5 / 141,4 ms | 34,2 / 54,9 ms |
| Klick „Text & Stufen“: erste Reaktion / UI-Blockade | 133,4 / 58,1 ms | 17,5 / 34,2 ms |
| Seitenwechsel wiederholt (alle Seiten): erste Reaktion p95 / Ruhe p95 / Blockade max | 51,7 / 117,6 / 30,7 ms | 35,7 / 134,3 / 34,6 ms |
| Seitenwechsel erstes Öffnen (max über 11 Seiten): Ruhe / Blockade | 316,5 / 158,2 ms | 150,5 / 65,5 ms |
| UI-Blockade nach dem Öffnen der Einstellungen (Vorbau, 3 s) | – | 76,5 ms |

Hinweis Startzeit: Mehrere Läufe desselben Builds lagen bei 979,3 / 1002,2 ms (bereit p50) – die Grenze 1000 ms liegt im
Streubereich. Hover-Blockade 25,8 ms fällt in die Einstellungen (Seitenleiste), grün.

## Ausnahmen

Gelb bleibt, wo grün nur mit Großumbau ginge (Zielwert des Runs, Christof 2026-11-02).

- **A1 UI-Thread / Dispatcher (🟡 gelb):** Messwert: Klick „Erkennung“ UI-Blockade 54,9 ms (Grenze grün 50 ms),
  Seitenwechsel erstes Öffnen max 65,5 ms, ein Vorbau-Schritt direkt nach dem Öffnen 76,5 ms. Ursache: Anhängen der großen
  Seite „Erkennung“ (`Views/Settings/RecognitionPage.axaml`, 5 Gruppen, 7 Auswahllisten, 6 Textfelder) wendet Styles an
  und bindet alle Steuerelemente in einem Dispatcher-Durchlauf. Verworfene Wege: (a) alle Seiten dauerhaft angehängt und
  gebunden lassen – Auswahl-Listen verborgener Seiten schreiben beim Austausch ihrer Listen `null` in die Einstellungen
  (im Test als NRE aufgefallen, Datenrisiko); (b) die Seite in Teil-Views je Gruppe zerlegen und schrittweise anhängen –
  Umbau aller 11 Seiten und sichtbares Nachladen beim Wechsel; (c) Composite-ReadyToRun unter Windows (weniger JIT) –
  entfernt Microsoft-Signaturen (Entscheidung Christof). Grund: Großumbau ohne fühlbaren Gewinn (alle Reaktionen ≤ 1 Frame
  der Abtast-Untergrenze, Blockaden < 80 ms).
- **A2 Startzeit (🟡 gelb):** Messwert: bereit p50 1002,2 ms, p95 1098,5 ms (Grenze grün 1000 ms; Vorlauf desselben Builds
  979,3 ms), sichtbar 672,8 ms. Verworfene Wege: Composite-ReadyToRun (bereit 828–872 ms, grün) – entfernt unter Windows
  die Microsoft-Signatur von ~175 Laufzeit-DLLs (WDAC/AppLocker, `docs/signing.md`), Entscheidung Christof 2026-11-02;
  Native AOT – nicht kompatibel ohne Umbau (Avalonia-XAML-Reflexion, Velopack, PortAudio; Kompatibilitätsnachweis fehlt);
  `TieredPGO=false` – ohne Wirkung (978 ms). Grund: der verbleibende Weg zu grün ist eine Signatur- bzw. AOT-Frage, kein
  Code-Fix.

## Grenzen

- Linux: keine UI-Messung (⚪ nicht gemessen, Skill-Lücke; AppImage mit Composite-R2R nur im CI gebaut); Verlaufsfenster nur per Tray erreichbar (nicht gemessen); nur Warmstarts.
- Statische Befunde sind Verdachtsstellen; Fehlalarme gestrichen (Sync-over-async in `Program.cs`: CLI vor dem UI-Start).
