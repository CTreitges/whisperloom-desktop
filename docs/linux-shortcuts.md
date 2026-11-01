# Linux: Tastenkürzel für WhisperLoom

Unter Linux bekommt eine App keine globalen Tastenkürzel – unter Wayland grundsätzlich nicht, und unter X11 bietet
WhisperLoom sie (noch) nicht an. Stattdessen legst du in deiner Desktop-Umgebung ein Kürzel auf einen Befehl. Das
funktioniert unter **Wayland und X11** gleich, weil die Desktop-Umgebung das Kürzel abfängt, nicht die App.

*English version below.*

## Der Befehl

```bash
"/pfad/zu/WhisperLoom" toggle
```

- Den genauen Pfad zeigt der Einrichtungs-Assistent im Schritt **Tastenkürzel** (zum Kopieren). Beim AppImage ist es
  der Pfad der `.AppImage`-Datei, beim tar.gz die Datei `WhisperLoom` im entpackten Ordner.
- `toggle` startet eine Aufnahme bzw. beendet sie und verarbeitet sie. Läuft WhisperLoom noch nicht, startet der
  Befehl die App gleich mit.
- Das Ergebnis liegt danach in der **Zwischenablage** – mit Strg+V einfügen. (Direktes Einfügen gibt es unter Linux
  noch nicht.)

Weitere Befehle für eigene Kürzel:

| Befehl | Wirkung |
|---|---|
| `toggle` | Aufnahme starten bzw. stoppen und verarbeiten |
| `start` / `stop` | nur starten bzw. nur stoppen |
| `cancel` | Aufnahme verwerfen |
| `pause` / `resume` | Pause bzw. weiter |
| `stage next` | nächste Stufe (z. B. Roh → Verschönern → Prompt) |
| `stage prompt` | feste Stufe: `off`, `polish`, `beautify`, `summarize`, `readable`, `prompt` |
| `show` | Bühnenansicht öffnen |
| `setup` | Einrichtungs-Assistent erneut öffnen |
| `status --json` | Zustand ausgeben (für Skripte) |
| `process-selection` | markierten Text verarbeiten: Stufen-Wähler als kleines Fenster, Ergebnis in die Zwischenablage |
| `process-selection --stage beautify` | markierten Text sofort mit dieser Stufe verarbeiten (`polish`, `beautify`, `summarize`, `prompt`) |

„Halten zum Sprechen“ geht über Kürzel der Desktop-Umgebung nicht, weil sie nur beim Drücken auslösen. Nimm
`toggle`: einmal drücken = Aufnahme, nochmal drücken = fertig.

## KDE Plasma (6)

1. **Systemeinstellungen → Tastatur → Kurzbefehle**.
2. Unten **Neu hinzufügen → Befehl oder Skript …**.
3. Befehl: `"/pfad/zu/WhisperLoom" toggle` – Name z. B. „WhisperLoom Diktat“.
4. Kürzel setzen, z. B. **Meta+Leertaste** oder **Strg+Alt+D**, dann **Anwenden**.

Plasma 5: Systemeinstellungen → Kurzbefehle → Eigene Kurzbefehle → Bearbeiten → Neu → Globales Kürzel → Befehl/Adresse.

## GNOME (45+)

1. **Einstellungen → Tastatur → Tastaturkürzel anzeigen und anpassen**.
2. Ganz unten **Eigene Tastenkombinationen → +**.
3. Name „WhisperLoom Diktat“, Befehl `"/pfad/zu/WhisperLoom" toggle`, Kürzel festlegen, **Hinzufügen**.

Per Kommandozeile (überschreibt die Liste eigener Kürzel – vorher mit `gsettings get …custom-keybindings` prüfen
und vorhandene Einträge in die Liste übernehmen):

```bash
P=/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/whisperloom/
gsettings set org.gnome.settings-daemon.plugins.media-keys custom-keybindings "['$P']"
gsettings set org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:$P name 'WhisperLoom Diktat'
gsettings set org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:$P command '"/pfad/zu/WhisperLoom" toggle'
gsettings set org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:$P binding '<Control><Alt>d'
```

Tray unter GNOME: Das Symbol erscheint nur mit der Erweiterung „AppIndicator and KStatusNotifierItem Support“.
Ohne Tray öffnet WhisperLoom beim Start die Bühnenansicht; die Kürzel funktionieren trotzdem.

## Andere Umgebungen

- **XFCE**: Einstellungen → Tastatur → Tastenkürzel für Anwendungen → Hinzufügen.
- **Sway / i3**: `bindsym Ctrl+Alt+d exec "/pfad/zu/WhisperLoom" toggle`
- **Hyprland**: `bind = CTRL ALT, D, exec, "/pfad/zu/WhisperLoom" toggle`

## Markierten Text verarbeiten

