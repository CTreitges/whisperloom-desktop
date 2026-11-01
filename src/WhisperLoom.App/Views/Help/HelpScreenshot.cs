using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Views.Help;

/// <summary>
/// Screenshot der Hilfe in der Sprache der Oberfläche und der Variante des Fensters (hell/dunkel), wechselt beides live mit.
/// Höchstens in Originalgröße, schmaler skaliert er mit. Fehlt die Datei, bleibt die Fläche leer (<c>HelpCatalogTest</c>
/// verhindert das).
/// </summary>
public sealed class HelpScreenshot : Image
{
    public static readonly StyledProperty<string?> ShotProperty = AvaloniaProperty.Register<HelpScreenshot, string?>(nameof(Shot));

    public HelpScreenshot() => ActualThemeVariantChanged += (_, _) => Reload();

    protected override Type StyleKeyOverride => typeof(Image);

    /// <summary>Name aus <c>screens.json</c>.</summary>
    public string? Shot { get => GetValue(ShotProperty); set => SetValue(ShotProperty, value); }

    /// <summary>Gerade gezeigte Datei (für Tests).</summary>
    public string? CurrentPath { get; private set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Loc.Instance.PropertyChanged += OnLanguageChanged;
        Reload();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShotProperty)
        {
            Reload();
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => Reload();

    private void Reload()
    {
        var theme = ActualThemeVariant == ThemeVariant.Dark ? "dark" : "light";
        var path = Shot is { } name ? HelpShot.AssetPath(name, Loc.Instance.Language, theme) : null;
        if (path == CurrentPath)
        {
            return;
        }
        CurrentPath = path;
        var old = Source as IDisposable;
        Source = path is not null && AssetLoader.Exists(new Uri(path)) ? new Bitmap(AssetLoader.Open(new Uri(path))) : null;
        MaxWidth = Source is Bitmap bitmap ? bitmap.Size.Width : 0;
        old?.Dispose();
    }
}
