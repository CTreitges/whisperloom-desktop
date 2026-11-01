using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.ViewModels;

/// <summary>
/// Sofort speichern (W2-UI1): jede gültige Änderung überträgt nur die geänderten Pfade auf den aktuellen Stand der App
/// (<see cref="SettingsMerge"/>), damit Änderungen anderer Stellen erhalten bleiben. Commits laufen nacheinander.
/// </summary>
public sealed partial class SettingsViewModel
{
    private readonly Func<SettingsViewModel> _reopen;
    private WhisperLoomSettings _baseline;
    private IReadOnlyList<string> _policyErrorPaths = [];
    private bool _historyOffPending;

    /// <summary>Einstellungsfenster: jede gültige Änderung gilt sofort. Aus (Assistent): erst <see cref="Save"/>.</summary>
    public bool AutoSave { get; set; }

    /// <summary>Letzter laufender Commit (Tests und Schließen warten darauf).</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    /// <summary>Neue Arbeitskopie vom aktuellen Stand der App (z. B. nach „Auf Firmenvorgabe zurücksetzen“).</summary>
    public SettingsViewModel Reopen()
    {
        var fresh = _reopen();
        fresh.Company = Company;
        return fresh;
    }

    /// <summary>Firmen-Anbindung für die Seite „Firma“ (W2-ENR); null ohne Server-Anbindung (Tests).</summary>
    public Management.ManagementService? Company { get; set; }

    /// <summary>Richtlinie der App jetzt; weicht sie von <see cref="Policy"/> ab, ist die Arbeitskopie veraltet (Abgleich, Verlassen).</summary>
    internal PolicySettings LivePolicy => _controller.Policy;

    /// <summary>Verlauf an/aus. Im Fenster fragt „aus“ erst nach (<see cref="HistoryOffPending"/>), denn es löscht den Verlauf.</summary>
    public bool HistoryEnabled
    {
        get => !_historyOffPending && _s.History.Enabled;
        set
        {
            if (AutoSave && !value && _s.History.Enabled)
            {
                HistoryOffPending = true;
                return;
            }
            HistoryOffPending = false;
            SetPlain(() => _s.History.Enabled = value);
        }
    }

    /// <summary>Rückfrage „Verlauf löschen?“ sichtbar.</summary>
    public bool HistoryOffPending
    {
        get => _historyOffPending;
        private set
        {
            if (SetProperty(ref _historyOffPending, value))
            {
                OnPropertyChanged(nameof(HistoryEnabled));
            }
        }
    }

    [RelayCommand]
    private void ConfirmHistoryOff()
    {
        HistoryOffPending = false;
        SetPlain(() => _s.History.Enabled = false);
        OnPropertyChanged(nameof(HistoryEnabled));
    }

    [RelayCommand]
    private void CancelHistoryOff() => HistoryOffPending = false;

    /// <summary>Aktueller Stand der App (nicht die Arbeitskopie).</summary>
    internal WhisperLoomSettings Current => _controller.Settings;

    /// <summary>„Auf Firmenvorgabe zurücksetzen“ (Quelle: <see cref="Design.IManagedSource.Reset"/>) und sofort speichern.</summary>
    public async Task ResetToDefaultAsync(string path, Design.IManagedSource source)
    {
        await Pending;
        var before = _controller.Settings;
        var after = source.Reset(before, path);
        // W2-FIX (Prüfer-Befund 1): Die Vorgabe kann ein anderes Ziel haben (Host, eigener Anbieter gleicher ID, Proxy). Eigene
        // Keys, die danach neu dorthin gingen, verwerfen wie beim Import (K1).
        var stored = await Cli.SyncCommands.StoredKeysAsync(_secrets, CancellationToken.None, before,
            after.Providers.Custom.Select(c => ProvidersSettings.CustomPrefix + c.Id));
        foreach (var drop in KeyRoutes.Dropped(before, after, stored))
        {
            await _secrets.DeleteAsync(drop.Name, CancellationToken.None);
        }
        await ApplyAsync(after);
    }

    /// <summary>Gültige Änderung an der Kopie: im Fenster sofort speichern.</summary>
    internal void Touch()
    {
        if (!_ready || !AutoSave)
        {
            return;
        }
        var previous = Pending;
        Pending = Run();

        async Task Run()
        {
            await previous;
            await CommitAsync();
        }
    }