Text in einem beliebigen Programm mit der Maus markieren (nicht kopieren nötig), dann ein Kürzel auf

```bash
"/pfad/zu/WhisperLoom" process-selection
```

drücken. WhisperLoom liest die **Primärauswahl** (das zuletzt Markierte), zeigt die Stufen zur Wahl (Klick, ↑/↓, Tab,
1–9, Enter; Esc bricht ab) und legt das Ergebnis in die **Zwischenablage** – mit Strg+V über die Markierung einfügen.
Mit `--stage <stufe>` entfällt die Wahl, z. B. ein zweites Kürzel nur für „Verschönern“. Der Eintrag landet im Verlauf
mit Quelle „Markierung“.

- Wayland: `wl-paste` aus `wl-clipboard` nötig; X11: `xclip` (oder `xsel`). Fehlt das Werkzeug, meldet WhisperLoom
  per Benachrichtigung, was zu installieren ist.
- Ist nichts markiert, kommt der Hinweis „Nichts markiert“.
- „Nach Anweisung umschreiben“ und direktes Ersetzen der Markierung gibt es nur unter Windows.

## Wayland oder X11?

| | Wayland | X11 |
|---|---|---|
| Kürzel der Desktop-Umgebung → `whisperloom toggle` | ja | ja |
| Globale Kürzel direkt in der App | nein (Wayland erlaubt es nicht) | noch nicht |
| Zwischenablage | `wl-clipboard` (`wl-copy`) nötig | `xclip` nötig |
| Markierung lesen (`process-selection`) | `wl-paste --primary` (`wl-clipboard`) | `xclip` oder `xsel` |
| Direkt einfügen/tippen | nein (später: wtype/ydotool) | nein |

Welche Sitzung läuft: `echo $XDG_SESSION_TYPE` (`wayland` oder `x11`).

## Probleme

- **Kürzel tut nichts**: Befehl im Terminal ausführen. Fehlt die Datei, Pfad prüfen; Pfade mit Leerzeichen in
  Anführungszeichen setzen.
- **Nichts in der Zwischenablage (Wayland)**: `wl-clipboard` installieren (`sudo pacman -S wl-clipboard` bzw.
  `sudo apt install wl-clipboard`).
- **Kürzel kollidiert**: Meta+Leertaste wechselt in manchen Setups das Tastaturlayout – anderes Kürzel wählen.
- **Proxy** für Kürzel und Autostart: Umgebungsvariablen gehören in `~/.config/environment.d/` (`docs/proxy.md`).

---

# Linux: keyboard shortcuts for WhisperLoom (English)

Apps get no global shortcuts on Linux (never on Wayland; WhisperLoom does not offer them on X11 yet). Bind a shortcut in
your desktop environment to a command instead. This works the same on **Wayland and X11**.

**Command:** `"/path/to/WhisperLoom" toggle` – the setup wizard shows the exact path (step *Shortcuts*). For an AppImage
use the `.AppImage` file. `toggle` starts or stops-and-processes a recording and starts the app if it is not running. The
result is on the **clipboard** (paste with Ctrl+V). Other verbs: `start`, `stop`, `cancel`, `pause`, `resume`,
`stage next`, `stage <off|polish|beautify|summarize|readable|prompt>`, `show`, `setup`, `status --json`. Push-to-talk is
not possible with desktop shortcuts; use `toggle`.

**Process selected text:** select text anywhere, then run `"/path/to/WhisperLoom" process-selection` from a shortcut.
WhisperLoom reads the primary selection, shows the stage picker as a small window (click, ↑/↓, Tab, 1–9, Enter; Esc
cancels) and puts the result on the clipboard. `--stage <stage>` skips the picker. Needs `wl-paste` (`wl-clipboard`) on
Wayland and `xclip` or `xsel` on X11; a notification names a missing tool.

- **KDE Plasma 6:** System Settings → Keyboard → Shortcuts → *Add New → Command or Script…* → command as above → set a
  key (e.g. Meta+Space or Ctrl+Alt+D) → Apply.
- **GNOME 45+:** Settings → Keyboard → *View and Customize Shortcuts* → *Custom Shortcuts* → **+** → name, command,
  shortcut → Add. The `gsettings` commands above do the same from a terminal (they replace the custom shortcut list).
  The tray icon needs the “AppIndicator and KStatusNotifierItem Support” extension.
- **XFCE:** Settings → Keyboard → Application Shortcuts. **Sway/i3:** `bindsym Ctrl+Alt+d exec "/path/to/WhisperLoom" toggle`.
  **Hyprland:** `bind = CTRL ALT, D, exec, "/path/to/WhisperLoom" toggle`.
- Clipboard needs `wl-clipboard` on Wayland and `xclip` on X11. Check your session with `echo $XDG_SESSION_TYPE`.
