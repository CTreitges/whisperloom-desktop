using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.Core.Tests.Pipeline;

/// <summary>
/// <see cref="RefineSkip.RaceAsync{T}"/> (Prüfer-Fund D3, TS-WTRAY): Ein Fehler, den der Ausweg nicht verursacht haben kann
/// (der Server hat mit Fehlerstatus geantwortet, das Modell hat unbrauchbar geantwortet), bleibt ein Fehler, auch wenn der
/// Ausweg beim Fangen schon gesetzt ist. Ein Abbruch durch den Ausweg bleibt „übersprungen“.
/// </summary>
/// <remarks>
/// Der Wettlauf: die Arbeit ist schon mit Fehler fertig, dann kommt Esc, dann erst wertet <c>RaceAsync</c> aus. Nachgestellt
/// mit einer bereits gescheiterten Aufgabe und gesetztem Ausweg, bevor <c>RaceAsync</c> wartet (beide fertig: die Arbeit gewinnt).
/// </remarks>
public sealed class RefineSkipRaceTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Func<CancellationToken, Task<string>> FailsThenEsc(RefineSkip skip, Exception error) => _ =>
    {
        var failed = Task.FromException<string>(error);
        skip.Skip();
        return failed;
    };

    [Fact]
    public async Task Http_Fehler_vor_Esc_kommt_durch()
    {
        var skip = new RefineSkip();
        var e = await Assert.ThrowsAsync<ApiHttpException>(() => skip.RaceAsync(FailsThenEsc(skip, new ApiHttpException(500, "HTTP 500")), Ct));
        Assert.Equal(500, e.StatusCode);
    }

    [Fact]
    public async Task Abgelehnte_Antwort_vor_Esc_kommt_durch()
    {
        var skip = new RefineSkip();
        await Assert.ThrowsAsync<RefineRejectedException>(() =>
            skip.RaceAsync(FailsThenEsc(skip, new RefineRejectedException("statt den Text zu bearbeiten")), Ct));
    }

    [Fact]
    public async Task Eingepackter_Http_Fehler_des_lokalen_Servers_vor_Esc_kommt_durch()
    {
        var skip = new RefineSkip();
        await Assert.ThrowsAsync<RefineFailedException>(() =>
            skip.RaceAsync(FailsThenEsc(skip, new RefineFailedException(RefinePlan.MsgLocalFailed, new ApiHttpException(503, "HTTP 503"))), Ct));
    }

    [Fact]
    public async Task Abbruch_durch_den_Ausweg_ist_uebersprungen()
    {
        var skip = new RefineSkip();
        var result = await skip.RaceAsync<string>(async token =>
        {
            await Task.Yield();
            skip.Skip();
            await Task.Delay(Timeout.Infinite, token);
            return "nie";
        }, Ct);
        Assert.Null(result);
    }

    [Fact]
    public async Task Abbruch_Fehler_der_Arbeit_nach_Esc_ist_uebersprungen()
    {
        // Die Arbeit endet wegen des Auswegs mit einem Abbruch (Online-Anfrage, lokales Modell): übersprungen wie bisher.
        var skip = new RefineSkip();
        Assert.Null(await skip.RaceAsync(FailsThenEsc(skip, new OperationCanceledException()), Ct));
    }

    [Fact]
    public async Task Anderer_Fehler_nach_Esc_bleibt_uebersprungen()
    {
        // Lokales Modell, das beim Abbruch mit eigenem Fehler endet (Prozess beendet): bleibt wie bisher „übersprungen“.
        var skip = new RefineSkip();
        Assert.Null(await skip.RaceAsync(FailsThenEsc(skip, new IOException("Prozess beendet")), Ct));
    }

    [Fact]
    public async Task Fehler_ohne_Ausweg_kommt_durch()
    {
        var skip = new RefineSkip();
        await Assert.ThrowsAsync<IOException>(() => skip.RaceAsync<string>(_ => Task.FromException<string>(new IOException("kaputt")), Ct));
    }
}
