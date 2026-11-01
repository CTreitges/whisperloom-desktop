using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;
using WhisperLoom.Core.Tests.Api;

namespace WhisperLoom.Core.Tests.Settings;

/// <summary>
/// W2-LOCAL Prüfrunde 2 R-K1-1: Auch das Provisioning (und die Server-Konfiguration über <see cref="Provisioning.Plan"/>)
/// lenkt keinen Key eines eigenen Anbieters an eine neue Adresse: Host-Wechsel oder entfernter Eintrag → Key löschen,
/// außer die Datei liefert <c>secrets."custom:&lt;id&gt;"</c> selbst. Dazu D-K1-3: doppelte Eigenschaft = Formatfehler.
/// </summary>
public class ProvisioningProviderKeyTest
{
    private static WhisperLoomSettings Current()
    {
        var s = ProviderDirectoryTest.WithEntries();
        s.Stt.ProviderId = "custom:lab";
        return s;
    }

    private const string ItHost = """{"providers":{"custom":[{"id":"lab","name":"Labor","baseUrl":"https://it.example/v1"}]},"stt":{"providerId":"custom:lab"}}""";

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // ohne Wissen über die Ablage (Server-Konfiguration): Einträge vorher/nachher gelten als möglich
    public void Host_change_without_own_secret_drops_the_entry_key(bool knownStore)
    {
        var plan = Provisioning.Plan(Current(), SyncDocument.Parse(ItHost), knownStore ? ["custom:lab"] : null);

        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        Assert.Contains(new KeyDrop("custom:lab", "https://it.example"), plan.DroppedKeys);
        Assert.Contains(plan.Warnings, w => w.StartsWith("secrets.custom:lab: Adresse geändert", StringComparison.Ordinal));
        Assert.Contains("key-drop custom:lab", plan.Summary());
    }

    [Fact]
    public void Removed_entry_drops_its_key()
    {
        var plan = Provisioning.Plan(Current(), SyncDocument.Parse("""{"providers":{"custom":[]},"stt":{"providerId":"openai"}}"""), ["custom:lab"]);

        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        var drop = Assert.Single(plan.DroppedKeys);
        Assert.True(drop.EntryRemoved);
    }

    [Fact]
    public void File_with_its_own_key_keeps_it()
    {
        var file = ItHost[..^1] + ""","secrets":{"custom:lab":"test-key-not-real-6"}}""";

        var plan = Provisioning.Plan(Current(), SyncDocument.Parse(file), ["custom:lab"]);

        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        Assert.Empty(plan.DroppedKeys);
        Assert.Equal("test-key-not-real-6", plan.Secrets["custom:lab"]);
    }

    [Fact]
    public void Same_host_keeps_the_key()
    {
        var plan = Provisioning.Plan(Current(), SyncDocument.Parse(
            """{"providers":{"custom":[{"id":"lab","name":"Neu","baseUrl":"https://lab.example/v1"}]}}"""), ["custom:lab"]);

        Assert.Empty(plan.DroppedKeys);
    }

    [Fact]
    public void Duplicate_property_is_a_format_error()
    {
        var e = Assert.Throws<SyncFormatException>(() => SyncDocument.Parse("""{"language":"de","language":"en"}"""));

        Assert.DoesNotContain("language", e.Message, StringComparison.Ordinal);
    }
}
