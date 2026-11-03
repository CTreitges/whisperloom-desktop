using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.ViewModels;

/// <summary>Ein Modell der lokalen Erkennung mit Größe, Zustand und Download/Löschen.</summary>
public sealed partial class LocalModelViewModel(OfflineModel model, LocalParts? parts) : ObservableObject, IModelRow
{
    private CancellationTokenSource? _download;

    public OfflineModel Model { get; } = model;

    public string Label => $"{Model.Label} ({Model.Bytes / 1_000_000} MB){(Model.Recommended ? " ★" : "")}";

    public bool Installed => parts?.Stt.Models.IsInstalled(Model) == true;

    /// <summary>Screenreader-Namen: sichtbares Verb zuerst, dann der Eintrag (WCAG 2.5.3).</summary>
    public string DownloadName => Loc.Instance.Format("A11y_ItemAction", Loc.Instance["Local_Download"], Model.Label);

    public string DeleteName => Loc.Instance.Format("A11y_ItemAction", Loc.Instance["Local_Delete"], Model.Label);

    public string CancelName => Loc.Instance.Format("A11y_ItemAction", Loc.Instance["Local_Pause"], Model.Label);

    /// <summary>Name ohne Größe und Sternchen (Modell-Liste, B5: keine Unicode-Zeichen als Bedeutungsträger).</summary>
    public string Name => Model.Label;

    public string Size => Loc.Instance.Format("Model_Size", Model.Bytes / 1_000_000);

    public bool Recommended => Model.Recommended;

    /// <summary>Optionsfeld der Modell-Liste: gewählt ist <c>offline.model</c> (setzt <see cref="LocalViewModel"/>).</summary>
    public bool IsSelected
    {
        get => CurrentId?.Invoke() == Model.Id;
        set
        {
            if (value)
            {
                Choose?.Invoke(Model.Id);
            }
        }
    }

    internal Func<string>? CurrentId { get; set; }

    internal Action<string>? Choose { get; set; }

    internal void RefreshSelection() => OnPropertyChanged(nameof(IsSelected));

    public bool CanDownload => !Installed;

    public string Note => "";

    /// <summary>Rückfrage vor dem Löschen (inline in der Zeile, nennt Name und Größe).</summary>
    [ObservableProperty] private bool _deletePending;

    public string DeleteQuestion => Loc.Instance.Format("Model_DeleteQuestion", Model.Label, Model.Bytes / 1_000_000);

    [RelayCommand]
    private void AskDelete() => DeletePending = true;

    [RelayCommand]
    private void Keep() => DeletePending = false;

    [ObservableProperty] private string _status = "";

    /// <summary>Download läuft: Fortschritt (0–100) und „Anhalten“ statt „Herunterladen“.</summary>
    [ObservableProperty] private bool _isDownloading;

    [ObservableProperty] private double _progress;

    /// <summary>Lädt das Modell (Range-Fortsetzung, SHA256) und, falls nötig, den Begleitprozess.</summary>
    [RelayCommand]
    public async Task Download()
    {
        if (parts is null || _download is not null) return;
        _download = new CancellationTokenSource();
        IsDownloading = true;
        Progress = 0;
        try
        {
            Status = Loc.Instance["Local_Downloading"];
            var ok = await new ModelDownloader(parts.Download).DownloadAsync(Model, parts.Stt.Models,
                (bytes, total, rate) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Progress = bytes * 100.0 / Math.Max(1, total);
                    Status = DownloadText.Of(bytes, total, rate);
                }), _download.Token);
            if (ok)
            {
                await parts.Stt.Release.EnsureAsync(SidecarVariant.Cpu, null, _download.Token);
                await TryVulkanAsync(_download.Token);
                Status = Loc.Instance["Local_Installed"];
            }
            else
            {
                Status = Loc.Instance["Local_Paused"];
            }
        }
        catch (OperationCanceledException)
        {
            Status = Loc.Instance["Local_Paused"];
        }
        catch (Exception e) when (e is DownloadException or IOException or UnauthorizedAccessException or HttpRequestException)
        {
            Status = e.Message;
        }
        finally
        {
            _download.Dispose();
            _download = null;
            IsDownloading = false;
            OnPropertyChanged(nameof(Installed));
            OnPropertyChanged(nameof(CanDownload));
        }
    }

    [RelayCommand]
    public void Cancel() => _download?.Cancel();

    [RelayCommand]
    public async Task Delete()
    {
        DeletePending = false;
        if (parts is null || _download is not null) return; // erst anhalten, dann löschen
        await parts.Stt.StopAsync();
        try
        {
            Status = parts.Stt.Models.Delete(Model) ? "" : Loc.Instance["Local_DeleteFailed"];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = Loc.Instance["Local_DeleteFailed"];
        }
        OnPropertyChanged(nameof(Installed));
        OnPropertyChanged(nameof(CanDownload));
    }

    /// <summary>Die Vulkan-Variante ist optional: scheitert ihr Download, rechnet die CPU.</summary>
    private async Task TryVulkanAsync(CancellationToken ct)
    {
        try
        {
            await parts!.Stt.Release.EnsureAsync(SidecarVariant.Vulkan, null, ct);
        }
        catch (DownloadException)
        {
            // CPU bleibt.
        }
    }
}

/// <summary>
/// Einstellungs-Tab „Lokal“: Erkennung online oder lokal, Modelle (Download/Löschen), Backend (Vulkan → CPU),
/// Genauigkeit, Regel der Textverbesserung bei lokaler Erkennung und das Ollama-Modell. Arbeitet auf der Kopie der
/// Einstellungen des <see cref="SettingsViewModel"/>; <see cref="Apply"/> schreibt den Abschnitt <c>local</c> zurück.
/// </summary>
public sealed partial class LocalViewModel : ObservableObject
{
    private readonly WhisperLoomSettings _s;
    private readonly LocalSettings _local;
    private readonly LocalParts? _parts;

