using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.ViewModels;

/// <summary>Eine eigene Whisper-Datei (<c>offline.customModels</c>) mit Entfernen.</summary>
public sealed partial class CustomWhisperRow(CustomWhisperModel model, Action<CustomWhisperRow> remove) : ObservableObject
{
    public CustomWhisperModel Model { get; } = model;

    public string Name => Model.Name;

    public string Path => Model.Path;

    public string RemoveName => Loc.Instance.Format("A11y_ItemAction", Loc.Instance["Local_Remove"], Name);

    /// <summary>„eigene Datei · Pfad“ unter dem Namen in der Modell-Liste.</summary>
    public string FileNote => Loc.Instance.Format("Local_FileNote", Model.Path);

    /// <summary>Wert von <c>offline.model</c> für diese Datei.</summary>
    public string Id => OfflineSettings.CustomModelPrefix + Model.Name;

    /// <summary>Optionsfeld der Modell-Liste (eigene Dateien stehen mit in der Liste).</summary>
    public bool IsSelected
    {
        get => CurrentId?.Invoke() == Id;
        set
        {
            if (value)
            {
                Choose?.Invoke(Id);
            }
        }
    }

    internal Func<string>? CurrentId { get; set; }

    internal Action<string>? Choose { get; set; }

    internal void RefreshSelection() => OnPropertyChanged(nameof(IsSelected));

    /// <summary>Datei fehlt bzw. ist kein Modell: Hinweis an der Zeile.</summary>
    public string Status => WhisperModels.CheckFile(Model.Path) switch
    {
        WhisperModels.FileCheck.Ok => "",
        WhisperModels.FileCheck.Missing => Loc.Instance["Local_FileMissing"],
        _ => Loc.Instance["Local_FileNoModel"],
    };

    [RelayCommand]
    private void Remove() => remove(this);
}

/// <summary>
/// W2-LOCAL-Teil von <see cref="LocalViewModel"/>: Text-Engine (Ollama, mitgeliefert, Server), mitgelieferte Modelle,
/// eigene Whisper-Dateien und lokale Server. Änderungen an Dateien oder Servern melden <see cref="EntriesChanged"/>
/// (Anbieterlisten neu bauen, speichern).
/// </summary>
public sealed partial class LocalViewModel
{
    public static readonly IReadOnlyList<string> TextEngineKeys = [LocalTextEngine.Ollama, LocalTextEngine.Bundled, LocalTextEngine.Server];

    /// <summary>Eigene Dateien oder lokale Server haben sich geändert.</summary>
    public event Action? EntriesChanged;

    public IReadOnlyList<Choice> TextEngines { get; private set; } = [];

    public IReadOnlyList<Choice> BundledModels { get; private set; } = [];

    public ObservableCollection<Choice> TextServers { get; } = [];

    public ObservableCollection<CustomWhisperRow> CustomModels { get; } = [];

    public ObservableCollection<LocalServerRow> Servers { get; } = [];

    public IReadOnlyList<TextModelViewModel> TextModels { get; private set; } = [];

    [ObservableProperty] private Choice _textEngine = new("", "");
    [ObservableProperty] private Choice _bundledModel = new("", "");
    [ObservableProperty] private Choice? _textServer;
    [ObservableProperty] private string _customModelError = "";

    /// <summary>Gewählte eigene Whisper-Datei fehlt oder ist kein Modell: Fehler direkt an der Modellwahl („Erkennung“).</summary>
    public string? SelectedModelError =>
        SelectedModel?.Value is { } id && id.StartsWith(OfflineSettings.CustomModelPrefix, StringComparison.Ordinal) &&
        _s.Offline.CustomModels.FirstOrDefault(m => OfflineSettings.CustomModelPrefix + m.Name == id) is { } m &&
        WhisperModels.CheckFile(m.Path) != WhisperModels.FileCheck.Ok
            ? Loc.Instance["Local_SelectedFileMissing"]
            : null;

    partial void OnSelectedModelChanged(Choice value)
    {
        OnPropertyChanged(nameof(SelectedModelError));
        foreach (var row in Models)
        {
            row.RefreshSelection();
        }
        foreach (var row in CustomModels)
        {
            row.RefreshSelection();
        }
    }

    partial void OnBundledModelChanged(Choice value)
    {
        foreach (var row in TextModels)
        {
            row.RefreshSelection();
        }
    }

    /// <summary>Optionsfeld der Modell-Liste gewählt: <c>offline.model</c> auf dieses Modell bzw. diese Datei.</summary>
    private void ChooseModel(string id) => SelectedModel = Pick(ModelChoices, id);

    private void ChooseBundled(string id) => BundledModel = Pick(BundledModels, id);

    private CustomWhisperRow NewCustomRow(CustomWhisperModel model) =>
        new(model, RemoveCustomModel) { CurrentId = () => SelectedModel.Value, Choose = ChooseModel };

    public bool IsOllama => TextEngine.Value == LocalTextEngine.Ollama;

    public bool IsBundled => TextEngine.Value == LocalTextEngine.Bundled;

    public bool IsServer => TextEngine.Value == LocalTextEngine.Server;

