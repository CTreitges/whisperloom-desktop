using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Design;

/// <summary>Gemeinsame Logik von Gruppe und Seite: Titel aus der Lokalisierung, Stufen der enthaltenen Zeilen.</summary>
internal static class ManagedRows
{
    /// <summary>Zeilen mit Pfad unterhalb von <paramref name="root"/> (logischer Baum, auch in zugeklapptem „Erweitert“).</summary>
    public static IEnumerable<SettingRow> Of(ILogical root) =>
        root.GetLogicalDescendants().OfType<SettingRow>().Where(r => r.Path is not null);

    public static ManagedLevel LevelOf(Control at, SettingRow row) =>
        Managed.GetContext(at)?.Source.LevelOf(row.Path!) ?? ManagedLevel.Free;
}

/// <summary>
/// Baustein „Gruppe“: Überschrift, abgerundete Fläche mit Zeilen (Inhalt = Panel mit <see cref="SettingRow"/>), optional
/// Fußtext. Sind alle Zeilen mit Pfad Fest, verschwindet die ganze Gruppe.
/// </summary>
public class SettingsGroup : HeaderedContentControl
{
    public static readonly StyledProperty<string?> TitleKeyProperty = AvaloniaProperty.Register<SettingsGroup, string?>(nameof(TitleKey));
    public static readonly StyledProperty<string?> FooterKeyProperty = AvaloniaProperty.Register<SettingsGroup, string?>(nameof(FooterKey));

    public static readonly DirectProperty<SettingsGroup, bool> IsShownProperty =
        AvaloniaProperty.RegisterDirect<SettingsGroup, bool>(nameof(IsShown), g => g.IsShown);
    public static readonly DirectProperty<SettingsGroup, string> FooterProperty =
        AvaloniaProperty.RegisterDirect<SettingsGroup, string>(nameof(Footer), g => g.Footer);

    private bool _isShown = true;
    private string _footer = "";

    public string? TitleKey { get => GetValue(TitleKeyProperty); set => SetValue(TitleKeyProperty, value); }

    public string? FooterKey { get => GetValue(FooterKeyProperty); set => SetValue(FooterKeyProperty, value); }

    public bool IsShown { get => _isShown; private set => SetAndRaise(IsShownProperty, ref _isShown, value); }

    public string Footer { get => _footer; private set => SetAndRaise(FooterProperty, ref _footer, value); }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Loc.Instance.PropertyChanged += OnLanguageChanged;
        Refresh();
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
        base.OnDetachedFromLogicalTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleKeyProperty || change.Property == FooterKeyProperty || change.Property == Managed.ContextProperty)
        {
            Refresh();
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (TitleKey is { } key)
        {
            Header = Loc.Instance[key];
        }
        Footer = FooterKey is { } footer ? Loc.Instance[footer] : "";
        PseudoClasses.Set(":footer", Footer.Length > 0);
        var rows = ManagedRows.Of(this).ToList();
        IsShown = rows.Count == 0 || rows.Any(r => ManagedRows.LevelOf(this, r) != ManagedLevel.Fixed);
    }
}

/// <summary>
/// Baustein „Seite“: Titel, darunter die Verwaltet-Zeile (sobald eine Zeile der Seite Fest ist), dann die Gruppen.
/// Rollt selbst (ScrollViewer in der Vorlage).
/// </summary>
public class SettingsPage : ContentControl
{
    public static readonly StyledProperty<string?> TitleKeyProperty = AvaloniaProperty.Register<SettingsPage, string?>(nameof(TitleKey));

    public static readonly DirectProperty<SettingsPage, string> TitleProperty =
        AvaloniaProperty.RegisterDirect<SettingsPage, string>(nameof(Title), p => p.Title);
    public static readonly DirectProperty<SettingsPage, string> ManagedTextProperty =
        AvaloniaProperty.RegisterDirect<SettingsPage, string>(nameof(ManagedText), p => p.ManagedText);
    public static readonly DirectProperty<SettingsPage, bool> HasManagedRowsProperty =
        AvaloniaProperty.RegisterDirect<SettingsPage, bool>(nameof(HasManagedRows), p => p.HasManagedRows);

    private string _title = "";
    private string _managedText = "";
    private bool _hasManagedRows;

    public string? TitleKey { get => GetValue(TitleKeyProperty); set => SetValue(TitleKeyProperty, value); }

    public string Title { get => _title; private set => SetAndRaise(TitleProperty, ref _title, value); }

    /// <summary>„Einige Einstellungen verwaltet …“.</summary>
    public string ManagedText { get => _managedText; private set => SetAndRaise(ManagedTextProperty, ref _managedText, value); }

    public bool HasManagedRows { get => _hasManagedRows; private set => SetAndRaise(HasManagedRowsProperty, ref _hasManagedRows, value); }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Loc.Instance.PropertyChanged += OnLanguageChanged;
        Refresh();
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
        base.OnDetachedFromLogicalTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleKeyProperty || change.Property == Managed.ContextProperty)
        {
            Refresh();
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        Title = TitleKey is { } key ? Loc.Instance[key] : "";
        var source = Managed.GetContext(this)?.Source;
        HasManagedRows = source?.ManagerName is not null && ManagedRows.Of(this).Any(r => ManagedRows.LevelOf(this, r) == ManagedLevel.Fixed);
        ManagedText = source?.ManagerName is { } name ? Loc.Instance.Format("Managed_Note", name) : "";
    }
}
