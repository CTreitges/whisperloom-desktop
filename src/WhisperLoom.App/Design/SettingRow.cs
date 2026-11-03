using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.Core.Contracts.Settings;
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
    public static readonly StyledProperty<bool> BareProperty = AvaloniaProperty.Register<SettingRow, bool>(nameof(Bare));

    public static readonly DirectProperty<SettingRow, string> TitleProperty =
        AvaloniaProperty.RegisterDirect<SettingRow, string>(nameof(Title), r => r.Title);
    public static readonly DirectProperty<SettingRow, string> DescriptionProperty =
        AvaloniaProperty.RegisterDirect<SettingRow, string>(nameof(Description), r => r.Description);
    public static readonly DirectProperty<SettingRow, ManagedState> LevelProperty =
        AvaloniaProperty.RegisterDirect<SettingRow, ManagedState>(nameof(Level), r => r.Level);
    public static readonly DirectProperty<SettingRow, bool> IsShownProperty =
        AvaloniaProperty.RegisterDirect<SettingRow, bool>(nameof(IsShown), r => r.IsShown);
    public static readonly DirectProperty<SettingRow, string> BadgeTextProperty =
        AvaloniaProperty.RegisterDirect<SettingRow, string>(nameof(BadgeText), r => r.BadgeText);
    public static readonly DirectProperty<SettingRow, string> ResetTextProperty =
        AvaloniaProperty.RegisterDirect<SettingRow, string>(nameof(ResetText), r => r.ResetText);

    public static readonly DirectProperty<SettingRow, ICommand> ResetCommandProperty =
        AvaloniaProperty.RegisterDirect<SettingRow, ICommand>(nameof(ResetCommand), r => r.ResetCommand);

    private string _title = "";
    private string _description = "";
    private ManagedState _level;
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

    /// <summary>
    /// Ohne sichtbaren Titel und Beschreibung, wenn der Gruppentitel schon sagt, worum es geht (Optionsgruppe als erste
    /// Zeile, z. B. „Erkennungsweg“); der Titel bleibt Name des Steuerelements für Screenreader.
    /// </summary>
    public bool Bare { get => GetValue(BareProperty); set => SetValue(BareProperty, value); }

    public string Title { get => _title; private set => SetAndRaise(TitleProperty, ref _title, value); }

    public string Description { get => _description; private set => SetAndRaise(DescriptionProperty, ref _description, value); }

    public ManagedState Level { get => _level; private set => SetAndRaise(LevelProperty, ref _level, value); }

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
        if (change.Property == BareProperty)
        {
            PseudoClasses.Set(":bare", Bare);
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
        var source = Managed.GetContext(this)?.Source;
        Level = Path is null ? ManagedState.Free : source?.StateOf(Path) ?? ManagedState.Free;
        IsShown = Level != ManagedState.Fixed;
        BadgeText = loc["Managed_Badge"];
        ResetText = loc["Managed_Reset"];
        PseudoClasses.Set(":default", Level == ManagedState.Default);
        // Zurücksetzen nur, wenn der Nutzer die Vorgabe geändert hat (ManagedDefaults, W2-C0).
        PseudoClasses.Set(":overridden", Level == ManagedState.Default && source?.IsOverridden(Path!) == true);
        PseudoClasses.Set(":error", !string.IsNullOrEmpty(Error));
        PseudoClasses.Set(":described", Description.Length > 0);
        // Name auf das Steuerelement; bei einem Panel auf das erste Eingabeelement ohne eigenen Text (z. B. Liste neben „Neu laden“).
        var target = Content is Panel panel
            ? panel.Children.OfType<TemplatedControl>().FirstOrDefault(c => c is not Button)
            : Content as Control;
        if (target is not null)
        {
            target.SetValue(AutomationProperties.NameProperty, Title);
            target.SetValue(AutomationProperties.HelpTextProperty, string.IsNullOrEmpty(Error) ? Description : Error);
        }
    }

    /// <summary>Nach dem Sprung aus der Suche: Zeile kurz in der Auswahlfarbe zeigen (zielstruktur.md §1).</summary>
    public void Flash(TimeSpan? duration = null)
    {
        PseudoClasses.Set(":revealed", true);
        Avalonia.Threading.DispatcherTimer.RunOnce(() => PseudoClasses.Set(":revealed", false), duration ?? TimeSpan.FromSeconds(1.5));
    }

    /// <summary>Ist die Zeile gerade markiert (Tests)?</summary>
    public bool IsFlashing => PseudoClasses.Contains(":revealed");

    private Task ResetAsync() =>
        Path is { } path && Managed.GetContext(this) is { } context ? context.ResetToDefaultAsync(path) : Task.CompletedTask;
}
