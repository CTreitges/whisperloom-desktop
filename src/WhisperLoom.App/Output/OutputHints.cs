using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Output;

namespace WhisperLoom.App.Output;

/// <summary>Senke, die wissen kann, dass die letzte Ausgabe in ein Passwortfeld ging (dann kein Verlaufseintrag).</summary>
public interface IPrivateAwareOutput
{
    bool LastWasPrivate { get; }
}

/// <summary>
/// Senke ohne Einfügen (Linux, Tray/CLI unter Windows): der Text landet über die innere Senke in der Zwischenablage,
/// danach der Hinweis „Kopiert – Strg+V“ — dieselbe Core-Entscheidung wie beim Einfügen, mit Fokus „unbekannt“ und ohne
/// Einfügen (<see cref="OutputDecision"/>, W2-OUT).
/// </summary>
/// <param name="show">Zeigt den Hinweis (Windows: Toast, Linux: notify-send); darf nichts tun.</param>
public sealed class HintingOutputSink(IOutputSink inner, Func<OutputSettings> settings, Action<OutputHint> show) : IOutputSink
{
    public string Kind => inner.Kind;

    /// <summary>Plan der letzten Ausgabe (Status/Smoke); null = noch keine.</summary>
    public OutputPlan? LastPlan { get; private set; }

    public async Task WriteAsync(string text, CancellationToken ct)
    {
        await inner.WriteAsync(text, ct).ConfigureAwait(false);
        var plan = OutputDecision.Decide(FocusKind.Unknown, InsertAttempt.NotTried, settings().AlwaysCopy);
        LastPlan = plan;
        if (plan.Hint != OutputHint.None)
        {
            show(plan.Hint);
        }
    }
}

/// <summary>Lokalisierungs-Schlüssel der Hinweise.</summary>
public static class OutputHints
{
    public static string Key(OutputHint hint) => hint switch
    {
        OutputHint.Copied => "Output_Hint_Copied",
        OutputHint.PasswordField => "Output_Hint_Password",
        _ => "",
    };

    public static string Name(OutputHint hint) => hint switch
    {
        OutputHint.Copied => "copied",
        OutputHint.PasswordField => "password",
        _ => "none",
    };
}
