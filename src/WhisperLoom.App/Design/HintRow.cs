using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Design;

/// <summary>Art einer Hinweis-Zeile: Info (neutral), Warnung, verwaltet (Firmen-Tönung).</summary>
public enum HintKind
{
    Info,
    Warning,
    Managed,
}

/// <summary>
/// Bausteine „Hinweis-Zeile“ und „Verwaltet-Zeile“ (<see cref="HintKind.Managed"/>): Symbol plus Text, ohne Steuerelement.
/// Text entweder fest (<see cref="Text"/>) oder über <see cref="TextKey"/> aus der Lokalisierung.
/// </summary>
public class HintRow : TemplatedControl
{
    public static readonly StyledProperty<string?> TextKeyProperty = AvaloniaProperty.Register<HintRow, string?>(nameof(TextKey));
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<HintRow, string?>(nameof(Text));
    public static readonly StyledProperty<HintKind> KindProperty = AvaloniaProperty.Register<HintRow, HintKind>(nameof(Kind));

    public static readonly DirectProperty<HintRow, string> ShownTextProperty =
        AvaloniaProperty.RegisterDirect<HintRow, string>(nameof(ShownText), h => h.ShownText);

    private string _shownText = "";

    public string? TextKey { get => GetValue(TextKeyProperty); set => SetValue(TextKeyProperty, value); }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public HintKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public string ShownText { get => _shownText; private set => SetAndRaise(ShownTextProperty, ref _shownText, value); }

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
        if (change.Property == TextKeyProperty || change.Property == TextProperty)
        {
            Refresh();
        }
        if (change.Property == KindProperty)
        {
            PseudoClasses.Set(":warning", Kind == HintKind.Warning);
            PseudoClasses.Set(":managed", Kind == HintKind.Managed);
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh() => ShownText = Text ?? (TextKey is { } key ? Loc.Instance[key] : "");
}
