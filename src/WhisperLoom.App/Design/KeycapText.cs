using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace WhisperLoom.App.Design;

/// <summary>
/// Satz mit Tastenkappen (einrichtung.html Befund 12): <see cref="Template"/> enthält Platzhalter {0}, {1} …, die als
/// hervorgehobene Läufe aus <see cref="Keys"/> erscheinen (Fläche SurfaceHigh, halbfett). Ein TextBlock, darum bricht der
/// Satz bei 200 % normal um, und Screenreader lesen ihn als einen Text.
/// </summary>
public sealed partial class KeycapText : TextBlock
{
    public static readonly StyledProperty<string?> TemplateProperty = AvaloniaProperty.Register<KeycapText, string?>(nameof(Template));
    public static readonly StyledProperty<IReadOnlyList<string>?> KeysProperty = AvaloniaProperty.Register<KeycapText, IReadOnlyList<string>?>(nameof(Keys));

    public KeycapText() => TextWrapping = TextWrapping.Wrap;

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? Template { get => GetValue(TemplateProperty); set => SetValue(TemplateProperty, value); }

    public IReadOnlyList<string>? Keys { get => GetValue(KeysProperty); set => SetValue(KeysProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TemplateProperty || change.Property == KeysProperty)
        {
            Build();
        }
    }

    /// <summary>Teile der Vorlage: Text bzw. Index einer Taste (Tests).</summary>
    internal static IEnumerable<(string Text, bool IsKey)> Parts(string template, IReadOnlyList<string> keys)
    {
        var last = 0;
        foreach (Match m in Placeholder().Matches(template))
        {
            if (m.Index > last)
            {
                yield return (template[last..m.Index], false);
            }
            var i = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            yield return i < keys.Count ? (keys[i], true) : (m.Value, false);
            last = m.Index + m.Length;
        }
        if (last < template.Length)
        {
            yield return (template[last..], false);
        }
    }

    private void Build()
    {
        var inlines = Inlines ??= [];
        inlines.Clear();
        foreach (var (text, isKey) in Parts(Template ?? "", Keys ?? []))
        {
            var run = new Run(isKey ? " " + text + " " : text);
            if (isKey)
            {
                run.FontWeight = FontWeight.SemiBold;
                run.Bind(TextElement.BackgroundProperty, this.GetResourceObservable("Loom.SurfaceHigh"));
            }
            inlines.Add(run);
        }
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();
}
