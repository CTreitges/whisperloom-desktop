using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using WhisperLoom.App.Management;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Management;

/// <summary>
/// Firmen-Server im Speicher nach <c>docs/server-api.md</c> (§3.1–§3.5): Enrollment per Code, Gerätezustand, Konfiguration mit
/// ETag/304, Token-Rotation (altes Token gilt bis zur ersten Nutzung des neuen), Abmelden, Fehlerformat §1.2. Nur Fake-Werte.
/// </summary>
internal sealed class FakeManagementServer : HttpMessageHandler
{
    public const string Url = "https://wl.example.test";
    public const string Code = "ABCD-EFGH-JKMN-PQRS";

    private int _tokens;

    public sealed class Device
    {
        public required string Id { get; init; }
        public string State { get; set; } = "pending";
        public string Token { get; set; } = "";
        public string? OldToken { get; set; }
        public bool Rotate { get; set; }
        public string Name { get; set; } = "";
    }

    public List<Device> Devices { get; } = [];

    public List<(string Method, string Path, string? Auth, string? IfNoneMatch)> Requests { get; } = [];

    public bool AutoApprove { get; set; }

    public bool Offline { get; set; }

    public bool TenantBlocked { get; set; }

    public string TenantName { get; set; } = "Muster GmbH";

    public string KeyMode { get; set; } = "proxy";

    /// <summary>Werte der Richtlinie (Teil-Datei) und Stufen; im Proxy-Modus ergänzt der Server die Zugänge wie SRV1.</summary>
    public JsonObject Settings { get; set; } = new() { ["schemaVersion"] = 1 };

    public JsonObject Levels { get; set; } = [];

    public JsonObject Policy { get; set; } = [];

    /// <summary>Verteilt-Modus: Zugang/Anbieter → Key.</summary>
    public JsonObject Secrets { get; set; } = [];

    public Device Only => Devices.Single();

    public string ProxyUrl => Url + "/api/v1/proxy";

    /// <summary>Andere Proxy-Adresse in der Konfiguration (Server mit abweichender WL_BASE_URL).</summary>
    public string? ProxyOverride { get; set; }

    /// <summary>Gerät per „Portal“ freigeben bzw. sperren.</summary>
    public void Set(string state) => Only.State = state;

    public HttpClient Client() => new(this);

