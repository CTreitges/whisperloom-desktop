# Designsystem (W2-UI1)

Gemeinsame Tokens, Styles und Bausteine für alle Fenster nach den eigenen UI/UX-Richtlinien (Skill ui-ux-guidelines, ohne
fremden Hersteller-Look). Begründete Abweichungen für den Desktop: `docs/ux-exceptions.md`.

Dateien: `src/WhisperLoom.App/Design/` – `Tokens.axaml` (Farben, Abstände, Schrift), `Icons.axaml` (Symbole, erzeugt), `Controls.axaml`
(Vorlagen der Bausteine), `Styles.axaml` (Klassen), dazu die Bausteine als C#-Klassen. Eingebunden in `App.axaml`.

## Hell/Dunkel/Hochkontrast
- Die App steht global auf Dunkel (Bühne, Overlay, Verlauf bleiben bis UX-3/UX-4 so).
- Ein Fenster mit Designsystem folgt dem System: im Konstruktor `SystemTheme.Follow(this, Application.Current?.PlatformSettings)` und `Classes="loom"` am Fenster. Ein Wechsel im System gilt sofort.
- Hochkontrast: Ist im System ein Kontrastdesign an (`PlatformColorValues.ContrastPreference == High`), nimmt das Fenster die Variante `SystemTheme.HighContrast` (Rückfall Dunkel). Schwarz, Weiß, Akzent `#6FD9C7`, Gelb nur für den Fokus.
- Farben nur als `{DynamicResource Loom.*}`, nie als feste Werte oder `StaticResource` – sonst wechselt die Farbe nicht mit.

## Farb-Tokens
Quelle: Loom-Palette der Android-App (`ui/theme/Color.kt`, `LoomPalette`). Dunkel = Android-Rollen 1:1, hell = dieselben Töne mit getauschten Rollen (dunkle Töne als Text/Akzent, helle als Fläche). Hochkontrast nach `zielstruktur.md` §5 (UX-Lauf 3).

| Token | Dunkel | Hell | Hochkontrast | Einsatz |
|---|---|---|---|---|
| `Loom.Background` | `#0E1116` background | `#EEF1F5` | `#000000` | Fenster |
| `Loom.Sidebar` | `#161A20` surfaceContainerLow | `#E4E8EE` onSurface | `#000000` | Seitenleiste |
| `Loom.Surface` | `#1A1F26` surfaceContainer | `#FFFFFF` | `#000000` | Gruppen (Hochkontrast: weißer Rand) |
| `Loom.SurfaceHigh` | `#242A32` surfaceContainerHigh | `#E4E8EE` | `#000000` | Hinweis-Zeile, Kombi-Felder |
| `Loom.Separator` | `#3A424D` outlineVariant | `#B8C0CC` onSurfaceVariant | `#FFFFFF` | Trennlinien (nur Schmuck) |
| `Loom.Outline` | `#8A95A3` | `#6B7480` | `#FFFFFF` | Rahmen von Feld, Auswahlliste, Schalter aus (≥ 3:1) |
| `Loom.Text` | `#E4E8EE` onSurface | `#0E1116` background | `#FFFFFF` | Text |
| `Loom.TextSecondary` | `#B8C0CC` onSurfaceVariant | `#3A424D` outlineVariant | `#FFFFFF` | Beschreibungen |
| `Loom.Accent` / `Loom.OnAccent` | `#6FD9C7` / `#00382F` | `#0F5B50` / `#FFFFFF` | `#6FD9C7` / `#000000` | Links, Hauptaktion, Schalter an |
| `Loom.Selection` / `Loom.OnSelection` | `#0F5B50` / `#B4F2E6` | `#B4F2E6` / `#00382F` | `#6FD9C7` / `#000000` | gewählte Seite |
| `Loom.Error` | `#FFB4AB` error | `#93000A` errorContainer | `#FFB4AB` | Inline-Fehler, Löschen, Verwerfen |
| `Loom.Warning` | `#FFC85C` | `#4A3600` | `#FFC85C` | Warnsymbol |
| `Loom.Managed` / `Loom.OnManaged` | `#2B4665` / `#D8E7FA` | `#D8E7FA` / `#12304F` | `#000000` / `#FFFFFF` | Verwaltet-Zeile, Etikett „Firmenvorgabe“ |
| `Loom.Focus` | `#6FD9C7` | `#0F5B50` | `#FFFF00` | Fokusring 2 px |
| `Loom.Record` | `#FF4D4D` | `#B3261E` | `#FF6B6B` | Aufnahme-Symbol (nur Symbol, nie Fläche) |
| `Loom.Tile.*` | Container-Töne | gleich | gleich | Symbol-Kacheln (weiße Glyphe) |

Kontrast, geprüft von `DesignTokensTest` (WCAG 2.x, relative Leuchtdichte): Text auf Fläche ≥ 4,5:1, im Hochkontrast ≥ 7:1;
Outline, Fokus, Akzentfläche, Warn- und Aufnahme-Symbol ≥ 3:1.

Fluent: Akzent hell `#0F5B50`, dunkel und Hochkontrast `#6FD9C7` (`App.axaml`; Hochkontrast über die Farben in `Tokens.axaml`,
weil `FluentTheme.Palettes` nur Light/Dark kennt). In allen Varianten überschreibt `Tokens.axaml` die Fluent-Rahmen von Feld,
Auswahlliste, Schalter aus, Kästchen und Optionsfeld mit `Loom.Outline`, den Fokusring mit `Loom.Focus` und Schrift bzw. Knopf
auf Akzentflächen mit `Loom.OnAccent`.

