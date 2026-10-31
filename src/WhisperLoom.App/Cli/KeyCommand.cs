using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Cli;

/// <summary>
/// <c>key set &lt;name&gt;</c> (Wert von stdin) und <c>key delete &lt;name&gt;</c>. Der Wert wird nie ausgegeben.
/// Von IT gesperrte Keys (<c>secrets.&lt;name&gt;</c> in der Richtlinie) lehnt der Befehl ab, wie das Einstellungsfenster.
/// </summary>
internal static class KeyCommand
{
    /// <returns>Exit-Code: 0 ok, 1 Fehler oder gesperrt.</returns>
    public static async Task<int> RunAsync(string action, string name, Func<string> readValue, ISecretStore store, PolicySettings policy,
        TextWriter output, TextWriter errors, CancellationToken ct)
    {
        if (policy.IsLocked("secrets." + name))
        {
            errors.WriteLine($"error Key {name} ist von IT festgelegt (Richtlinie) und lässt sich nicht ändern.");
            return 1;
        }
        try
        {
            if (action == "set")
            {
                await store.SetAsync(name, readValue().Trim(), ct);
            }
            else
            {
                await store.DeleteAsync(name, ct);
            }
            output.WriteLine($"ok {action} {name} ({store.Kind})");
            return 0;
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            errors.WriteLine(e.Message);
            return 1;
        }
    }
}
