using System.Text.Json.Nodes;
using WhisperLoom.App.Audio;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Instance;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Management;
using WhisperLoom.App.Output;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.App.Secrets;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.History;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App;

/// <summary>Stand für <c>status --json</c>: Diktat, Verlauf, Keys (nur vorhanden ja/nein), Richtlinie, Firmen-Anbindung, Lokales.</summary>
public sealed partial class AppHost
{
    private static JsonObject PolicyStatus(PolicySettings p) => new()
    {
        ["active"] = p.IsActive,
        ["locked"] = new JsonArray([.. p.Locked.Select(l => (JsonNode)l)]),
        ["historyEnabled"] = p.HistoryEnabled,
        ["updatesEnabled"] = p.UpdatesEnabled,
        ["allowedProviders"] = p.AllowedProviders is null ? null : new JsonArray([.. p.AllowedProviders.Select(a => (JsonNode)a)]),
    };
    /// <summary>Firmen-Anbindung für <c>status --json</c>: Zustand ohne Token; <c>notice</c> = Hinweis bei Sperre bzw. Widerruf.</summary>
    private JsonObject ManagementStatus(ManagementSettings m) => new()
    {
        ["state"] = m.State,
        ["serverUrl"] = m.ServerUrl,
        ["tenantName"] = m.TenantName,
        ["deviceId"] = m.DeviceId,
        ["keyMode"] = m.KeyMode,
        ["lastSync"] = m.LastSync,
        ["canLeave"] = Manager?.CanLeave,
        ["lastError"] = Manager?.LastError,
        ["notice"] = m.State is "blocked" or "revoked" ? ManagementTexts.Notice(m) : null,
    };

    /// <summary>Lokale Erkennung für <c>status --json</c>: Engine, Begleitprozess (PID, Backend, Gerät), Ollama-Modell.</summary>
    private JsonObject LocalStatus()
    {
        var server = Local?.Stt.Server;
        var last = Local?.Stt.LastBackend;
        return new JsonObject
        {
            ["engine"] = Controller.Settings.Engine,
            ["model"] = Controller.Settings.Offline.Model,
            ["sidecarPid"] = server is { HasExited: false } ? server.Pid : null,
            ["textEngine"] = Local?.Text.Engine,
            ["llamaPid"] = Local?.Bundled.Server is { HasExited: false } llama ? llama.Pid : null,
            ["llamaBackend"] = Local?.Bundled.Server?.Backend,
            ["backend"] = last?.Backend,
            ["device"] = last?.Device,
            ["ollamaModel"] = LocalSettings.Of(Controller.Settings).OllamaModel,
        };
    }

    /// <summary>Stand für <c>status --json</c>. Keys nur als vorhanden/fehlt, nie der Wert.</summary>
    public async Task<JsonObject> StatusAsync()
    {
        var keys = new JsonObject();
        foreach (var name in SecretNames.All)
        {
            bool? present;
            try
            {
                present = await Secrets.GetAsync(name, CancellationToken.None) is not null;
            }
            catch (IOException)
            {
                present = null;
            }
            keys[name] = present;
        }
        var history = await Controller.HistoryIfEnabledAsync();
        var latest = history.Count > 0 ? history[0] : null;
        var c = Controller;
        return new JsonObject
        {
            ["running"] = true,
            ["pid"] = Environment.ProcessId,
            ["version"] = typeof(AppHost).Assembly.GetName().Version?.ToString(),
            ["state"] = c.State.ToString().ToLowerInvariant(),
            ["stage"] = c.Stage.Key(),
            ["language"] = c.Language,
            ["uiLanguage"] = Loc.Instance.Language,
            ["elapsedMs"] = (long)c.Elapsed.TotalMilliseconds,
            ["lastRaw"] = c.LastRaw,
            ["lastResult"] = c.LastResult,
            ["lastModel"] = c.LastRefined?.Model,
            ["lastError"] = c.LastErrorClass,
            ["history"] = new JsonObject
            {
                ["enabled"] = c.Settings.History.Enabled,
                ["count"] = history.Count,
                ["latest"] = latest is null ? null : new JsonObject
                {
                    ["raw"] = latest.Raw,
                    ["text"] = latest.Text,
                    ["stage"] = latest.Refinement.Mode.Key(),
                    ["stt"] = latest.SttLabel,
                    ["source"] = latest.Source,
                },
            },
            ["secretStore"] = Secrets.Kind,
            ["keys"] = keys,
            ["policy"] = PolicyStatus(c.Policy),
            ["management"] = ManagementStatus(c.Settings.Management),
            ["dataDir"] = Options.DataDir,
            ["output"] = Options.OutputFile is null ? "clipboard" : "file",
            ["lastHint"] = LastHint,
            ["copies"] = new JsonObject { ["ok"] = CopyCount.Ok, ["failed"] = CopyCount.Failed },
            ["local"] = LocalStatus(),
            ["input"] = InputStatus?.Invoke(),
            ["recent"] = new JsonArray([.. Features.Reinsert.RecentHistory.Take(history).Select(e => (JsonNode)new JsonObject
            {
                ["id"] = e.Id,
                ["label"] = Features.Reinsert.RecentHistory.Label(e),
                ["stage"] = e.Refinement.Mode.Key(),
            })]),
        };
    }
}
