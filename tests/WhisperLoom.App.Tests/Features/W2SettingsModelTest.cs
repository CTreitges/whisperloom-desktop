using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Features;

/// <summary>W2-Vertrag in der App: local.* mit Standardwerten, Profil-Enter Erben/An/Aus, Secret-Name „device“.</summary>
public sealed class W2SettingsModelTest
{
    [Fact]
    public void Local_new_fields_have_defaults_and_roundtrip()
    {
        var empty = LocalSettings.Of(new WhisperLoomSettings());
        Assert.Equal(("ollama", ""), (empty.TextEngine, empty.BundledModel));
        Assert.Empty(empty.Servers);

        var settings = new WhisperLoomSettings();
        var local = new LocalSettings { TextEngine = "server", BundledModel = "qwen3-1.7b" };
        local.Servers.Add(new LocalServer { Name = "LM Studio", Kind = "text", BaseUrl = "http://127.0.0.1:1234/v1", Model = "m" });
        LocalSettings.Set(settings, local);

        var json = SettingsJson.Serialize(settings);
        var back = LocalSettings.Of(SettingsJson.Deserialize(json));

        Assert.Contains("\"textEngine\": \"server\"", json, StringComparison.Ordinal);
        Assert.Equal(("server", "qwen3-1.7b"), (back.TextEngine, back.BundledModel));
        Assert.Equal(("LM Studio", "text", "http://127.0.0.1:1234/v1"), (back.Servers[0].Name, back.Servers[0].Kind, back.Servers[0].BaseUrl));
    }

    [Theory]
    [InlineData("""{"desktop":{"profiles":[{"process":"a.exe"}]}}""", null)]
    [InlineData("""{"desktop":{"profiles":[{"process":"a.exe","pressEnter":null}]}}""", null)]
    [InlineData("""{"desktop":{"profiles":[{"process":"a.exe","pressEnter":true}]}}""", true)]
    [InlineData("""{"desktop":{"profiles":[{"process":"a.exe","pressEnter":false}]}}""", false)]
    public void Profile_enter_is_inherit_on_or_off(string json, bool? expected)
    {
        var profile = SettingsStore.Desktop(SettingsJson.Deserialize(json)).Profiles.Single();

        Assert.Equal(expected, profile.PressEnter);
    }

    [Fact]
    public void New_profile_inherits_enter()
    {
        Assert.Null(new AppProfile().PressEnter);
    }

    [Fact]
    public async Task File_store_accepts_the_device_token_name()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wld-w2c0-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WhisperLoom.App.Secrets.FileSecretStore(Path.Combine(dir, "secrets.json"), "WhisperLoom-Test-W2-C0:");
            await store.SetAsync(SecretNames.Device, "wld_test-token-not-real-1", CancellationToken.None);

            Assert.Equal("wld_test-token-not-real-1", await store.GetAsync(SecretNames.Device, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync("nope", "x", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
