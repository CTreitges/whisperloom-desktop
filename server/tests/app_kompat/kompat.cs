#:project ../../../src/WhisperLoom.Core/WhisperLoom.Core.csproj
// App-Kompatibilität des Proxys (W2-SRV2): die unveränderten App-Clients aus WhisperLoom.Core (ApiTranscriber,
// TextRefiner) mit Anbieter "custom", Base-URL = Proxy und Key = Geräte-Token. Keine Sonderlogik für den Proxy.
// Aufruf (aus server/tests/test_app_kompat.py): dotnet run kompat.cs -- <proxyBaseUrl> <geräteToken>
// Doku: https://learn.microsoft.com/dotnet/core/sdk/file-based-apps (#:project, abgerufen 2026-11-01)
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Prompts;

if (args.Length != 2)
{
    Console.Error.WriteLine("Aufruf: kompat.cs <proxyBaseUrl> <geräteToken>");
    return 2;
}

using var http = Http.CreateClient();
var custom = ProviderCatalog.Custom;
var stt = new ApiAccess(args[0], args[1], "whisper-x", 600_000, custom, null);
var text = await new ApiTranscriber(http, stt, "WhisperLoom").TranscribeAsync(
    new WavUpload(new short[16_000]), "de", CancellationToken.None);
Console.WriteLine($"STT {text.Text}");

var llm = new ApiAccess(args[0], args[1], "text-x", 600_000, custom, null);
var refined = await new TextRefiner(llm, http, new RefinePrompt(), new TextCleanup([])).RefineAsync(
    "hallo von der kompatibilitätsprüfung", "de", new Refinement(RefineMode.Polish), CancellationToken.None);
Console.WriteLine($"LLM {refined}");
return 0;