    public ManagementApi Api() => new(Client(), "windows", "1.0.0");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var auth = request.Headers.Authorization?.Parameter;
        var etag = request.Headers.TryGetValues("If-None-Match", out var v) ? v.Single() : null;
        Requests.Add((request.Method.Method, path, auth, etag));
        if (Offline)
        {
            throw new HttpRequestException("Verbindung abgelehnt", null, HttpStatusCode.ServiceUnavailable);
        }
        var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct)) as JsonObject;
        return (request.Method.Method, path) switch
        {
            ("POST", "/api/v1/enroll") => Enroll(body!),
            ("GET", "/api/v1/device") => WithDevice(auth, d => Json(200, DeviceJson(d))),
            ("GET", "/api/v1/config") => WithDevice(auth, d => Config(d, etag)),
            ("POST", "/api/v1/device/token") => WithDevice(auth, RotateToken),
            ("POST", "/api/v1/device/leave") => WithDevice(auth, Leave),
            _ => Error(404, "not_found"),
        };
    }

    public bool AllowLeave { get; set; } = true;

    private HttpResponseMessage Enroll(JsonObject body)
    {
        if (body.Any(p => p.Key is not ("code" or "deviceName" or "platform" or "appVersion")) || body["platform"] is null)
        {
            return Error(400, "invalid_request");
        }
        if ((string?)body["code"] != Code)
        {
            return Error(404, "code_invalid");
        }
        if (TenantBlocked)
        {
            return Error(403, "tenant_blocked");
        }
        var device = new Device { Id = $"d_{Devices.Count:D12}", Token = NewToken(), State = AutoApprove ? "approved" : "pending", Name = (string?)body["deviceName"] ?? "" };
        Devices.Add(device);
        return Json(201, new JsonObject
        {
            ["deviceId"] = device.Id, ["deviceToken"] = device.Token, ["state"] = device.State, ["tenantName"] = TenantName, ["pollAfterSec"] = 10,
        });
    }

    private HttpResponseMessage WithDevice(string? token, Func<Device, HttpResponseMessage> handle)
    {
        var device = Devices.FirstOrDefault(d => d.Token == token);
        if (device is null && Devices.FirstOrDefault(d => d.OldToken is not null && d.OldToken == token) is { } old)
        {
            device = old; // altes Token gilt, bis das neue benutzt wurde
        }
        else if (device is not null)
        {
            device.OldToken = null;
        }
        return device is null ? Error(401, "device_unknown") : handle(device);
    }

    private static JsonObject DeviceJson(Device d) => new()
    {
        ["deviceId"] = d.Id, ["deviceName"] = d.Name, ["state"] = d.State, ["tenantName"] = "Muster GmbH", ["configVersion"] = "c_1",
        ["rotateToken"] = d.Rotate, ["pollAfterSec"] = 10,
    };

    private HttpResponseMessage Config(Device d, string? etag)
    {
        if (TenantBlocked)
        {
            return Error(403, "tenant_blocked");
        }
        if (d.State != "approved")
        {
            return Error(403, d.State == "blocked" ? "device_blocked" : "device_pending");
        }
        var config = BuildConfig();
        var version = "\"c_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(config.ToJsonString())))[..12] + "\"";
        if (etag == version)
        {
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        }
        config["configVersion"] = version.Trim('"');
        var response = Json(200, config);
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(version);
        return response;
    }

    public JsonObject BuildConfig()
    {
        var settings = (JsonObject)Settings.DeepClone();
        var levels = (JsonObject)Levels.DeepClone();
        var config = new JsonObject { ["tenantName"] = TenantName, ["keyMode"] = KeyMode, ["syncIntervalSec"] = 900, ["rotateToken"] = false };
        if (KeyMode == "proxy")
        {
            config["proxy"] = new JsonObject { ["baseUrl"] = ProxyOverride ?? ProxyUrl };
            Fix(settings, levels, "stt", "providerId", "custom");
            Fix(settings, levels, "stt", "baseUrl", ProxyOverride ?? ProxyUrl);
            Fix(settings, levels, "llm", "providerId", "same");
            Fix(settings, levels, "sttFallback", "enabled", false);
            Fix(settings, levels, "llmFallback", "enabled", false);
        }
        else
        {
            config["secrets"] = Secrets.DeepClone();
        }
        settings["management"] = new JsonObject { ["allowLeave"] = AllowLeave };
        if (!AllowLeave)
        {
            levels["management.allowLeave"] = "fixed";
        }
        var policy = (JsonObject)Policy.DeepClone();
        policy["levels"] = levels;
        config["settings"] = settings;
        config["policy"] = policy;
        return config;
    }

    private static void Fix(JsonObject settings, JsonObject levels, string section, string field, JsonNode? value)
    {
        if (settings[section] is not JsonObject o)
        {
            o = [];
            settings[section] = o;
        }
        o[field] = value;
        levels[$"{section}.{field}"] = "fixed";
    }

    private HttpResponseMessage RotateToken(Device d)
    {
        if (d.State == "blocked")
        {
            return Error(403, "device_blocked");
        }
        d.OldToken = d.Token;
        d.Token = NewToken();
        d.Rotate = false;
        return Json(200, new JsonObject { ["deviceToken"] = d.Token });
    }

    private HttpResponseMessage Leave(Device d)
    {
        if (!AllowLeave)
        {
            return Error(403, "leave_not_allowed");
        }
        Devices.Remove(d);
        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private string NewToken() => "wld_test-token-not-real-" + (++_tokens).ToString("D20");

    private static HttpResponseMessage Json(int status, JsonObject body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Error(int status, string code) =>
        Json(status, new JsonObject { ["error"] = code, ["message"] = "fake " + code });
}

/// <summary>Firmen-Anbindung gegen den Fake-Server: Controller, Speicher, Service.</summary>
internal sealed class ManagementRig : IDisposable
{
    /// <param name="uiThread">Headless-UI-Tests: Änderungen auf dem UI-Thread wie in der App.</param>
    public ManagementRig(WhisperLoomSettings? settings = null, bool uiThread = false)
    {
        F = new ControllerFixture(settings);
        Policies = new PolicyStore(F.DataDir);
        Cache = new ManagementCache(F.DataDir).Load();
        Target = new HostManagedTarget(F.Controller, Policies,
            uiThread ? f => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(f) : f => f());
        Service = new ManagementService(Target, User, Company, Server.Api(), Cache, F.Log, () => F.Now);
        Secrets = new CompanySecretStore(User, Company, () => F.Controller.Settings, () => F.Controller.Policy,
            () => Service.KeyTargets, new OwnKeyTargets(F.DataDir));
    }

    public FakeManagementServer Server { get; } = new();

    public ControllerFixture F { get; }

    public MemorySecretStore User { get; } = new();

    public MemorySecretStore Company { get; } = new();

    public PolicyStore Policies { get; }

    public ManagementCache Cache { get; }

    public HostManagedTarget Target { get; }

    public ManagementService Service { get; }

    public CompanySecretStore Secrets { get; }

    public WhisperLoomSettings Settings => F.Controller.Settings;

    public PolicySettings Policy => F.Controller.Policy;

    public async Task<SyncOutcome> JoinAsync() => await Service.JoinAsync(FakeManagementServer.Url, FakeManagementServer.Code, "PC-Test", CancellationToken.None);

    public void Dispose() => F.Dispose();
}
