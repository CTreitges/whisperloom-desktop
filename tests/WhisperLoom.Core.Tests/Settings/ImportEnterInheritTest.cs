using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.Core.Tests.Settings;

/// <summary>
/// Review W2-C0 R1: Ein Import darf „Enter danach“ eines bestehenden Profils nur sicherer machen. „Aus“ (false) wird nicht
/// zu „Erben“ (null bzw. fehlend) — sonst sendete das Profil Enter, sobald <c>output.pressEnter</c> an ist.
/// </summary>
public class ImportEnterInheritTest
{
    private static WhisperLoomSettings Current(string profile) =>
        SyncFixtures.Import(Profiles(profile)).Settings!;

    private static string Profiles(string profile) => """{"desktop":{"profiles":[""" + profile + "]}}";

    private static JsonNode? EnterOf(ImportPreview preview) =>
        ((JsonObject)SyncFixtures.Tree(SyncExport.ToJson(preview.Settings!))["desktop"]!["profiles"]![0]!)
            .LastOrDefault(p => p.Key.Equals("pressEnter", StringComparison.OrdinalIgnoreCase)).Value;

    [Theory]
    [InlineData("""{"process":"cmd.exe"}""")]
    [InlineData("""{"process":"CMD","pressEnter":null}""")]
    [InlineData("""{"Process":"cmd.exe","PressEnter":null}""")]
    public void Off_stays_off_when_the_import_says_inherit(string incoming)
    {
        var current = Current("""{"process":"cmd.exe","pressEnter":false}""");

        var preview = SyncFixtures.Import(Profiles(incoming), current);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.False(EnterOf(preview)!.GetValue<bool>());
    }

    [Fact]
    public void Inherit_may_become_off_and_new_profiles_may_inherit()
    {
        var current = Current("""{"process":"cmd.exe"}""");

        var off = SyncFixtures.Import("""{"desktop":{"profiles":[{"process":"cmd.exe","pressEnter":false}]}}""", current);
        var fresh = SyncFixtures.Import("""{"desktop":{"profiles":[{"process":"notepad.exe"}]}}""", current);

        Assert.False(EnterOf(off)!.GetValue<bool>());
        Assert.Null(EnterOf(fresh));
    }
}
