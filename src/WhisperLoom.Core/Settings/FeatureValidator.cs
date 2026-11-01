using System.Text.Json;
using System.Text.RegularExpressions;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Settings;

/// <summary>Prüfung der W2-Abschnitte (<c>providers</c>, <c>offline.customModels</c>, <c>local</c>, <c>management</c>) für <see cref="SyncValidator"/>.</summary>
internal static partial class FeatureValidator
{
    private static readonly string[] TextEngines = ["ollama", "bundled", "server"];

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,31}$")]
    private static partial Regex CustomId();

    public static void Validate(WhisperLoomSettings s, List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < s.Providers.Custom.Count; i++)
        {
            var p = s.Providers.Custom[i];
            var path = $"providers.custom[{i}]";
            if (p.Id is null || !CustomId().IsMatch(p.Id) || !ids.Add(p.Id))
            {
                errors.Add($"{path}.id: eindeutig, a-z 0-9 _ - (höchstens 32)");
            }
            if (string.IsNullOrWhiteSpace(p.Name))
            {
                errors.Add($"{path}.name: nicht leer");
            }
            if (!CustomProvider.Protocols.Contains(p.Protocol))
            {
                errors.Add($"{path}.protocol: eines von {string.Join(", ", CustomProvider.Protocols)}");
            }
            Url(errors, $"{path}.baseUrl", p.BaseUrl);
            if ((p.Models ?? []).Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"{path}.models: nur nicht leere Modell-IDs");
            }
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < s.Offline.CustomModels.Count; i++)
        {
            var m = s.Offline.CustomModels[i];
            if (string.IsNullOrWhiteSpace(m.Name) || !names.Add(m.Name))
            {
                errors.Add($"offline.customModels[{i}].name: eindeutig, nicht leer");
            }
            if (string.IsNullOrWhiteSpace(m.Path))
            {
                errors.Add($"offline.customModels[{i}].path: nicht leer");
            }
        }
        if (s.Offline.Model.StartsWith(OfflineSettings.CustomModelPrefix, StringComparison.Ordinal)
            && !names.Contains(s.Offline.Model[OfflineSettings.CustomModelPrefix.Length..]))
        {
            errors.Add("offline.model: \"custom:<name>\" nennt keinen Eintrag in offline.customModels");
        }

        Local(s, errors);

        if (!ManagementSettings.KeyModes.Contains(s.Management.KeyMode))
        {
            errors.Add($"management.keyMode: eines von {string.Join(", ", ManagementSettings.KeyModes)}");
        }
        if (!ManagementSettings.States.Contains(s.Management.State))
        {
            errors.Add($"management.state: eines von {string.Join(", ", ManagementSettings.States)}");
        }
        Url(errors, "management.serverUrl", s.Management.ServerUrl, optional: true);
    }

    /// <summary>Eigener Eintrag als Anbieter eines Zugangs: <c>custom:&lt;id&gt;</c> mit passender Fähigkeit.</summary>
    public static bool IsCustomProvider(WhisperLoomSettings s, string id, bool stt) =>
        id.StartsWith(ProvidersSettings.CustomPrefix, StringComparison.Ordinal) &&
        s.Providers.Custom.FirstOrDefault(c => c.Id == id[ProvidersSettings.CustomPrefix.Length..]) is { } p &&
        (stt ? p.HasStt : p.HasLlm);

    /// <summary><c>local</c> gehört der App (<c>LocalSettings</c>, im Vertrag nur als JSON): <c>textEngine</c> und <c>servers</c>.</summary>
    private static void Local(WhisperLoomSettings s, List<string> errors)
    {
        if (s.Extra?.LastOrDefault(e => e.Key.Equals("local", StringComparison.OrdinalIgnoreCase)).Value is not { ValueKind: JsonValueKind.Object } local)
        {
            return;
        }
        foreach (var field in local.EnumerateObject())
        {
            if (field.Name.Equals("textEngine", StringComparison.OrdinalIgnoreCase)
                && !(field.Value.ValueKind == JsonValueKind.String && TextEngines.Contains(field.Value.GetString())))
            {
                errors.Add($"local.textEngine: eines von {string.Join(", ", TextEngines)}");
            }
            if (field.Name.Equals("servers", StringComparison.OrdinalIgnoreCase))
            {
                Servers(field.Value, errors);
            }
        }
    }

    private static void Servers(JsonElement servers, List<string> errors)
    {
        if (servers.ValueKind != JsonValueKind.Array)
        {
            errors.Add("local.servers: Liste");
            return;
        }
        var i = 0;
        foreach (var server in servers.EnumerateArray())
        {
            LocalServer? entry = null;
            try
            {
                entry = server.Deserialize<LocalServer>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch (JsonException)
            {
            }
            if (entry is null)
            {
                errors.Add($"local.servers[{i}]: Objekt mit name, kind, baseUrl, model");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    errors.Add($"local.servers[{i}].name: nicht leer");
                }
                if (!LocalServer.Kinds.Contains(entry.Kind))
                {
                    errors.Add($"local.servers[{i}].kind: \"stt\" oder \"text\"");
                }
                Url(errors, $"local.servers[{i}].baseUrl", entry.BaseUrl ?? "");
            }
            i++;
        }
    }

    /// <summary>http(s)-URL; http nur zu privaten Adressen (Regel des eigenen Servers, <see cref="ServerUrlCheck"/>).</summary>
    private static void Url(List<string> errors, string path, string url, bool optional = false)
    {
        if (optional && url == "")
        {
            return;
        }
        if (!(Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"))
        {
            errors.Add($"{path}: http(s)-URL");
        }
        else if (ProviderCatalog.Find("custom") is { } custom && ServerUrlCheck.Check(url, custom) is { } problem &&
                 (problem.Severity == ServerUrlCheck.Severity.Error || problem.Message == ServerUrlCheck.MsgPublicHttp))
        {
            errors.Add($"{path}: {problem.Message}");
        }
    }
}
