# Desktop-Ausnahmen von den UI/UX-Richtlinien

Die Richtlinien (Skill ui-ux-guidelines) sind für Web und App geschrieben. WhisperLoom Desktop ist eine Hintergrund-App für
Windows und Linux, bedient mit Maus und Tastatur. Wo eine Regel dort nicht passt, steht hier die Abweichung mit Grund. Alles
andere gilt unverändert, mindestens WCAG 2.2 AA. Entschieden im UX-Lauf 3 (Zielstruktur §10, Freigabe 2026-11-03).

Die Ux-Tests (`tests/WhisperLoom.App.Tests/Ux/`) prüfen Klickziele gegen `UxRules.MinTarget` = 24 (D2). Die Mindestgröße
720 × 480 (D3) steht im Einstellungsfenster als `MinWidth`/`MinHeight`.

| # | Richtlinie | Ausnahme | Grund |
|---|---|---|---|
| D1 | Fließtext 16 px | Fließtext 14 px (`Loom.Font.Body`) | Windows-Standard für Desktop-Programme; wächst mit der Textgröße des Systems bis 225 % (`TextScale`). |
| D2 | Klickziele 44 × 44 px | Mindestens 24 × 24 px (WCAG 2.5.8 AA), Hauptknöpfe 32 hoch | Bedienung mit Maus und Tastatur; keine Touch-Bedienung. |
| D3 | Layout ab 320 px Breite (WCAG 1.4.10) | Mindestgröße der Fenster 720 × 480 (Einstellungen), darunter Umbruch statt Abschneiden | Fenster statt Viewport; bei 200 % Text stapeln Zeilen, nichts rollt waagrecht. |
| D4 | Test auf dem Telefon | Entfällt | Keine Telefon-Version. |
| D5 | Jedes Bedienelement per Tab erreichbar | Overlay, Markierungs-Knopf und Stufen-Wähler nehmen keinen Fokus | Sie schweben über dem Programm, in das diktiert wird, und dürfen ihm den Fokus nicht nehmen. Bedienung über Tastenkürzel; die Tastenwege stehen als Tastenkappen im Fenster. |
| D6 | Landmarks des Webs (`main`, `nav`, …) | Benannte Fenster, Listen mit Namen, Überschriften mit Ebene | Desktop-Bedienungshilfen (UI Automation, AT-SPI) kennen keine Landmarks. |
| D7 | Ein Farbsystem für alle Ansichten | Bühne folgt dem System, mit Schalter „Dunkel für Beamer“ (Strg+D) | Bei Vorführungen über einen Beamer liest sich helle Schrift auf Dunkel besser; der Schalter kostet nichts. Keine Ausnahme vom Farbsystem, nur eine zusätzliche Wahl. |
| D8 | Felder mit sichtbarer Beschriftung statt Platzhalter | Keine Ausnahme | Suchfelder bekommen ein sichtbares Label über dem Feld und eine Lupe; der Platzhalter beschriftet nichts. |
