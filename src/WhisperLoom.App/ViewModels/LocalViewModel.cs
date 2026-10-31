using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.ViewModels;

/// <summary>Ein Modell der lokalen Erkennung mit Größe, Zustand und Download/Löschen.</summary>
public sealed partial class LocalModelViewModel(OfflineModel model, LocalParts? parts) : ObservableObject
{
    private CancellationTokenSource? _download;

    public OfflineModel Model { get; } = model;

    public string Label => $"{Model.Label} ({Model.Bytes / 1_000_000} MB){(Model.Recommended ? " ★" : "")}";

    public bool Installed => parts?.Stt.Models.IsInstalled(Model) == true;

    [ObservableProperty] private string _status = "";

    /// <summary>Lädt das Modell (Range-Fortsetzung, SHA256) und, falls nötig, den Begleitprozess.</summary>
    [RelayCommand]
    public async Task Download()
    {
        if (parts is null || _download is not null) return;
        _download = new CancellationTokenSource();
        try
        {
            Status = Loc.Instance["Local_Downloading"];
            var ok = await new ModelDownloader(parts.Download).DownloadAsync(Model, parts.Stt.Models,
                (bytes, total, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => Status = $"{bytes * 100 / Math.Max(1, total)} %"), _download.Token);
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
        catch (DownloadException e)
        {
            Status = e.Message;
        }
        catch (OperationCanceledException)
        {
            Status = Loc.Instance["Local_Paused"];
        }
        finally
        {
            _download.Dispose();
            _download = null;
            OnPropertyChanged(nameof(Installed));
        }
    }

    [RelayCommand]
    public void Cancel() => _download?.Cancel();

    [RelayCommand]
    public async Task Delete()
    {
        if (parts is null) return;
        await parts.Stt.StopAsync();
        Status = parts.Stt.Models.Delete(Model) ? "" : Loc.Instance["Local_DeleteFailed"];
        OnPropertyChanged(nameof(Installed));
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
        Engines = [new Choice("online", loc["Local_EngineOnline"]), new Choice("offline", loc["Local_EngineOffline"])];
        Backends = [new Choice("auto", loc["Local_BackendAuto"]), new Choice("cpu", loc["Local_BackendCpu"])];
        Rules = [.. new[] { "local", "online_local", "skip" }.Select(r => new Choice(r, loc["Local_Rule_" + r]))];
        Models = [.. ModelCatalog.Models.Select(m => new LocalModelViewModel(m, parts))];
        ModelChoices = [.. ModelCatalog.Models.Select(m => new Choice(m.Id, m.Label))];
        _engine = Pick(Engines, settings.Engine ?? "online");
        _backend = Pick(Backends, _local.Backend);
        _rule = Pick(Rules, settings.Offline.Refine);
        _selectedModel = Pick(ModelChoices, ModelCatalog.ById(settings.Offline.Model).Id);
        _ollamaModel = _local.OllamaModel;
        if (_ollamaModel.Length > 0) OllamaModels.Add(_ollamaModel);
    }

    public IReadOnlyList<Choice> Engines { get; }
    public IReadOnlyList<Choice> Backends { get; }
    public IReadOnlyList<Choice> Rules { get; }
    public IReadOnlyList<Choice> ModelChoices { get; }
    public IReadOnlyList<LocalModelViewModel> Models { get; }
    public ObservableCollection<string> OllamaModels { get; } = [];

    [ObservableProperty] private Choice _engine;
    [ObservableProperty] private Choice _backend;
    [ObservableProperty] private Choice _rule;
    [ObservableProperty] private Choice _selectedModel;
    [ObservableProperty] private string _ollamaModel;
    [ObservableProperty] private string _ollamaStatus = "";
    [ObservableProperty] private bool _ollamaMissing;

    public bool Accurate { get => _s.Offline.Accurate; set => _s.Offline.Accurate = value; }

    /// <summary>Womit zuletzt erkannt wurde, z. B. „vulkan · NVIDIA GeForce GTX 1080 Ti“.</summary>
    public string BackendInfo => _parts?.Stt.LastBackend is { } b ? $"{b.Backend} · {b.Device}" : Loc.Instance["Local_NotStarted"];

    public string OllamaDownloadUrl => OllamaLocalRefiner.DownloadUrl;

    /// <summary>Modellliste von Ollama (<c>/api/tags</c>); fehlt Ollama, ein Hinweis mit Link — keine Installation.</summary>
    [RelayCommand]
    public async Task RefreshOllama()
    {
        if (_parts is null) return;
        var models = await OllamaLocalRefiner.ListModelsAsync(_parts.OllamaHttp, LocalParts.OllamaUrl(_s));
        OllamaMissing = models is null;
        OllamaModels.Clear();
        foreach (var m in models ?? []) OllamaModels.Add(m);
        OllamaStatus = models is null ? Loc.Instance["Local_OllamaMissing"]
            : models.Count == 0 ? Loc.Instance["Local_OllamaEmpty"]
            : string.Format(Loc.Instance["Local_OllamaFound"], models.Count);
        if (OllamaModel.Length == 0 && models is { Count: > 0 }) OllamaModel = models[0];
    }

    /// <summary>Übernimmt die Auswahl in die Einstellungs-Kopie (vor dem Speichern).</summary>
    public void Apply()
    {
        _s.Engine = Engine.Value;
        _s.Offline.Model = SelectedModel.Value;
        _s.Offline.Refine = Rule.Value;
        _local.Backend = Backend.Value;
        _local.OllamaModel = (OllamaModel ?? "").Trim();
        LocalSettings.Set(_s, _local);
    }

    private static Choice Pick(IReadOnlyList<Choice> choices, string value) =>
        choices.FirstOrDefault(c => c.Value == value) ?? choices[0];
}
