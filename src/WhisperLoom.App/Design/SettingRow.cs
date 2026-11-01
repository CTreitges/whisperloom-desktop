using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels.Settings;

namespace WhisperLoom.App.Design;

/// <summary>
/// Baustein „Zeile“: Titel, Beschreibung und rechts das Steuerelement (<see cref="ContentControl.Content"/>), darunter ein
/// Fehlertext. Mit <see cref="Path"/> kommen Titel und Beschreibung aus dem <see cref="SettingsCatalog"/>, und die Stufe
/// aus dem vererbten <see cref="Managed.ContextProperty"/> gilt: Fest blendet die Zeile aus, Vorgabe zeigt Etikett und
/// „Auf Firmenvorgabe zurücksetzen“. Setzt den Accessible Name des Steuerelements auf den Titel.
/// </summary>
public class SettingRow : ContentControl
{
    public static readonly StyledProperty<string?> PathProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(Path));
    public static readonly StyledProperty<string?> TitleKeyProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(TitleKey));
    public static readonly StyledProperty<string?> DescriptionKeyProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(DescriptionKey));
    public static readonly StyledProperty<string?> ErrorProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(Error));
    public static readonly StyledProperty<bool> StackedProperty = AvaloniaProperty.Register<SettingRow, bool>(nameof(Stacked));

    public static readonly DirectProperty<SettingRow, string> TitleProperty =
        AvaloniaProperty.RegisterDirect<SettingRow, string>(nameof(Title), r => r.Title);
    public static readonly DirectProperty<SettingRow, string> DescriptionProperty =
        AvaloniaProperty.RegisterDirect<SettingRow, string>(nameof(Description), r => r.Description);
    public static readonly DirectProperty<SettingRow, ManagedLevel> LevelProperty =
        AvaloniaProperty.RegisterDirect<SettingRow, ManagedLevel>(nameof(Level), r => r.Level);
    public static readonly DirectProperty<SettingRow, bool> IsShownProperty =
        AvaloniaProperty.RegisterDirect<SettingRow, bool>(nameof(IsShown), r => r.IsShown);
    public static readonly DirectProperty<SettingRow, string> BadgeTextProperty =
        AvaloniaProperty.RegisterDirect<SettingRow, string>(nameof(BadgeText), r => r.BadgeText);
    public static readonly DirectProperty<SettingRow, string> ResetTextProperty =
        AvaloniaProperty.RegisterDirect<SettingRow, string>(nameof(ResetText), r => r.ResetText);

    private string _title = "";
    private string _description = "";
    private ManagedLevel _level;
    private bool _isShown = true;
    private string _badgeText = "";
    private string _resetText = "";

    public SettingRow()
    {
        ResetCommand = new AsyncRelayCommand(ResetAsync);
    }

    /// <summary>Settings-Pfad, z. B. <c>stt.model</c> oder <c>secrets.stt</c>; null = Aktions- oder Info-Zeile.</summary>
    public string? Path { get => GetValue(PathProperty); set => SetValue(PathProperty, value); }

    /// <summary>Titel-Schlüssel (Localization); ohne Angabe der aus dem Katalog.</summary>
    public string? TitleKey { get => GetValue(TitleKeyProperty); set => SetValue(TitleKeyProperty, value); }

    public string? DescriptionKey { get => GetValue(DescriptionKeyProperty); set => SetValue(DescriptionKeyProperty, value); }

    /// <summary>Inline-Fehler (ungültiger Wert, nicht gespeichert); leer = keiner.</summary>
    public string? Error { get => GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }

    /// <summary>Steuerelement unter dem Text in voller Breite (Listen, mehrzeilige Felder).</summary>
    public bool Stacked { get => GetValue(StackedProperty); set => SetValue(StackedProperty, value); }

    public string Title { get => _title; private set => SetAndRaise(TitleProperty, ref _title, value); }

    public string Description { get => _description; private set => SetAndRaise(DescriptionProperty, ref _description, value); }

    public ManagedLevel Level { get => _level; private set => SetAndRaise(LevelProperty, ref _level, value); }

    /// <summary>false bei Stufe Fest: die Vorlage zeigt dann nichts, auch nicht für Screenreader.</summary>
    public bool IsShown { get => _isShown; private set => SetAndRaise(IsShownProperty, ref _isShown, value); }

    public string BadgeText { get => _badgeText; private set => SetAndRaise(BadgeTextProperty, ref _badgeText, value); }

    public string ResetText { get => _resetText; private set => SetAndRaise(ResetTextProperty, ref _resetText, value); }

    public ICommand ResetCommand { get; }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Loc.Instance.PropertyChanged += OnLanguageChanged;
        Refresh();
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
        base.OnDetachedFromLogicalTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PathProperty || change.Property == TitleKeyProperty || change.Property == DescriptionKeyProperty
            || change.Property == Managed.ContextProperty || change.Property == ContentProperty || change.Property == ErrorProperty)
        {
            Refresh();
        }
        if (change.Property == StackedProperty)
        {
            PseudoClasses.Set(":stacked", Stacked);
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var loc = Loc.Instance;
        var entry = Path is null ? null : SettingsCatalog.Find(Path);
        var titleKey = TitleKey ?? entry?.TitleKey;
        var descriptionKey = DescriptionKey ?? entry?.DescriptionKey;
        Title = titleKey is null ? "" : loc[titleKey];
        Description = descriptionKey is null ? "" : loc[descriptionKey];
        Level = Path is null ? ManagedLevel.Free : Managed.GetContext(this)?.Source.LevelOf(Path) ?? ManagedLevel.Free;
        IsShown = Level != ManagedLevel.Fixed;
        BadgeText = loc["Managed_Badge"];
        ResetText = loc["Managed_Reset"];
        PseudoClasses.Set(":default", Level == ManagedLevel.Default);
        PseudoClasses.Set(":error", !string.IsNullOrEmpty(Error));
        PseudoClasses.Set(":described", Description.Length > 0);
        if (Content is Control control)
        {
            control.SetValue(AutomationProperties.NameProperty, Title);
            control.SetValue(AutomationProperties.HelpTextProperty, string.IsNullOrEmpty(Error) ? Description : Error);
        }
    }

    private Task ResetAsync() =>
        Path is { } path && Managed.GetContext(this) is { } context ? context.ResetToDefaultAsync(path) : Task.CompletedTask;
}
