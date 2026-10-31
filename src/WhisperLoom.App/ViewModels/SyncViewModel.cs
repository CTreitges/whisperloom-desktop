using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.ViewModels;

/// <summary>Eine Zeile der Import-Vorschau.</summary>
public sealed record ChangeRow(string Path, string Old, string New, bool Locked)
{
    public string Text => $"{Path}: {Old} → {New}" + (Locked ? "  (" + Loc.Instance["Policy_ByIt"] + ")" : "");
}

/// <summary>
/// Abschnitt Import/Export der Einstellungen. Import: Datei lesen → Vorschau als Diff-Liste → Übernehmen.
/// Die Dateiauswahl macht das Fenster; hier kommt nur der Text an. Keys und Richtlinie übernimmt nur
/// <c>WhisperLoom provision</c>, ein Export enthält beides nie.
/// </summary>
public sealed partial class SyncViewModel(DictationController controller) : ObservableObject
{
    public const string SuggestedFileName = "whisperloom-settings.json";

    private ImportPreview? _preview;

    public ObservableCollection<ChangeRow> Changes { get; } = [];

    [ObservableProperty] private string _status = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _canApply;

    /// <summary>Nach dem Übernehmen: das Einstellungsfenster muss neu laden (seine Kopie ist veraltet).</summary>
    public event Action? Applied;

    public string ExportJson() => SyncExport.ToJson(controller.Settings);

    public void ExportDone(string fileName) => Status = Loc.Instance.Format("Sync_Exported", fileName);

    /// <summary>Liest den Dateiinhalt und zeigt die Vorschau; übernommen wird erst mit <see cref="Apply"/>.</summary>
    public void Preview(string json)
    {
        Changes.Clear();
        _preview = null;
        CanApply = false;
        ImportPreview preview;
        try
        {
            preview = SyncImport.Preview(controller.Settings, SyncDocument.Parse(json), controller.Policy);
        }
        catch (Exception e) when (e is SyncFormatException or NotSupportedException)
        {
            Status = Loc.Instance["Sync_Invalid"] + " " + e.Message;
            return;
        }
        foreach (var c in preview.Changes)
        {
            Changes.Add(new ChangeRow(c.Path, c.Old ?? "–", c.New ?? "–", c.Locked));
        }
        var lines = new List<string>();
        if (preview.Errors.Count > 0)
        {
            lines.Add(Loc.Instance["Sync_Invalid"]);
            lines.AddRange(preview.Errors);
        }
        else
        {
            lines.Add(Loc.Instance.Format("Sync_Preview", preview.Changes.Count(c => !c.Locked)));
        }
        lines.AddRange(preview.Warnings.Select(_ => Loc.Instance["Sync_SecretsIgnored"]));
        Status = string.Join("\n", lines);
        _preview = preview;
        CanApply = preview.CanApply && preview.Changes.Any(c => !c.Locked);
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    public async Task Apply()
    {
        if (_preview?.Settings is not { } settings)
        {
            return;
        }
        try
        {
            await controller.ApplySettingsAsync(settings);
            Status = Loc.Instance["Sync_Applied"];
        }
        catch (IOException)
        {
            Status = Loc.Instance["Settings_HistoryClearError"];
        }
        CanApply = false;
        _preview = null;
        Applied?.Invoke();
    }
}

/// <summary>
/// Sperren der Richtlinie für die Oberfläche: <c>IsEnabled="{Binding Editable[stt.providerId]}"</c>.
/// Die Richtlinie ändert sich nicht, solange das Fenster offen ist (Neuladen öffnet es neu).
/// </summary>
public sealed class LockMap(PolicySettings policy)
{
    public bool this[string path] => !policy.IsLocked(path);
}