    partial void OnTextEngineChanged(Choice value)
    {
        OnPropertyChanged(nameof(IsOllama));
        OnPropertyChanged(nameof(IsBundled));
        OnPropertyChanged(nameof(IsServer));
    }

    private void InitEntries()
    {
        var loc = Loc.Instance;
        TextEngines = [.. TextEngineKeys.Select(k => new Choice(k, loc["TextEngine_" + k], loc["TextEngine_" + k + "_Desc"]))];
        var ram = TextModelCatalog.DeviceRamBytes();
        BundledModels = [.. TextModelCatalog.Models.Select(m => new Choice(m.Id, TextModelCatalog.Fits(m, ram) ? m.Label : loc.Format("TextModel_TooBig", m.Label)))];
        TextModels = [.. TextModelCatalog.Models.Select(m => new TextModelViewModel(m, _parts, ram) { CurrentId = () => BundledModel.Value, Choose = ChooseBundled })];
        TextEngine = Pick(TextEngines, _local.TextEngine);
        BundledModel = Pick(BundledModels, TextModelCatalog.ById(_local.BundledModel).Id);
        foreach (var m in _s.Offline.CustomModels)
        {
            CustomModels.Add(NewCustomRow(m));
        }
        foreach (var server in _local.Servers)
        {
            Servers.Add(NewServerRow(server));
        }
        FillTextServers();
    }

    /// <summary>Lokale Modelle: Katalog und eigene Dateien als <c>custom:&lt;name&gt;</c>.</summary>
    private void FillModelChoices()
    {
        ModelChoices.Clear();
        foreach (var m in ModelCatalog.Models)
        {
            ModelChoices.Add(new Choice(m.Id, m.Label));
        }
        foreach (var m in _s.Offline.CustomModels)
        {
            ModelChoices.Add(new Choice(OfflineSettings.CustomModelPrefix + m.Name, Loc.Instance.Format("Local_CustomFile", m.Name)));
        }
    }

    private void FillTextServers()
    {
        var current = TextServer?.Value ?? _local.TextServer;
        TextServers.Clear();
        TextServers.Add(new Choice("", Loc.Instance["TextServer_First"]));
        foreach (var s in _local.Servers.Where(s => s.Kind == "text"))
        {
            TextServers.Add(new Choice(s.Name, s.Name));
        }
        TextServer = TextServers.FirstOrDefault(c => c.Value == current) ?? TextServers[0];
    }

    /// <summary>Eigene Whisper-Datei hinzufügen (Dateiauswahl .bin/.gguf). Kein Modell → Fehler an der Zeile, nichts gespeichert.</summary>
    public string? AddCustomModel(string path)
    {
        var error = WhisperModels.CheckFile(path) switch
        {
            WhisperModels.FileCheck.Ok => null,
            WhisperModels.FileCheck.Missing => Loc.Instance["Local_FileMissing"],
            _ => Loc.Instance["Local_FileNoModel"],
        };
        CustomModelError = error ?? "";
        if (error is not null) return error;
        var model = new CustomWhisperModel { Name = WhisperModels.SuggestName(path, _s.Offline.CustomModels), Path = path };
        _s.Offline.CustomModels.Add(model);
        CustomModels.Add(NewCustomRow(model));
        FillModelChoices();
        SelectedModel = Pick(ModelChoices, SelectedModel.Value);
        EntriesChanged?.Invoke();
        return null;
    }

    /// <summary>Entfernt den Eintrag (nicht die Datei). War er gewählt, gilt wieder die Empfehlung (Small).</summary>
    private void RemoveCustomModel(CustomWhisperRow row)
    {
        _s.Offline.CustomModels.Remove(row.Model);
        CustomModels.Remove(row);
        var selected = SelectedModel.Value == OfflineSettings.CustomModelPrefix + row.Name ? ModelCatalog.Default.Id : SelectedModel.Value;
        FillModelChoices();
        SelectedModel = Pick(ModelChoices, selected);
        EntriesChanged?.Invoke();
    }

    [RelayCommand]
    private void AddServer()
    {
        var names = _local.Servers.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var name = Loc.Instance["Server_DefaultName"];
        for (var i = 2; names.Contains(name); i++) name = $"{Loc.Instance["Server_DefaultName"]} {i}";
        var server = new LocalServer { Name = name, Kind = "text", BaseUrl = "http://127.0.0.1:1234/v1" };
        _local.Servers.Add(server);
        Servers.Add(NewServerRow(server));
        ServersChanged();
    }

    private LocalServerRow NewServerRow(LocalServer server) =>
        new(server, () => _local.Servers.Where(s => !ReferenceEquals(s, server)).Select(s => s.Name), ServersChanged, row =>
        {
            _local.Servers.Remove(row.Server);
            Servers.Remove(row);
            ServersChanged();
        }, _parts?.OllamaHttp);

    private void ServersChanged()
    {
        FillTextServers();
        EntriesChanged?.Invoke();
    }

    private void ApplyEntries()
    {
        _local.TextEngine = TextEngine.Value;
        _local.BundledModel = BundledModel.Value == TextModelCatalog.Default.Id ? "" : BundledModel.Value;
        _local.TextServer = TextServer?.Value ?? "";
    }
}
