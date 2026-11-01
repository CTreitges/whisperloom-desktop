using System.ComponentModel;
using WhisperLoom.App.Secrets;

namespace WhisperLoom.App.Platform.Linux;

/// <summary>Ergebnis: der markierte Text oder das fehlende Werkzeug (Hinweis „bitte installieren“).</summary>
public sealed record PrimaryRead(string? Text, string? MissingTool = null);

/// <summary>
/// Liest die Primärauswahl (zuletzt markierter Text, W2-SEL): Wayland <c>wl-paste --primary --no-newline</c>, X11
/// <c>xclip -o -selection primary</c> bzw. <c>xsel -o -p</c>. Wayland zuerst, wenn <c>WAYLAND_DISPLAY</c> gesetzt ist (XWayland-
/// Programme spiegeln ihre Auswahl dorthin). Fehlt jedes Werkzeug, nennt das Ergebnis das passende zum Installieren.
/// </summary>
// Doku: man wl-paste (wl-clipboard: --primary, --no-newline), man xclip (-o -selection primary), man xsel (-o -p) (abgerufen 2026-11-01).
internal sealed class PrimarySelection(IProcessRunner runner, Func<string, string?> env)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public static IReadOnlyList<(string File, string[] Args)> Tools(bool wayland) => wayland
        ? [("wl-paste", ["--primary", "--no-newline"]), ("xclip", ["-o", "-selection", "primary"]), ("xsel", ["-o", "-p"])]
        : [("xclip", ["-o", "-selection", "primary"]), ("xsel", ["-o", "-p"]), ("wl-paste", ["--primary", "--no-newline"])];

    public async Task<PrimaryRead> ReadAsync(CancellationToken ct)
    {
        var wayland = !string.IsNullOrEmpty(env("WAYLAND_DISPLAY"));
        var found = false;
        foreach (var (file, args) in Tools(wayland))
        {
            ProcessResult result;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(Timeout);
                result = await runner.RunAsync(file, args, null, cts.Token).ConfigureAwait(false);
            }
            catch (Win32Exception)
            {
                continue; // nicht installiert
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                found = true;
                continue; // hängt (kein Display): nächstes Werkzeug
            }
            found = true;
            // Exit ≠ 0 bei leerer Auswahl (wl-paste: „No selection“): leer, kein Fehler.
            return new PrimaryRead(result.ExitCode == 0 ? result.StdOut : "");
        }
        return found ? new PrimaryRead("") : new PrimaryRead(null, wayland ? "wl-clipboard (wl-paste)" : "xclip");
    }
}
