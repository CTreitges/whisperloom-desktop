namespace WhisperLoom.Core.Text;

/// <summary>Regex-Bausteine, die sich zwischen Java und .NET unterscheiden.</summary>
internal static class TextPatterns
{
    /// <summary>
    /// Javas <c>\s</c> (ohne UNICODE_CHARACTER_CLASS): nur ASCII-Leerraum. .NETs <c>\s</c> trifft auch
    /// U+00A0 und andere Unicode-Leerzeichen — die ließe Android stehen.
    /// </summary>
    public const string Ws = @"[ \t\n\x0B\f\r]";
}
