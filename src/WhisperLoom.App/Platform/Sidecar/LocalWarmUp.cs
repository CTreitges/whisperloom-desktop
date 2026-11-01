using WhisperLoom.App.Services;

namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// Vorladen während der Aufnahme (Port der Entscheidung von <c>TranscriptionEngine.warmUp</c>, Android
/// TranscriptionEngine.kt:91-98): Beginnt eine Aufnahme und wird danach lokal verbessert, startet das mitgelieferte
/// Textmodell schon jetzt — das Laden überlappt mit dem Sprechen. Fehler zeigen sich erst beim Verbessern.
/// </summary>
internal static class LocalWarmUp
{
    public static void Attach(DictationController controller, Func<CancellationToken, Task<bool>> shouldWarmUp, LocalTextEngine text)
    {
        controller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DictationController.State) && controller.State == DictationState.Recording)
            {
                _ = WarmUpAsync(shouldWarmUp, text);
            }
        };
    }

    /// <returns>true, wenn vorgeladen wurde (für Tests).</returns>
    internal static async Task<bool> WarmUpAsync(Func<CancellationToken, Task<bool>> shouldWarmUp, LocalTextEngine text)
    {
        try
        {
            if (text.Engine != LocalTextEngine.Bundled || !await shouldWarmUp(CancellationToken.None).ConfigureAwait(false)) return false;
            await text.WarmUpAsync(CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or HttpRequestException)
        {
            return false; // z. B. Key-Ablage nicht lesbar: das Diktat selbst meldet es
        }
    }
}
