using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>
/// Basis einer Einstellungsseite: Id (= Seite im <see cref="SettingsCatalog"/>), Titel, Symbol, Einleitungssatz und die
/// gemeinsame Arbeitskopie <see cref="Session"/>. Jede Seite hat genau eine View <c>Views/Settings/&lt;Seite&gt;Page.axaml</c>.
/// </summary>
public abstract class SettingsPageViewModel : ObservableObject, IDisposable
{
    protected SettingsPageViewModel(string id, string icon, SettingsViewModel session)
    {
        Id = id;
        Icon = "Loom.Icon." + icon;
        Session = session;
        GoToCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<string>(page =>
        {
            if (page is not null)
            {
                Navigate?.Invoke(page);
            }
        });
        Loc.Instance.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>Sprung zu einer anderen Seite („Zur Erkennung“, „Zur Firma“); setzt die Hülle.</summary>
    internal Action<string>? Navigate { get; set; }

    public CommunityToolkit.Mvvm.Input.IRelayCommand<string> GoToCommand { get; }

    public string Id { get; }

    public string TitleKey => "Page_" + Id;

    public string Title => Loc.Instance[TitleKey];

    /// <summary>Lokalisierungs-Schlüssel des Einleitungssatzes unter dem Titel (<c>PageIntro_&lt;Seite&gt;</c>).</summary>
    public string IntroKey => "PageIntro_" + Id;

    /// <summary>Konto-Eintrag am Ende der Seitenleiste (Firma), abgesetzt von den Aufgaben-Seiten.</summary>
    public bool IsAccount => Id == SettingsCatalog.AccountPage;

    /// <summary>Ressourcen-Schlüssel des Symbols.</summary>
    public string Icon { get; }

    public SettingsViewModel Session { get; }

    public virtual void Dispose()
    {
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }

    protected virtual void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(Title));

    /// <summary>Zahl in einem Textfeld: gültig → schreiben und speichern, sonst nur Fehler am Pfad.</summary>
    protected void SetNumber(string path, string text, int min, int max, bool zeroAllowed, Action<int> write)
    {
        var error = SettingsValidation.Integer(text, min, max, zeroAllowed, out var value);
        Session.Errors.Set(path, error);
        if (error is null)
        {
            write(value);
            Session.Touch();
        }
    }

    /// <summary>Optionen der Modellwahl: Standard-Eintrag, dann die Modelle aus providers.json.</summary>
    protected static IEnumerable<Choice> ModelChoices(IReadOnlyList<ModelOption> models, string defaultLabel) =>
        [new Choice("", defaultLabel), .. models.Select(m => new Choice(m.Id, m.Label))];

    /// <summary>„Empfehlung (X)“ bzw. „Standard des Anbieters“ ohne Katalog-Modelle.</summary>
    protected static string Recommended(IReadOnlyList<ModelOption> models) =>
        models.Count > 0 ? Loc.Instance.Format("Model_Recommended", models[0].Label) : Loc.Instance["Model_ProviderDefault"];

    protected static CustomChoiceViewModel Picker(IEnumerable<Choice> options, Func<string> read, Action<string> write) =>
        new(options, read, v =>
        {
            write(v);
            return null;
        }, "Model_Custom");

    protected static bool NeedsUrl(string providerId) => ProviderCatalog.Find(providerId)?.NeedsUrl == true;
}