    public LocalViewModel(WhisperLoomSettings settings, LocalParts? parts)
    {
        _s = settings;
        _parts = parts;
        _local = LocalSettings.Of(settings);
        var loc = Loc.Instance;
        Engines = [new Choice("online", loc["Local_EngineOnline"], loc["Local_EngineOnline_Desc"]), new Choice("offline", loc["Local_EngineOffline"], loc["Local_EngineOffline_Desc"])];
        Backends = [new Choice("auto", loc["Local_BackendAuto"], loc["Local_BackendAuto_Desc"]), new Choice("cpu", loc["Local_BackendCpu"], loc["Local_BackendCpu_Desc"])];
        Rules = [.. new[] { "local", "online_local", "skip" }.Select(r => new Choice(r, loc["Local_Rule_" + r], loc["Local_Rule_" + r + "_Desc"]))];
        Models = [.. ModelCatalog.Models.Select(m => new LocalModelViewModel(m, parts) { CurrentId = () => SelectedModel.Value, Choose = ChooseModel })];
        FillModelChoices();
        _engine = Pick(Engines, settings.Engine ?? "online");
        _backend = Pick(Backends, _local.Backend);
        _rule = Pick(Rules, settings.Offline.Refine);
        // Eigene Datei bleibt gewählt (custom:<name>); unbekannte Katalog-ID gilt als Small.
        _selectedModel = ModelChoices.FirstOrDefault(c => c.Value == settings.Offline.Model) ?? Pick(ModelChoices, ModelCatalog.ById(settings.Offline.Model).Id);
        _ollamaModel = _local.OllamaModel;
        if (_ollamaModel.Length > 0) OllamaModels.Add(_ollamaModel);
        InitEntries();
    }

    public IReadOnlyList<Choice> Engines { get; }
    public IReadOnlyList<Choice> Backends { get; }
    public IReadOnlyList<Choice> Rules { get; }
    public System.Collections.ObjectModel.ObservableCollection<Choice> ModelChoices { get; } = [];
    public IReadOnlyList<LocalModelViewModel> Models { get; }
    public ObservableCollection<string> OllamaModels { get; } = [];

    [ObservableProperty] private Choice _engine;
    [ObservableProperty] private Choice _backend;
    [ObservableProperty] private Choice _rule;
    [ObservableProperty] private Choice _selectedModel;
    [ObservableProperty] private string _ollamaModel;
    [ObservableProperty] private string _ollamaStatus = "";
    [ObservableProperty] private bool _ollamaMissing;

    public bool Accurate
    {
        get => _s.Offline.Accurate;
        set
        {
            if (_s.Offline.Accurate != value)
            {
                _s.Offline.Accurate = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Begleitprozess endet nach so vielen Minuten ohne Erkennung (Abschnitt <c>local</c>).</summary>
    public int IdleMinutes
    {
        get => _local.IdleMinutes;
        set
        {
            if (_local.IdleMinutes != value)
            {
                _local.IdleMinutes = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Adresse von Ollama; "" = Standard (127.0.0.1:11434).</summary>
    public string OllamaUrl
    {
        get => _local.OllamaUrl;
        set
        {
            if (_local.OllamaUrl != value)
            {
                _local.OllamaUrl = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Womit zuletzt erkannt wurde, z. B. „vulkan · NVIDIA GeForce GTX 1080 Ti“.</summary>
    public string BackendInfo => _parts?.Stt.LastBackend is { } b ? $"{b.Backend} · {b.Device}" : Loc.Instance["Local_NotStarted"];

    public string OllamaDownloadUrl => OllamaLocalRefiner.DownloadUrl;

    /// <summary>Modellliste von Ollama (<c>/api/tags</c>); fehlt Ollama, ein Hinweis mit Link — keine Installation.</summary>
    [RelayCommand]
    public async Task RefreshOllama()
    {
        if (_parts is null) return;
        var models = await OllamaLocalRefiner.ListModelsAsync(_parts.OllamaHttp, _local.OllamaUrl.Length > 0 ? _local.OllamaUrl : OllamaLocalRefiner.DefaultBaseUrl);
        var chosen = OllamaModel ?? "";
        OllamaMissing = models is null;
        OllamaModels.Clear();
        foreach (var m in models ?? []) OllamaModels.Add(m);
        if (chosen.Length > 0 && !OllamaModels.Contains(chosen)) OllamaModels.Add(chosen);
        OllamaModel = chosen; // die ComboBox setzt beim Leeren null
        OllamaStatus = models is null ? Loc.Instance["Local_OllamaMissing"]
            : models.Count == 0 ? Loc.Instance["Local_OllamaEmpty"]
            : string.Format(Loc.Instance["Local_OllamaFound"], models.Count);
        if (string.IsNullOrEmpty(OllamaModel) && models is { Count: > 0 }) OllamaModel = models[0];
    }

    /// <summary>Übernimmt die Auswahl in die Einstellungs-Kopie (vor dem Speichern).</summary>
    public void Apply()
    {
        _s.Engine = Engine.Value;
        _s.Offline.Model = SelectedModel?.Value ?? _s.Offline.Model;
        _s.Offline.Refine = Rule.Value;
        _local.Backend = Backend.Value;
        _local.OllamaModel = (OllamaModel ?? "").Trim();
        ApplyEntries();
        LocalSettings.Set(_s, _local);
    }

    private static Choice Pick(IReadOnlyList<Choice> choices, string value) =>
        choices.FirstOrDefault(c => c.Value == value) ?? choices[0];
}
