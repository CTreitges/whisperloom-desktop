using System.Text.Json.Nodes;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.Core.Tests.Settings;

/// <summary>
/// Kompass-Todo aus Lauf 1: Die App liest <c>desktop</c> und <c>local</c> ohne Rücksicht auf Groß-/Kleinschreibung,
/// die Sperre muss deshalb ebenso vergleichen — sonst umgeht <c>desktop.ProxyMode</c> die Sperre <c>desktop.proxyMode</c>.
/// </summary>
public class PolicyCaseTest
{
    [Theory]
    [InlineData("desktop.ProxyMode")]
    [InlineData("DESKTOP.proxymode")]
    [InlineData("Desktop.ProxyMode.Extra")]
    [InlineData("History.Enabled")]
    public void Lock_ignores_case(string path)
    {
        var p = new PolicySettings { Locked = ["desktop.proxyMode"], HistoryEnabled = true };

        Assert.True(p.IsLocked(path));
    }

    [Fact]
    public void Lock_still_respects_path_boundaries_in_any_case()
    {
        var p = new PolicySettings { Locked = ["desktop.proxy"] };

        Assert.False(p.IsLocked("Desktop.ProxyMode"));
    }

    [Fact]
    public void Import_with_other_case_cannot_bypass_the_lock()
    {
        var current = SyncFixtures.Import("""{"desktop":{"proxyMode":"off"}}""").Settings!;
        var policy = new PolicySettings { Locked = ["desktop.proxyMode"] };

        var preview = SyncFixtures.Import("""{"desktop":{"ProxyMode":"manual","ProxyUrl":"http://10.0.0.9:3128"}}""", current, policy);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        var desktop = (JsonObject)SyncFixtures.Tree(SyncExport.ToJson(preview.Settings!))["desktop"]!;
        Assert.Null(desktop["ProxyMode"]);
        Assert.Equal("off", desktop["proxyMode"]!.GetValue<string>());
        Assert.Contains(preview.Changes, c => c is { Path: "desktop.ProxyMode", Locked: true });
    }
}
