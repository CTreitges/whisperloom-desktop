namespace WhisperLoom.Core.Audio;

/// <summary>Stille nach erkannter Sprache bis zum Auto-Stopp. Port von <c>SpeechPause</c> (<c>agent/WidgetProfile.kt</c>).</summary>
public enum SpeechPause
{
    Short,
    Normal,
    Long,
}

public static class SpeechPauses
{
    public static string Key(this SpeechPause pause) => pause switch
    {
        SpeechPause.Short => "short",
        SpeechPause.Normal => "normal",
        SpeechPause.Long => "long",
        _ => throw new ArgumentOutOfRangeException(nameof(pause), pause, null),
    };

    public static long Ms(this SpeechPause pause) => pause switch
    {
        SpeechPause.Short => 1_200L,
        SpeechPause.Normal => 2_000L,
        SpeechPause.Long => 3_500L,
        _ => throw new ArgumentOutOfRangeException(nameof(pause), pause, null),
    };

    /// <summary>Unbekannt oder fehlend = <see cref="SpeechPause.Normal"/>.</summary>
    public static SpeechPause FromKey(string? key) =>
        Enum.GetValues<SpeechPause>().FirstOrDefault(p => p.Key() == key, SpeechPause.Normal);
}
