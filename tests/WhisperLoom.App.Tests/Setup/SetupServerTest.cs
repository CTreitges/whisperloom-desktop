using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.Setup;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Tests.Support;
using static WhisperLoom.App.Tests.Setup.SetupUiTest;

namespace WhisperLoom.App.Tests.Setup;

/// <summary>
/// TP-WSETUP (P2): Der Weg „Eigener Server“ im Assistenten fragte keine Adresse ab – Einrichtung unmöglich. Jetzt: Adresse
/// (Pflicht) und Modell im Schritt Erkennung, ungültig → Meldung ohne Speichern, gültig → gespeichert und nutzbar.
/// Ende zu Ende gegen den lokalen <see cref="FakeServer"/> (nie gegen echte Anbieter, kein Key).
/// </summary>
public sealed class SetupServerTest
{
    private static void ToRecognition(SetupViewModel vm)
    {
        while (vm.Current != SetupStep.Recognition)
        {
            vm.Next();
        }
    }

    private static void ChooseCustom(SetupFixture s) =>
        s.Settings.SttProvider = s.Settings.SttProviders.Single(p => p.Value == ProviderCatalog.CustomId);

    [AvaloniaFact]
    public void Eigener_Server_zeigt_Adresse_und_Modell_nur_bei_diesem_Anbieter()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = Show(s.Vm);
        ToRecognition(s.Vm);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Shown(Named<TextBox>(window, "ServerUrlBox"))); // Standard OpenAI: feste Adresse

        ChooseCustom(s);
        Dispatcher.UIThread.RunJobs();

        Assert.True(s.Vm.ShowServer);
        Assert.True(Shown(Named<TextBox>(window, "ServerUrlBox")));
        Assert.True(Shown(Named<TextBox>(window, "ServerModelBox")));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Ohne_Adresse_bleibt_der_Assistent_stehen_und_speichert_nichts()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        Show(s.Vm);
        ToRecognition(s.Vm);
        ChooseCustom(s);
        Assert.Equal("", s.Vm.ServerError); // noch kein Versuch: keine Meldung

        s.Vm.Next();

        Assert.Equal(SetupStep.Recognition, s.Vm.Current);
        Assert.Equal(Loc.Instance["Setup_ServerUrlMissing"], s.Vm.ServerError);
        await s.Vm.FinishCommand.ExecuteAsync(null);
        Assert.Equal(Loc.Instance["Setup_ServerUrlMissing"], s.Vm.Status);
        Assert.Equal(0, s.Closed);
        Assert.False(s.State.Done);
        Assert.NotEqual(ProviderCatalog.CustomId, s.F.Store.Load().Stt.ProviderId);
    }

    [AvaloniaFact]
    public async Task Ungueltige_Adresse_meldet_einen_Fehler_ohne_Speichern()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = Show(s.Vm);
        ToRecognition(s.Vm);
        ChooseCustom(s);

        s.Vm.ServerUrl = "mein server:8000";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Loc.Instance["Validation_Url"], s.Vm.ServerError);
        Assert.Contains(Loc.Instance["Validation_Url"], UiTest.TextsOf(window));
        Assert.Equal("", s.Settings.SttBaseUrl); // nicht in die Arbeitskopie übernommen
        s.Vm.Next();
        Assert.Equal(SetupStep.Recognition, s.Vm.Current);
        await s.Vm.FinishCommand.ExecuteAsync(null);
        Assert.Equal(0, s.Closed);
        Assert.Equal("", s.F.Store.Load().Stt.BaseUrl);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Gueltige_Adresse_wird_geprueft_gespeichert_und_erkennt_Ende_zu_Ende()
    {
        Loc.Instance.SetLanguage("de");
        await using var server = new FakeServer();
        var transcriptions = 0;
        server.Map("/v1/models", _ => new FakeResponse(200, """{"data":[{"id":"Systran/faster-whisper-small"}]}"""));
        server.Map("/v1/audio/transcriptions", r =>
        {
            transcriptions++;
            Assert.Contains("Systran/faster-whisper-small", r.Latin1, StringComparison.Ordinal);
            return new FakeResponse(200, """{"text":"hallo vom eigenen server"}""");
        });
        using var http = new HttpClient();
        using var s = new SetupFixture(checker: new HttpAccessChecker(() => http));
        Show(s.Vm);
        ToRecognition(s.Vm);
        ChooseCustom(s);

        s.Vm.ServerUrl = server.Url("/v1");
        s.Vm.ServerModel = "Systran/faster-whisper-small";
        await s.Settings.SttKey.CheckCommand.ExecuteAsync(null); // „Zugang prüfen“ gegen den Server, ohne Key

        Assert.Equal("", s.Vm.ServerError);
        Assert.Equal(Loc.Instance["Access_Ok"] + " (HTTP 200)", s.Settings.SttKey.Status);
        s.Vm.Next();
        Assert.NotEqual(SetupStep.Recognition, s.Vm.Current);
        while (s.Vm.Current != SetupStep.Done)
        {
            s.Vm.Next();
        }
        await s.Vm.FinishCommand.ExecuteAsync(null);

        Assert.Equal("", s.Vm.Status);
        Assert.Equal(1, s.Closed);
        var saved = s.F.Store.Load();
        Assert.Equal(ProviderCatalog.CustomId, saved.Stt.ProviderId);
        Assert.Equal(server.Url("/v1"), saved.Stt.BaseUrl);
        Assert.Equal("Systran/faster-whisper-small", saved.Stt.Model);

        // Ende zu Ende: die gespeicherten Einstellungen erkennen über die echte Pipeline am Fake-Server.
        using var pipeline = new AppPipeline(() => saved, new MemorySecretStore(), new ModelCache());
        var result = await pipeline.RunAsync(
            new DictationRequest(new AudioBuffer(Enumerable.Repeat((short)9830, AudioBuffer.DefaultSampleRate).ToArray()), "de",
                DictationController.RefinementOf(saved.Refine)),
            CancellationToken.None);
        Assert.Equal("hallo vom eigenen server", result.Raw);
        Assert.Equal(1, transcriptions);
    }

    [AvaloniaFact]
    public void Lokaler_Weg_braucht_keine_Server_Adresse()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        Show(s.Vm);
        ToRecognition(s.Vm);
        ChooseCustom(s);
        s.Settings.Local.Engine = s.Settings.Local.Engines.Single(e => e.Value == "offline");

        Assert.False(s.Vm.ShowServer);
        s.Vm.Next();
        Assert.NotEqual(SetupStep.Recognition, s.Vm.Current);
    }
}
