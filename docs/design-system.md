# Designsystem (W2-UI1)

Gemeinsame Tokens, Styles und Bausteine für Fenster nach Apple-HIG (Vorbild: macOS-Systemeinstellungen). Ab W2-UI1 nutzt
das Einstellungsfenster sie; Verlauf, Overlay und Einrichtung (W2-OUT, W2-ENR) können sie übernehmen.

Dateien: `src/WhisperLoom.App/Design/` – `Tokens.axaml` (Farben, Abstände, Schrift, Symbole), `Controls.axaml`
(Vorlagen der Bausteine), `Styles.axaml` (Klassen), dazu die Bausteine als C#-Klassen. Eingebunden in `App.axaml`.

## Hell/Dunkel
- Die App steht global auf Dunkel (Bühne, Overlay, Verlauf bleiben wie bisher).
- Ein Fenster mit Designsystem folgt dem System: im Konstruktor `SystemTheme.Follow(this, Application.Current?.PlatformSettings)` und `Classes="loom"` am Fenster. Ein Wechsel im System gilt sofort.
- Farben nur als `{DynamicResource Loom.*}`, nie als feste Werte oder `StaticResource` – sonst wechselt die Farbe nicht mit.

## Farb-Tokens
Quelle: Loom-Palette der Android-App (`ui/theme/Color.kt`, `LoomPalette`). Dunkel = Android-Rollen 1:1, hell = dieselben Töne mit getauschten Rollen (dunkle Töne als Text/Akzent, helle als Fläche). Weiß und `#EEF1F5` sind die einzigen Zusätze.

| Token | Dunkel | Hell | Einsatz |
|---|---|---|---|
| `Loom.Background` | `#0E1116` background | `#EEF1F5` | Fenster |
| `Loom.Sidebar` | `#161A20` surfaceContainerLow | `#E4E8EE` onSurface | Seitenleiste |
| `Loom.Surface` | `#1A1F26` surfaceContainer | `#FFFFFF` | Gruppen |
| `Loom.SurfaceHigh` | `#242A32` surfaceContainerHigh | `#E4E8EE` | Hinweis-Zeile, Kombi-Felder |
| `Loom.Separator` | `#3A424D` outlineVariant | `#B8C0CC` onSurfaceVariant | Linien |
| `Loom.Text` | `#E4E8EE` onSurface | `#0E1116` background | Text |
| `Loom.TextSecondary` | `#B8C0CC` onSurfaceVariant | `#3A424D` outlineVariant | Beschreibungen, Gruppentitel |
| `Loom.Accent` / `Loom.OnAccent` | `#6FD9C7` / `#00382F` | `#0F5B50` / `#FFFFFF` | Links, Hauptaktion |
| `Loom.Selection` / `Loom.OnSelection` | `#0F5B50` / `#B4F2E6` | `#B4F2E6` / `#00382F` | gewählte Seite |
| `Loom.Error` | `#FFB4AB` error | `#93000A` errorContainer | Inline-Fehler, Warnungen |
| `Loom.Managed` / `Loom.OnManaged` | `#2B4665` / `#D8E7FA` | `#D8E7FA` / `#12304F` | Verwaltet-Zeile, Etikett „Firmenvorgabe“ |
| `Loom.Tile.*` | Container-Töne | gleich | Symbol-Kacheln (weiße Glyphe) |

Kontrast: Text auf Fläche ≥ 4,5:1 in beiden Varianten, geprüft von `DesignTokensTest` (WCAG 2.x, relative Leuchtdichte).
FluentTheme-Akzent (Häkchen, Schalter, Fokus) = `#0F5B50`.

## Abstände, Radien, Schrift
- Raster 4: `Loom.Space.XS/S/M/L/XL` = 4/8/12/16/24. Zeilen-Innenabstand 14×10, Gruppenabstand 20.
- Radien: `Loom.Radius.Group` 10 (Gruppen), `Loom.Radius.Control` 6 (Steuerelemente, Kacheln).
- Schrift (Systemschrift bzw. Inter): `Loom.Font.PageTitle` 22 halbfett, `GroupTitle` 13 halbfett, `Body` 14, `Caption` 12.

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
