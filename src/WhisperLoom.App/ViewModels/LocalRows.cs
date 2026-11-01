using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.ViewModels;

/// <summary>
/// Ein lokaler OpenAI-kompatibler Server (<c>local.servers</c>): Name, Art (Erkennung/Text), Adresse, Modell, „Zugang
/// prüfen“ (nie mit Key). Ungültige Eingaben bleiben als Fehler an der Zeile stehen und werden nicht übernommen.
/// </summary>
public sealed partial class LocalServerRow : ObservableObject
{
    private readonly Func<IEnumerable<string>> _otherNames;
    private readonly Action _changed;
    private readonly Action<LocalServerRow> _remove;
    private readonly HttpClient? _http;

    public LocalServerRow(LocalServer server, Func<IEnumerable<string>> otherNames, Action changed, Action<LocalServerRow> remove, HttpClient? http)
    {
        Server = server;
        _otherNames = otherNames;
        _changed = changed;
        _remove = remove;
        _http = http;
        Kinds = [.. LocalServer.Kinds.Select(k => new Choice(k, Loc.Instance["ServerKind_" + k]))];
        _kind = SettingsViewModel.Pick(Kinds, server.Kind);
    }

    public LocalServer Server { get; }

    public IReadOnlyList<Choice> Kinds { get; }

    [ObservableProperty] private Choice _kind;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _status = "";

    public string Name
    {
        get => Server.Name;
        set
        {
            var name = value.Trim();
            if (name.Length == 0 || name.Contains(':', StringComparison.Ordinal) || _otherNames().Contains(name))
            {
                Error = Loc.Instance["Server_NameInvalid"];
                return;
            }
            Error = "";
            Server.Name = name;
            OnPropertyChanged();
            _changed();
        }
    }

    public string BaseUrl
    {
        get => Server.BaseUrl;
        set
        {
            var url = value.Trim();
            var problem = Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"
                ? ServerUrlCheck.Check(url, ProviderCatalog.Custom)
                : new ServerUrlCheck.Problem(ServerUrlCheck.Severity.Error, ServerUrlCheck.MsgScheme);
            // Unverschlüsselt über das Internet ist hier ein Fehler: lokale Server sind LAN, VPN oder dieses Gerät.
            if (problem is { } p && (p.Severity == ServerUrlCheck.Severity.Error || p.Message == ServerUrlCheck.MsgPublicHttp))
            {
                Error = p.Message;
                return;
            }
            Error = problem?.Message ?? "";
            Server.BaseUrl = url;
            OnPropertyChanged();
            _changed();
        }
    }

    public string Model
    {
        get => Server.Model;
        set
        {
            Server.Model = value.Trim();
            OnPropertyChanged();
            _changed();
        }
    }

    partial void OnKindChanged(Choice value)
    {
        if (value is null || Server.Kind == value.Value) return;
        Server.Kind = value.Value;
        _changed();
    }

    /// <summary>„Zugang prüfen“: GET <c>/models</c> am Server dieses Eintrags, ohne Key.</summary>
    [RelayCommand]
    private async Task Check()
    {
        if (_http is null) return;
        Status = Loc.Instance["Access_Checking"];
        var id = LocalServerSource.LocalPrefix + Server.Name;
        var checker = new HttpAccessChecker(() => _http, () => new ProviderDirectory([new LocalServerSource([Server])]));
        var result = await checker.CheckAsync(id, "", null, CancellationToken.None);
        Status = result.StatusCode is { } code ? $"{Loc.Instance[result.MessageKey]} (HTTP {code})" : Loc.Instance[result.MessageKey];
    }

    [RelayCommand]
    private void Remove() => _remove(this);
}

/// <summary>Ein mitgeliefertes Textmodell mit Größe, Lizenz, RAM-Hinweis, Download (fortsetzbar, SHA-256) und Löschen.</summary>
public sealed partial class TextModelViewModel(TextModel model, LocalParts? parts, long deviceRam) : ObservableObject
{
    private CancellationTokenSource? _download;

    public TextModel Model { get; } = model;

    public string Label => $"{Model.Label} ({Model.File.Bytes / 1_000_000} MB, {Model.License}){(Model.File.Recommended ? " ★" : "")}";

    /// <summary>Passt in den Arbeitsspeicher dieses Geräts.</summary>
    public bool Fits => TextModelCatalog.Fits(Model, deviceRam);

    public string RamNote => Fits ? "" : TextModelCatalog.MsgTooLittleRam(Model);

    public bool Installed => parts?.Bundled.Models.IsInstalled(Model.File) == true;

    public bool CanDownload => !Installed && Fits;

    [ObservableProperty] private string _status = "";

    /// <summary>Lädt das Modell (Range-Fortsetzung, SHA-256, Platzprüfung, Wiederholungen) und <c>llama-server</c> (CPU, Vulkan optional).</summary>
    [RelayCommand]
    public async Task Download()
    {
        if (parts is null || _download is not null || !Fits) return;
        _download = new CancellationTokenSource();
        try
        {
            Status = Loc.Instance["Local_Downloading"];
            var ok = await new ModelDownloader(parts.Download).DownloadAsync(Model.File, parts.Bundled.Models,
                (bytes, total, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => Status = $"{bytes * 100 / Math.Max(1, total)} %"), _download.Token);
            if (ok)
            {
                await parts.Bundled.Release.EnsureAsync(SidecarVariant.Cpu, null, _download.Token);
                try
                {
                    await parts.Bundled.Release.EnsureAsync(SidecarVariant.Vulkan, null, _download.Token);
                }
                catch (DownloadException)
                {
                    // Vulkan ist optional: die CPU rechnet.
                }
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
            OnPropertyChanged(nameof(Installed));
            OnPropertyChanged(nameof(CanDownload));
        }
    }

    [RelayCommand]
    public void Cancel() => _download?.Cancel();

    [RelayCommand]
    public async Task Delete()
    {
        if (parts is null || _download is not null) return;
        await parts.Bundled.StopAsync();
        try
        {
            Status = parts.Bundled.Models.Delete(Model.File) ? "" : Loc.Instance["Local_DeleteFailed"];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = Loc.Instance["Local_DeleteFailed"];
        }
        OnPropertyChanged(nameof(Installed));
        OnPropertyChanged(nameof(CanDownload));
    }
}
