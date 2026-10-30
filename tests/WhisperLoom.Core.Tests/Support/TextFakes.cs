using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Tests.Support;

/// <summary>
/// Fake-Keys in der Form aus gemeinsam.md (nie echte Keys). Die Kotlin-Tests nutzen „sk-x“, „gsk“, „xi“ usw.
/// </summary>
public static class Keys
{
    public const string K1 = "test-key-not-real-1";
    public const string K2 = "test-key-not-real-2";
    public const string K3 = "test-key-not-real-3";
    public const string K4 = "test-key-not-real-4";
}

/// <summary>Grund für Tests, deren Kotlin-Erwartung echte Text-Bausteine aus WLD-P1a braucht.</summary>
public static class P1a
{
    public const string Reason = "Needs P1a: echte Text-Bausteine (TextPolisher/RefinePrompt) — WLD-INT schaltet scharf";

    /// <summary>Platzhalter für den echten Prompt-Builder aus WLD-P1a.</summary>
    public static IRefinePromptBuilder Prompts => throw new NotImplementedException("kommt mit WLD-P1a");

    /// <summary>Platzhalter für den echten TextPolisher aus WLD-P1a.</summary>
    public static ITextPolisher Polisher => throw new NotImplementedException("kommt mit WLD-P1a");
}

/// <summary>
/// Prompt-Builder für Tests: der System-Prompt nennt seine Argumente, damit die Tests prüfen können, WAS die
/// Pipeline verlangt (Stufe, Sprache, Füllwörter, Absätze, kurz). Der Text selbst ist Sache von WLD-P1a.
/// </summary>
public sealed class FakePromptBuilder : IRefinePromptBuilder
{
    public string Build(RefineMode mode, bool german, bool smartFillers, bool paragraphs = true, bool isShort = false)
    {
        if (mode == RefineMode.Off) throw new ArgumentException("Stufe aus hat keinen Prompt", nameof(mode));
        return $"SYSTEM mode={mode.Key()} german={german} smartFillers={smartFillers} paragraphs={paragraphs} short={isShort}";
    }

    public string UserText(string raw, bool german) => german ? $"<diktat>\n{raw}\n</diktat>" : $"<dictation>\n{raw}\n</dictation>";
}

/// <summary>
/// Polisher für Tests: gibt den Text unverändert zurück und merkt sich jeden Aufruf mit Optionen — so prüfen
/// die Tests, ob die Pipeline den Rohtext mit den vollen Regeln oder die KI-Fassung mit den KI-Regeln schickt.
/// </summary>
public sealed class RecordingPolisher(params string[] fillers) : ITextPolisher
{
    public List<(string Text, PolishOptions Options)> Calls { get; } = [];

    public (string Text, PolishOptions Options) Last => Calls[^1];

    public string Polish(string raw, PolishOptions options)
    {
        lock (Calls) Calls.Add((raw, options));
        return raw;
    }

    public IReadOnlyList<string> BuiltinFillers(string language) => fillers;
}
