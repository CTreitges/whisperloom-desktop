using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Features.History;

/// <summary>Texte des Verlaufs nach UX-3 (mockups/verlauf.html): Trefferzahl, Rückfrage mit Anzahl, Leerzustand.</summary>
public sealed partial class HistoryViewModel
{
    /// <summary>Ansage unter der Suche: „3 Treffer“ bzw. „Keine Treffer für „x“.“; leer ohne Suche.</summary>
    public string SearchSummary =>
        Search.Trim().Length == 0 || _all.Count == 0 ? ""
        : MatchCount == 0 ? Loc.Instance.Format("History_NoMatchesFor", Search.Trim())
        : Loc.Instance.Format(MatchCount == 1 ? "History_MatchOne" : "History_Matches", MatchCount);

    /// <summary>Echte Treffer; ein stehen gebliebener Entwurf zählt nicht mit.</summary>
    private int MatchCount => Rows.Count(r => HistoryRow.Matches(r.Entry, Search));

    /// <summary>Rückfrage vor „Verlauf löschen“ mit Anzahl (Befund 7): „Alle 4 Diktate löschen? …“.</summary>
    public string ClearQuestion => Loc.Instance.Format(_all.Count == 1 ? "History_ClearConfirmOne" : "History_ClearConfirm", _all.Count);

    /// <summary>
    /// Leerzustand: Satz mit den Tastenkürzeln als Tastenkappen (Windows), mit Tray-Menü und Bühne (Windows ohne Kürzel, leere
    /// Liste) bzw. mit dem Desktop-Kürzel (Linux, null).
    /// </summary>
    public string EmptyTemplate => Loc.Instance[actions.StartKeys switch
    {
        { Count: >= 2 } => "History_EmptyHint",
        not null => "History_EmptyHintNoKeys",
        null => "History_EmptyHintLinux",
    }];

    public IReadOnlyList<string> EmptyKeys => actions.StartKeys ?? [];

    /// <summary>Werkzeugleiste nur mit gewähltem Eintrag; sonst steht rechts der Leerzustand bzw. „keine Treffer“.</summary>
    public bool ShowToolbar => HasSelection;

    public bool HasStatus => Status.Length > 0;

    /// <summary>Fuß rechts: Statuszeile und/oder Werkzeugleiste.</summary>
    public bool ShowFoot => HasStatus || ShowToolbar;

    /// <summary>Überschrift rechts: Datum des Eintrags, sonst „Noch keine Diktate“ bzw. „Keine Treffer“.</summary>
    public string HeaderText =>
        Detail?.Header ?? Loc.Instance[IsEmpty ? "History_Empty" : NoMatches ? "History_NoMatches" : "History_NoSelection"];

    [RelayCommand]
    public void ClearSearch() => Search = "";

    partial void OnStatusChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(ShowFoot));
    }

    partial void OnDetailChanged(HistoryDetailViewModel? value) => OnPropertyChanged(nameof(HeaderText));

    /// <summary>Sprachwechsel bei offenem Fenster (das Fenster meldet ihn und löst das Abo beim Schließen).</summary>
    public void RefreshTexts()
    {
        RaiseListTexts();
        OnPropertyChanged(nameof(EmptyTemplate));
    }

    private void RaiseListTexts()
    {
        OnPropertyChanged(nameof(SearchSummary));
        OnPropertyChanged(nameof(ClearQuestion));
        OnPropertyChanged(nameof(ShowToolbar));
        OnPropertyChanged(nameof(ShowFoot));
        OnPropertyChanged(nameof(HeaderText));
    }
}
