using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Design;

/// <summary>Art einer Hinweis-Zeile: Info (neutral), Warnung, verwaltet (Firmen-Tönung), Fehler.</summary>
public enum HintKind
{
    Info,
    Warning,
    Managed,
    Error,
}

/// <summary>
/// Bausteine „Hinweis-Zeile“ und „Verwaltet-Zeile“ (<see cref="HintKind.Managed"/>, zielstruktur.md §7): Symbol 20 px plus
/// Text, optional ein Textknopf rechts („Zur Erkennung“, „Zur Firma“). Text entweder fest (<see cref="Text"/>) oder über
/// <see cref="TextKey"/> aus der Lokalisierung, der Knopf über <see cref="ActionKey"/> und <see cref="ActionCommand"/>.
/// </summary>
public class HintRow : TemplatedControl
{
    public static readonly StyledProperty<string?> TextKeyProperty = AvaloniaProperty.Register<HintRow, string?>(nameof(TextKey));
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<HintRow, string?>(nameof(Text));
    public static readonly StyledProperty<HintKind> KindProperty = AvaloniaProperty.Register<HintRow, HintKind>(nameof(Kind));
    public static readonly StyledProperty<string?> ActionKeyProperty = AvaloniaProperty.Register<HintRow, string?>(nameof(ActionKey));
    public static readonly StyledProperty<ICommand?> ActionCommandProperty = AvaloniaProperty.Register<HintRow, ICommand?>(nameof(ActionCommand));
    public static readonly StyledProperty<object?> ActionParameterProperty = AvaloniaProperty.Register<HintRow, object?>(nameof(ActionParameter));

    public static readonly DirectProperty<HintRow, string> ShownTextProperty =
        AvaloniaProperty.RegisterDirect<HintRow, string>(nameof(ShownText), h => h.ShownText);
    public static readonly DirectProperty<HintRow, string> ActionTextProperty =
        AvaloniaProperty.RegisterDirect<HintRow, string>(nameof(ActionText), h => h.ActionText);

    private string _shownText = "";
    private string _actionText = "";

    public string? TextKey { get => GetValue(TextKeyProperty); set => SetValue(TextKeyProperty, value); }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public HintKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    /// <summary>Beschriftung des Textknopfs rechts; ohne Schlüssel kein Knopf.</summary>
    public string? ActionKey { get => GetValue(ActionKeyProperty); set => SetValue(ActionKeyProperty, value); }

    public ICommand? ActionCommand { get => GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }

    public object? ActionParameter { get => GetValue(ActionParameterProperty); set => SetValue(ActionParameterProperty, value); }

    public string ShownText { get => _shownText; private set => SetAndRaise(ShownTextProperty, ref _shownText, value); }

    public string ActionText { get => _actionText; private set => SetAndRaise(ActionTextProperty, ref _actionText, value); }

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
        if (change.Property == TextKeyProperty || change.Property == TextProperty || change.Property == ActionKeyProperty)
        {
            Refresh();
        }
        if (change.Property == KindProperty)
        {
            PseudoClasses.Set(":warning", Kind == HintKind.Warning);
            PseudoClasses.Set(":managed", Kind == HintKind.Managed);
            PseudoClasses.Set(":error", Kind == HintKind.Error);
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        ShownText = Text ?? (TextKey is { } key ? Loc.Instance[key] : "");
        ActionText = ActionKey is { } action ? Loc.Instance[action] : "";
        PseudoClasses.Set(":action", ActionText.Length > 0);
    }
}
