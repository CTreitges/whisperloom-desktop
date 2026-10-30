using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.ViewModels;

/// <summary>
/// Ein Key-Feld: zeigt nur Maskenzeichen bzw. „nicht gesetzt“, nie den Wert und kein Aufdecken. Aktionen:
/// Ersetzen (neues Feld mit Maskenzeichen, nach dem Speichern sofort geleert), Löschen, Zugang prüfen.
/// Der gespeicherte Key wird nur für „Zugang prüfen“ gelesen und nicht gehalten.
/// </summary>
public sealed partial class KeyFieldViewModel : ObservableObject
{
    /// <summary>Feste Maske, unabhängig von der Key-Länge.</summary>
    public const string Mask = "••••••••••••";

    private readonly ISecretStore _store;
    private readonly IAccessChecker _checker;
    private readonly Func<(string ProviderId, string BaseUrl)> _target;

    public KeyFieldViewModel(string name, ISecretStore store, IAccessChecker checker, Func<(string ProviderId, string BaseUrl)> target)
    {
        Name = name;
        _store = store;
        _checker = checker;
        _target = target;
        Loc.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(MaskText));
    }

    /// <summary>Name im <see cref="ISecretStore"/> (<see cref="SecretNames"/>).</summary>
    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaskText))]
    private bool _isSet;

    [ObservableProperty]
    private bool _isReplacing;

    /// <summary>Eingabe beim Ersetzen; wird nach dem Speichern oder Abbrechen geleert.</summary>
    [ObservableProperty]
    private string _newValue = "";

    [ObservableProperty]
    private string _status = "";

    public string MaskText => IsSet ? Mask : Loc.Instance["Key_NotSet"];

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
        NewValue = "";
        IsReplacing = true;
    }

    [RelayCommand]
    private async Task SaveKey()
    {
        var value = NewValue.Trim();
        NewValue = "";
        IsReplacing = false;
        if (value.Length == 0)
        {
            return;
        }
        try
        {
            await _store.SetAsync(Name, value, CancellationToken.None);
            IsSet = true;
            Status = Loc.Instance["Key_Saved"];
        }
        catch (IOException)
        {
            Status = Loc.Instance["Key_StoreError"];
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
        try
        {
            await _store.DeleteAsync(Name, CancellationToken.None);
            IsSet = false;
            Status = Loc.Instance["Key_Deleted"];
        }
        catch (IOException)
        {
            Status = Loc.Instance["Key_StoreError"];
        }
    }

    [RelayCommand]
    private async Task Check()
    {
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
        var result = await _checker.CheckAsync(providerId, baseUrl, key, CancellationToken.None);
        Status = result.StatusCode is { } code
            ? $"{Loc.Instance[result.MessageKey]} (HTTP {code})"
            : Loc.Instance[result.MessageKey];
    }
}
