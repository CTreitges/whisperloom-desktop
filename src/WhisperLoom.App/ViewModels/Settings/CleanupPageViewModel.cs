using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>Seite „Bereinigung“: Regeln ohne KI (Füllwörter, Großschreibung, Leerzeichen) – bisher nur per Datei.</summary>
public sealed class CleanupPageViewModel(SettingsViewModel session) : SettingsPageViewModel("cleanup", "Edit", "Teal", session)
{
    private PolishSettings Polish => Session.Working.Polish;

    public bool RemoveFillers { get => Polish.RemoveFillers; set => Write(() => Polish.RemoveFillers = value); }

    public bool AutoCapitalize { get => Polish.AutoCapitalize; set => Write(() => Polish.AutoCapitalize = value); }

    public bool TrailingSpace { get => Polish.TrailingSpace; set => Write(() => Polish.TrailingSpace = value); }

    /// <summary>Eigene Füllwörter, durch Komma getrennt; gespeichert klein geschrieben, ohne Duplikate.</summary>
    public string CustomFillers { get => string.Join(", ", Polish.CustomFillers); set => Write(() => Polish.CustomFillers = Words(value)); }

    /// <summary>Eingebaute Füllwörter, die stehen bleiben.</summary>
    public string DisabledFillers { get => string.Join(", ", Polish.DisabledFillers); set => Write(() => Polish.DisabledFillers = Words(value)); }

    internal static List<string> Words(string text) =>
        [.. text.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.ToLowerInvariant()).Distinct()];

    private void Write(Action write)
    {
        write();
        Session.Touch();
    }
}