## Abstände, Radien, Schrift
- Abstände nur 4/8/12/16/24/32/48: `Loom.Space.1` … `Loom.Space.7`. Seitenrand 24, Karte 16 + 1 px Rand, Textkante bei x = 41.
- Radien: `Loom.Radius.Group` 10 (Gruppen), `Loom.Radius.Control` 6 (Steuerelemente, Kacheln).
- Schrift (Systemschrift), Größen bei 100 %: `Loom.Font.Caption` 12, `Body` 14, `Title` 16 (Gruppentitel, halbfett), `PageTitle` 20, `WindowTitle` 24, `Display` 32.
- Textgröße des Systems (Windows „Textgröße“, Linux Text-Skalierung, bis 225 %): `TextScale.Follow` beim Start und beim Öffnen eines Fensters multipliziert alle `Loom.Font.*` und die Fluent-Schriftgrößen. Ansichten nutzen darum nie feste Schriftgrößen.

## Symbole
- Eigenes Set, 55 Strich-Symbole (24-px-Raster, Strich 1,75, runde Enden): Quellen `design/icons/*.svg`, erzeugt nach `Design/Icons.axaml` mit `py -3 -X utf8 tools/gen_icons.py` (Prüfen: `--check`). Schlüssel `Loom.Icon.<Name>`, Liste in `tools/gen_icons.py` (`NAMES`).
- Gezeichnet mit `ds:LoomIcon` (Kontur, Farbe aus `Foreground`): 16 px neben Text, 20 px in Knöpfen und Leiste, 24 px allein. `PathIcon` füllt nur und passt nicht zu Strich-Symbolen.
- Symbole tragen nie allein Bedeutung – immer mit Text oder Namen.

## Bausteine
| Baustein | Klasse | Zweck |
|---|---|---|
| Seite | `ds:SettingsPage TitleKey=…` | Titel, Verwaltet-Zeile (automatisch), Inhalt; rollt selbst |
| Gruppe | `ds:SettingsGroup TitleKey=… FooterKey=…` | Überschrift, abgerundete Fläche, optional Fußtext; verschwindet, wenn alle Zeilen Fest sind |
| Zeile | `ds:SettingRow Path=… [TitleKey] [DescriptionKey] [Error] [Stacked]` | Titel/Beschreibung links, Steuerelement rechts (oder darunter mit `Stacked`), Fehler darunter |
| Auswahl mit „Eigenes …“ | `ds:CustomChoiceBox` + `CustomChoiceViewModel` | Liste fester Optionen plus „Eigenes …“ mit Textfeld; leer = Fehler, nicht gespeichert |
| Hinweis-Zeile | `ds:HintRow TextKey=… Kind=Info|Warning` | Symbol + Text, kein Steuerelement |
| Verwaltet-Zeile | `ds:HintRow Kind=Managed` | „Einige Einstellungen werden von … verwaltet“ – setzt die Seite selbst |
| Erweitert | `Expander Classes="advanced"` | seltene Optionen (HIG „Disclosure controls“) |

Regeln:
- **Zeile mit `Path`**: Titel und Beschreibung kommen aus `SettingsCatalog` (eine Quelle für Seite, Suche und Abdeckungstest). Der Accessible Name des Steuerelements wird automatisch auf den Titel gesetzt.
- **Fest / Vorgabe / Frei** (`Managed.cs`): Das Fenster setzt `ds:Managed.Context` einmal; jede Zeile fragt die Stufe ihres Pfades. Fest → Zeile weg (auch für Screenreader); Vorgabe → Etikett, nach einer Änderung des Nutzers (User-Override) zusätzlich „Auf Firmenvorgabe zurücksetzen“. Quelle: `PolicyManagedSource` über das `ManagedState`-Modell aus W2-C0 (`PolicySettings.StateOf`, Overrides und Zurücksetzen über `ManagedDefaults`); der Firmenname kommt aus `management.tenantName`, sonst „deine IT“. Tastenkürzel sind nie Fest.
- **Sofort speichern**: Auswahl und Schalter speichern bei Änderung, Textfelder beim Verlassen bzw. Enter (`UpdateSourceTrigger=LostFocus`). Ungültige Werte zeigen den Fehler in der Zeile und werden nicht gespeichert. Zerstörende Änderungen (Verlauf aus) fragen inline nach.
- **Texte** nur über `Localization/*.json` (DE+EN), Schlüssel-Präfixe: `Page_`, `Section_`, `G_` (Gruppe), `Set_` (Zeile), `Validation_`, `Managed_`.

## Neue Seite anlegen
1. Einträge in `ViewModels/Settings/SettingsCatalog.cs` (Pfad → Seite, Gruppe, Titel) und ggf. Seite in `Pages` (höchstens 9).
2. `ViewModels/Settings/<Seite>PageViewModel.cs` (erbt `SettingsPageViewModel`), `Views/Settings/<Seite>Page.axaml` mit `ds:SettingsPage`.
3. In `SettingsShellViewModel.CreatePages` und als `DataTemplate` in `SettingsWindow.axaml` eintragen.
4. `SettingsCoverageTest` muss grün bleiben.

## Tray-Menü
Nach HIG „Menus“: Hauptaktion (Aufnahme starten/beenden) oben, dann Gruppen durch Trenner – Stufe und letzte Diktate (Untermenüs) | Bühne, Verlauf | Einstellungen …, Einrichtung … (Auslassungszeichen: öffnet ein Fenster mit weiteren Eingaben) | „WhisperLoom beenden“. Kurze Labels.
