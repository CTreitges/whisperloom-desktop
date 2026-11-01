namespace WhisperLoom.Core.Local;

/// <summary>Ein herunterladbares Textmodell für das mitgelieferte <c>llama-server</c> (GGUF) mit Lizenz und Quelle.</summary>
/// <param name="File">Datei mit fester Größe und SHA-256 (<see cref="ModelDownloader"/>), RAM-Werten und Empfehlung.</param>
/// <param name="License">SPDX-Kennung der Modell-Lizenz.</param>
/// <param name="Source">Hugging-Face-Repository der Datei.</param>
public sealed record TextModel(OfflineModel File, string License, string Source)
{
    public string Id => File.Id;

    public string Label => File.Label;
}

/// <summary>
/// Textmodelle für <c>local.textEngine = "bundled"</c> (Rolle von <c>whisper/TextModelCatalog.kt</c> auf Android). Nur
/// Modelle mit freier Lizenz (Apache-2.0/MIT), Download durch den User von Hugging Face, Datei an einem festen Commit,
/// geprüft per SHA-256. Kein Modell liegt im Paket. Begründung der Auswahl: <c>docs/local-models.md</c>.
/// </summary>
// Doku: https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF, https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF
// (Größe und SHA-256 aus der Hub-API /api/models/<repo>/tree/main, abgerufen 2026-11-01)
public static class TextModelCatalog
{
    private const long Mb = 1_000_000L;
    private const long Gib = 1024L * 1024L * 1024L;

    public static readonly TextModel Qwen25_1_5B = new(
        new OfflineModel(
            "qwen2.5-1.5b", "Qwen2.5 1.5B Instruct", "qwen2.5-1.5b-instruct-q4_k_m.gguf",
            "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/91cad51170dc346986eccefdc2dd33a9da36ead9/qwen2.5-1.5b-instruct-q4_k_m.gguf",
            1_117_320_736L, "6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e",
            ApproxRamBytes: 2_000 * Mb, MinDeviceRamBytes: 8 * Gib, Recommended: true),
        "Apache-2.0", "Qwen/Qwen2.5-1.5B-Instruct-GGUF");

    public static readonly TextModel Qwen3_4B = new(
        new OfflineModel(
            "qwen3-4b-2507", "Qwen3 4B Instruct 2507", "Qwen3-4B-Instruct-2507-Q4_K_M.gguf",
            "https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF/resolve/a06e946bb6b655725eafa393f4a9745d460374c9/Qwen3-4B-Instruct-2507-Q4_K_M.gguf",
            2_497_281_120L, "3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597",
            ApproxRamBytes: 4_000 * Mb, MinDeviceRamBytes: 16 * Gib),
        "Apache-2.0", "unsloth/Qwen3-4B-Instruct-2507-GGUF");

    public static IReadOnlyList<TextModel> Models { get; } = [Qwen25_1_5B, Qwen3_4B];

    /// <summary>Empfehlung und Rückfall für <c>local.bundledModel = ""</c> bzw. Unbekanntes.</summary>
    public static TextModel Default => Qwen25_1_5B;

    public static TextModel? Find(string? id) => Models.FirstOrDefault(m => m.Id == id);

    public static TextModel ById(string? id) => Find(id) ?? Default;

    /// <summary>Passt das Modell in den Speicher dieses Geräts? <paramref name="deviceRamBytes"/> = 0 heißt unbekannt (dann ja).</summary>
    public static bool Fits(TextModel model, long deviceRamBytes) =>
        deviceRamBytes <= 0 || deviceRamBytes >= model.File.MinDeviceRamBytes;

    public static string MsgTooLittleRam(TextModel model) =>
        $"{model.Label} braucht mindestens {model.File.MinDeviceRamBytes / Gib} GB Arbeitsspeicher";

    /// <summary>Gesamter Arbeitsspeicher des Geräts laut .NET (physisch bzw. Container-Grenze).</summary>
    public static long DeviceRamBytes() => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
}
