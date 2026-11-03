using System.Text.RegularExpressions;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>
/// Texte nach Richtlinie 05 (Nachher-Audit UX-6): Meldungen ohne „bitte“/„please“ und ohne „ungültig“/„invalid“
/// (§5.3, stattdessen sagen, was zu tun ist), keine gerätespezifischen Verben „klicken“/„click“ (§5.1, „wählen“/„öffnen“).
/// Ausgenommen sind die Hilfetexte (<c>Help*</c>), die Bedienwege erklären, der Funktionsname „Strg+Rechtsklick“ und
/// <c>Error_Budget</c> (Ausnahme D9 in <c>docs/ux-exceptions.md</c>: wörtlich im Firmen-Server zitiert).
/// </summary>
public sealed partial class UxWritingTest
{
    private static readonly HashSet<string> Exceptions = ["Error_Budget"];

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void Messages_say_what_to_do_without_please_invalid_or_click(string lang)
    {
        var rule = lang == "de" ? GermanRule() : EnglishRule();
        var hits = Loc.LoadTable(lang)
            .Where(e => !e.Key.StartsWith("Help", StringComparison.Ordinal) && !Exceptions.Contains(e.Key) && rule.IsMatch(e.Value))
            .Select(e => $"{e.Key}: {e.Value}")
            .ToList();
        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    [GeneratedRegex(@"\bbitte\b|ungültig|(?<!rechts)\bklick|anklick", RegexOptions.IgnoreCase)]
    private static partial Regex GermanRule();

    [GeneratedRegex(@"\bplease\b|\binvalid\b|(?<!right-)\bclick", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishRule();
}
