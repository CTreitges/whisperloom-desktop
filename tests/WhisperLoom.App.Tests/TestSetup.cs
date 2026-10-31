// Loc.Instance (UI-Sprache) ist global: UI-Tests verschiedener Klassen dürfen nicht verzahnt laufen, sonst stellt einer
// die Sprache um, während ein anderer an einem await hängt (CI windows: UiTest sah englische Knöpfe). Die Suite
// braucht nur Sekunden, sequentiell kostet also nichts.
// Doku: xunit.net/docs/running-tests-in-parallel (abgerufen 2026-10-31).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
