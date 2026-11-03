using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>
/// Ein eigener Anbieter (<c>providers.custom</c>): Name, Protokoll, Adresse, Modelle, Key optional, eigener Key (geht nur an
/// die Adresse dieses Eintrags) und „Zugang prüfen“. Übernommen wird der Eintrag erst mit gültiger Adresse.
/// </summary>
public sealed partial class CustomProviderRow : ObservableObject
{
    private readonly Action _changed;
    private readonly Func<CustomProviderRow, string?> _inUse;
    private readonly Func<CustomProviderRow, Task> _delete;

    /// <param name="inUse">Fehlertext, wenn ein Zugang den Eintrag nutzt; null = löschbar.</param>
    public CustomProviderRow(CustomProvider entry, SettingsViewModel session, Action changed, Func<CustomProviderRow, string?> inUse, Func<CustomProviderRow, Task> delete)
    {
        Entry = entry;
        _changed = changed;
        _inUse = inUse;
        _delete = delete;
        Protocols = [.. CustomProvider.Protocols.Select(p => new Choice(p, Loc.Instance["Protocol_" + p], Loc.Instance["Protocol_" + p + "_Desc"]))];
        _protocol = SettingsViewModel.Pick(Protocols, entry.Protocol);
        Key = new KeyFieldViewModel(SecretNames.ForProvider(Id), session.SecretStore, session.Checker, () => (Id, Entry.BaseUrl)) { Policy = session.Policy };
        Key.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(KeyFieldViewModel.IsSet))
            {
                OnPropertyChanged(nameof(Summary));
            }
        };
        IsValid = UrlError(entry.BaseUrl) is null;
    }

    /// <summary>Kopf des eingeklappten Eintrags: „OpenAI-kompatibel · https://… · Key gespeichert“.</summary>
    public string Summary => string.Join(" · ", Protocol.Label, Entry.BaseUrl,
        Loc.Instance[Key.IsSet ? "Provider_KeySaved" : Entry.KeyOptional ? "Provider_NoKeyNeeded" : "Provider_NoKey"]);

    /// <summary>Beschriftung des Löschen-Knopfs mit Namen: „Anbieter „Labor“ löschen …“.</summary>
    public string DeleteLabel => Loc.Instance.Format("Providers_DeleteNamed", Entry.Name);

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Protocol) or nameof(BaseUrl) or nameof(KeyOptional))
        {
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Summary)));
        }
        else if (e.PropertyName == nameof(Name))
        {
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(DeleteLabel)));
        }
    }

    public CustomProvider Entry { get; }

    /// <summary>Anbieter-ID, wie sie ein Zugang nennt: <c>custom:&lt;id&gt;</c>.</summary>
    public string Id => ProvidersSettings.CustomPrefix + Entry.Id;

    public IReadOnlyList<Choice> Protocols { get; }

    public KeyFieldViewModel Key { get; }

    /// <summary>Adresse gültig: der Eintrag steht in den Einstellungen.</summary>
    public bool IsValid { get; private set; }

    [ObservableProperty] private Choice _protocol;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _hint = "";

    /// <summary>Rückfrage „Löschen?“ sichtbar (löscht auch den Key, HIG: destruktiv nur nach Bestätigung).</summary>
    [ObservableProperty] private bool _deletePending;

    public string Name
    {
        get => Entry.Name;
        set
        {
            if (value.Trim().Length == 0) return;
            Entry.Name = value.Trim();
            OnPropertyChanged();
            _changed();
        }
    }

    public string BaseUrl
    {
        get => Entry.BaseUrl;
        set
        {
            var url = value.Trim();
            var error = UrlError(url);
            Error = error ?? "";
            if (error is not null || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return;
            var oldHost = Uri.TryCreate(Entry.BaseUrl, UriKind.Absolute, out var old) ? old.Authority : "";
            Hint = Key.IsSet && oldHost.Length > 0 && !string.Equals(oldHost, u.Authority, StringComparison.OrdinalIgnoreCase)
                ? Loc.Instance.Format("Provider_KeyNewHost", u.Authority)
                : ServerUrlCheck.Check(url, CustomProviderSource.ToProvider(Entry)) is { Severity: ServerUrlCheck.Severity.Warning } w ? w.Message : "";
            Entry.BaseUrl = url;
            IsValid = true;
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>Modell-IDs, durch Komma oder Zeilenumbruch getrennt; leer = Liste vom Server.</summary>
    public string Models
    {
        get => string.Join(", ", Entry.Models);
        set
        {
            Entry.Models = [.. value.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal)];
            OnPropertyChanged();
            _changed();
        }
    }

    public bool KeyOptional
    {
        get => Entry.KeyOptional;
        set
        {
            Entry.KeyOptional = value;
            OnPropertyChanged();
            _changed();
        }
    }

    partial void OnProtocolChanged(Choice value)
    {
        if (value is null || Entry.Protocol == value.Value) return;
        Entry.Protocol = value.Value;
        _changed();
    }

    [RelayCommand]
    private void Delete()
    {
        var error = _inUse(this);
        Error = error ?? "";
        DeletePending = error is null;
    }

    [RelayCommand]
    private Task ConfirmDelete()
    {
        DeletePending = false;
        return _delete(this);
    }

    [RelayCommand]
    private void CancelDelete() => DeletePending = false;

    /// <summary>http(s) mit Host; http nur zu privaten Adressen (wie der Import, <see cref="ServerUrlCheck"/>).</summary>
    private string? UrlError(string url)
    {
        if (!(Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")) return ServerUrlCheck.MsgScheme;
        return ServerUrlCheck.Check(url, CustomProviderSource.ToProvider(Entry)) is { } p &&
               (p.Severity == ServerUrlCheck.Severity.Error || p.Message == ServerUrlCheck.MsgPublicHttp)
            ? p.Message
            : null;
    }
}

/// <summary>
/// Seite „Verbindungen“ (UX-2): Wege nach draußen und ins eigene Netz – eigene Anbieter (W2-LOCAL, wie „Eigener Anbieter“
/// der Android-App: anlegen, bearbeiten, löschen; in jedem Zugang wählbar, ein genutzter lässt sich nicht löschen), lokale
/// Server ohne Key und der Proxy (Adresse nur bei „Eigener Proxy“).
/// </summary>
public sealed partial class ConnectionsPageViewModel : SettingsPageViewModel
{
    public ConnectionsPageViewModel(SettingsViewModel session) : base("connections", "Connections", session)
    {
        foreach (var entry in session.Working.Providers.Custom)
        {
            Rows.Add(Row(entry));
        }
        session.PropertyChanged += OnSessionChanged;
    }

    public LocalViewModel Local => Session.Local;

    /// <summary>Proxy-Adresse nur bei „Eigener Proxy“ (nur zeigen, was gilt).</summary>
    public bool IsManual => Session.ProxyMode.Value == "manual";

    public override void Dispose()
    {
        Session.PropertyChanged -= OnSessionChanged;
        base.Dispose();
    }

    private void OnSessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.ProxyMode))
        {
            OnPropertyChanged(nameof(IsManual));
        }
    }

    public ObservableCollection<CustomProviderRow> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    [ObservableProperty] private string _error = "";

    /// <summary>
    /// Neuer Eintrag mit freier ID. Ein gespeicherter Key unter dieser ID (verwaist, z. B. nach einem Import) wird vorher
    /// gelöscht — sonst erbte ihn der neue Eintrag und schickte ihn an seine Adresse (Prüfrunde 1 K-1).
    /// </summary>
    [RelayCommand]
    private async Task Add()
    {
        var ids = Rows.Select(r => r.Entry.Id).ToHashSet(StringComparer.Ordinal);
        var n = 1;
        while (ids.Contains($"anbieter-{n}")) n++;
        var entry = new CustomProvider { Id = $"anbieter-{n}", Name = Loc.Instance.Format("Provider_NewName", n), Protocol = "openai" };
        try
        {
            await Session.SecretStore.DeleteAsync(SecretNames.ForProvider(ProvidersSettings.CustomPrefix + entry.Id), CancellationToken.None);
        }
        catch (IOException)
        {
            Error = Loc.Instance["Key_StoreError"];
            return;
        }
        Error = "";
        var row = Row(entry);
        Rows.Add(row);
        OnPropertyChanged(nameof(IsEmpty));
        await row.Key.LoadAsync();
    }

    /// <summary>Lädt die Keys der Einträge (nur „gesetzt“ bzw. „nicht gesetzt“).</summary>
    public Task LoadKeysAsync() => Task.WhenAll(Rows.Select(r => r.Key.LoadAsync()));

    private CustomProviderRow Row(CustomProvider entry) => new(entry, Session, Changed, InUse, DeleteAsync);

    /// <summary>Nur gültige Einträge kommen in die Einstellungen; dann Anbieterlisten neu und speichern.</summary>
    private void Changed()
    {
        Session.Working.Providers.Custom = [.. Rows.Where(r => r.IsValid).Select(r => r.Entry)];
        Session.RefreshProviders();
        Session.Touch();
    }

    /// <summary>Genutzt von einem Zugang (auch abgeschaltetem Fallback)? Dann erst dort umstellen.</summary>
    private string? InUse(CustomProviderRow row)
    {
        var s = Session.Working;
        var used = new[] { ("Slot_stt", s.Stt.ProviderId), ("Slot_sttFallback", s.SttFallback.ProviderId), ("Slot_llm", s.Llm.ProviderId), ("Slot_llmFallback", s.LlmFallback.ProviderId) }
            .Where(u => u.Item2 == row.Id && row.IsValid).Select(u => Loc.Instance[u.Item1]).ToList();
        return used.Count > 0 ? Loc.Instance.Format("Provider_InUse", string.Join(", ", used)) : null;
    }

    private async Task DeleteAsync(CustomProviderRow row)
    {
        if (InUse(row) is { } error)
        {
            row.Error = error;
            return;
        }
        // Erst den Key: scheitert die Ablage, bleibt der Eintrag (ein verwaister Key ginge an einen späteren Eintrag gleicher ID).
        try
        {
            await Session.SecretStore.DeleteAsync(row.Key.Name, CancellationToken.None);
        }
        catch (IOException)
        {
            row.Error = Loc.Instance["Key_StoreError"];
            return;
        }
        Rows.Remove(row);
        OnPropertyChanged(nameof(IsEmpty));
        Changed();
    }
}
