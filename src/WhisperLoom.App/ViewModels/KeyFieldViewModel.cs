using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.ViewModels;

/// <summary>
/// Ein Key-Feld (UX-2, zielstruktur.md „Key-Feld“): Zustand als Satz mit Symbol („Key gespeichert“ / „Kein Key gespeichert“), nie
/// der Key, kein Aufdecken. Eine Hauptaktion je Zustand („Verbindung testen“ bzw. „Key eingeben …“), Nebenaktionen „Ersetzen …“
/// und „Löschen“. Löschen bietet „Rückgängig“ an, bis zur nächsten Aktion: dafür bleibt der gelöschte Key so lange im Speicher
/// (nur in diesem Objekt, nie angezeigt oder geloggt) und wird mit der nächsten Aktion vergessen.
/// </summary>
public sealed partial class KeyFieldViewModel : ObservableObject
{
    private readonly ISecretStore _store;
    private readonly IAccessChecker _checker;
    private readonly Func<(string ProviderId, string BaseUrl)> _target;
    private string? _deleted;

    public KeyFieldViewModel(string name, ISecretStore store, IAccessChecker checker, Func<(string ProviderId, string BaseUrl)> target)
    {
        Name = name;
        _store = store;
        _checker = checker;
        _target = target;
        Loc.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(StateText));
    }

    /// <summary>Name im <see cref="ISecretStore"/> (<see cref="SecretNames"/>).</summary>
    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(CanDelete), nameof(CanReplace), nameof(ShowCheck), nameof(ShowEnter))]
    private bool _isSet;

    /// <summary>false = Key von IT festgelegt (Richtlinie): Ersetzen und Löschen ausgegraut, Prüfen bleibt.</summary>
    public bool Editable { get; init; } = true;

    /// <summary>Richtlinie für „Verbindung testen“ (<see cref="PolicySettings.AllowsTarget"/>).</summary>
    public PolicySettings Policy { get; init; } = PolicySettings.None;

    public bool CanDelete => IsSet && Editable;

    /// <summary>„Ersetzen …“ nur, wenn ein Key da ist (sonst ist „Key eingeben …“ die Hauptaktion).</summary>
    public bool CanReplace => IsSet && Editable;

    /// <summary>Hauptaktion bei gespeichertem Key: „Verbindung testen“ (Sekundärknopf).</summary>
    public bool ShowCheck => IsSet;

    /// <summary>Hauptaktion ohne Key: „Key eingeben …“ (Hauptknopf), solange die Eingabe nicht offen ist.</summary>
    public bool ShowEnter => !IsSet && !IsReplacing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnter))]
    private bool _isReplacing;

    /// <summary>Eingabe beim Ersetzen; wird nach dem Speichern oder Abbrechen geleert.</summary>
    [ObservableProperty]
    private string _newValue = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = "";

    /// <summary>„Rückgängig“ nach dem Löschen anbieten.</summary>
    [ObservableProperty]
    private bool _canUndo;

    public bool HasStatus => Status.Length > 0;

    /// <summary>Zustand als Satz; enthält nie den Key oder Teile davon.</summary>
    public string StateText => Loc.Instance[IsSet ? "Key_State_Set" : "Key_State_None"];

    public async Task LoadAsync()
    {
        try
        {
            IsSet = await _store.GetAsync(Name, CancellationToken.None) is not null;
        }
        catch (IOException)
        {
            Status = Loc.Instance["Key_StoreError"];
        }
    }

    [RelayCommand]
    private void Replace()
    {
        Forget();
        Status = "";
        NewValue = "";
        IsReplacing = true;
    }

    [RelayCommand]
    private async Task SaveKey()
    {
        Forget();
        var value = NewValue.Trim();
        NewValue = "";
        IsReplacing = false;
        if (value.Length > 0 && await StoreAsync(value))
        {
            Status = Loc.Instance["Key_Saved"];
        }
    }

    [RelayCommand]
    private void CancelReplace()
    {
        NewValue = "";
        IsReplacing = false;
    }

    [RelayCommand]
    private async Task Delete()
    {
        Forget();
        try
        {
            var current = await _store.GetAsync(Name, CancellationToken.None);
            await _store.DeleteAsync(Name, CancellationToken.None);
            IsSet = false;
            Status = Loc.Instance["Key_Deleted"];
            _deleted = current;
            CanUndo = current is not null;
        }
        catch (IOException)
        {
            Status = Loc.Instance["Key_StoreError"];
        }
    }

    /// <summary>Gelöschten Key zurücklegen (nur direkt nach „Löschen“).</summary>
    [RelayCommand]
    private async Task Undo()
    {
        var value = _deleted;
        Forget();
        if (value is not null && await StoreAsync(value))
        {
            Status = Loc.Instance["Key_Restored"];
        }
    }

    /// <summary>Rückgängig verfällt: gelöschten Key vergessen.</summary>
    private void Forget()
    {
        _deleted = null;
        CanUndo = false;
    }

    private async Task<bool> StoreAsync(string value)
    {
        try
        {
            if (_store is Management.CompanySecretStore company)
            {
                // Unter Firmen-Verwaltung gilt der Key für das Ziel, das der Nutzer gerade sieht (W2-FIX K-1).
                await company.SetAsync(Name, value, _target(), CancellationToken.None);
            }
            else
            {
                await _store.SetAsync(Name, value, CancellationToken.None);
            }
            IsSet = true;
            return true;
        }
        catch (IOException)
        {
            Status = Loc.Instance["Key_StoreError"];
            return false;
        }
    }

    [RelayCommand]
    private async Task Check()
    {
        Forget();
        Status = Loc.Instance["Access_Checking"];
        string? key;
        try
        {
            key = await _store.GetAsync(Name, CancellationToken.None);
        }
        catch (IOException)
        {
            Status = Loc.Instance["Key_StoreError"];
            return;
        }
        var (providerId, baseUrl) = _target();
        // Freigabeliste der IT gilt auch hier: sonst ginge der Key an eine eingetippte, nie gespeicherte Adresse.
        if (!Policy.AllowsTarget(providerId, baseUrl))
        {
            Status = Loc.Instance["Access_Policy"];
            return;
        }
        // W2: Ein Eintrag (custom:/local:) bekommt nur seinen eigenen Key, nie den Key eines Zugangs.
        if (providerId.Contains(':', StringComparison.Ordinal) && Name != SecretNames.ForProvider(providerId))
        {
            key = null;
        }
        var result = await _checker.CheckAsync(providerId, baseUrl, key, CancellationToken.None);
        Status = result.StatusCode is { } code
            ? $"{Loc.Instance[result.MessageKey]} (HTTP {code})"
            : Loc.Instance[result.MessageKey];
    }
}