    /// <summary>Schreibt ohne Prüfung, meldet die Eigenschaft und speichert.</summary>
    private void SetPlain(Action write, [CallerMemberName] string? property = null)
    {
        write();
        OnPropertyChanged(property);
        Touch();
    }

    /// <summary>Gültig → schreiben und speichern; ungültig → nur Fehler am Pfad, die Kopie bleibt (Eingabe bleibt stehen).</summary>
    private void SetChecked(string path, string value, Func<string, string?> check, Action<string> write, [CallerMemberName] string? property = null)
    {
        var error = check(value);
        Errors.Set(path, error);
        if (error is null)
        {
            write(value);
            OnPropertyChanged(property);
            Touch();
        }
    }

    private async Task CommitAsync()
    {
        var candidate = BuildResult();
        var changed = SettingsMerge.Changed(_baseline, candidate);
        if (changed.Count == 0)
        {
            return;
        }
        if (await ApplyAsync(SettingsMerge.Apply(_controller.Settings, candidate, changed)))
        {
            _baseline = candidate;
        }
    }

    /// <summary>Auswahlen und Unterseiten in die Kopie schreiben; Ergebnis ist eine eigenständige Kopie.</summary>
    private WhisperLoomSettings BuildResult()
    {
        // Während RefreshProviders kann die Auswahl kurz null sein: dann gilt der Stand der Kopie.
        _s.Stt.ProviderId = SttProvider?.Value ?? _s.Stt.ProviderId;
        _s.SttFallback.ProviderId = SttFallbackProvider?.Value ?? _s.SttFallback.ProviderId;
        _s.Llm.ProviderId = LlmProvider?.Value ?? _s.Llm.ProviderId;
        _s.LlmFallback.ProviderId = LlmFallbackProvider?.Value ?? _s.LlmFallback.ProviderId;
        _s.Language = Language.Value;
        _s.Refine.Stage = Stage.Value;
        _s.History.Size = HistorySize;
        _desktop.ProxyMode = ProxyMode.Value;
        _desktop.Output = Output.Value;
        _desktop.Microphone = Microphone.Value;
        _desktop.UiLanguage = UiLanguage.Value;
        _desktop.InsertMode = InsertMode.Value;
        Hotkeys.Apply();
        Profiles.Apply();
        SettingsStore.SetDesktop(_s, _desktop);
        Local.Apply();
        return SettingsJson.Deserialize(SettingsJson.Serialize(_s));
    }

    /// <summary>Wie ein Import: gesperrte Felder bleiben, fester Verlauf gilt, nicht freigegebene Anbieter werden abgelehnt.</summary>
    private async Task<bool> ApplyAsync(WhisperLoomSettings result)
    {
        if (_policy.IsActive)
        {
            var preview = SyncImport.ApplyPolicy(_controller.Settings, result, _policy);
            ShowPolicyErrors(preview.Errors);
            if (!preview.CanApply)
            {
                SaveStatus = string.Join(Environment.NewLine, preview.Errors);
                return false;
            }
            result = preview.Settings!;
        }
        // Nur bei geänderter Oberflächensprache umschalten: sonst setzte jeder Sofort-Commit „Wie System“ neu auf die OS-Sprache.
        var uiLanguage = SettingsStore.Desktop(result).UiLanguage;
        if (uiLanguage != SettingsStore.Desktop(_controller.Settings).UiLanguage)
        {
            Loc.Instance.SetLanguage(uiLanguage);
        }
        try
        {
            await _controller.ApplySettingsAsync(result);
            SaveStatus = Loc.Instance["Settings_Saved"];
        }
        catch (IOException)
        {
            SaveStatus = Loc.Instance["Settings_HistoryClearError"];
        }
        return true;
    }

    /// <summary>Verstöße („stt.baseUrl: …“) als Inline-Fehler am Pfad.</summary>
    private void ShowPolicyErrors(IReadOnlyList<string> errors)
    {
        foreach (var path in _policyErrorPaths)
        {
            Errors.Set(path, null);
        }
        _policyErrorPaths = [.. errors.Select(e => e.Split(':')[0].Trim()).Distinct()];
        foreach (var path in _policyErrorPaths)
        {
            Errors.Set(path, Loc.Instance["Validation_Policy"]);
        }
    }
}
