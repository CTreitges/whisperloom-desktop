namespace WhisperLoom.Core.Local;

/// <summary>
/// Eine herunterladbare Datei mit fester Größe und Prüfsumme (Port von <c>OfflineModel</c>, <c>whisper/ModelCatalog.kt</c>):
/// whisper.cpp-Modelle und die Archive des Begleitprozesses. Ein Download gilt nur bei gleicher Größe und SHA256 als fertig.
/// </summary>
/// <param name="Id">Stabile ID ("small", …), steht in <c>offline.model</c>.</param>
/// <param name="FileName">Dateiname im Zielordner.</param>
/// <param name="Bytes">Exakte Dateigröße.</param>
/// <param name="Sha256">SHA256, hex, klein geschrieben.</param>
/// <param name="ApproxRamBytes">Grober RAM-Bedarf beim Rechnen.</param>
/// <param name="MinDeviceRamBytes">Mindest-RAM des Geräts (0 = keine Einschränkung).</param>
public sealed record OfflineModel(
    string Id,
    string Label,
    string FileName,
    string Url,
    long Bytes,
    string Sha256,
    long ApproxRamBytes = 0,
    long MinDeviceRamBytes = 0,
    bool Recommended = false,
    long ExtraDiskBytes = 0);

/// <summary>
/// Die vier whisper.cpp-Modelle (GGML, quantisiert) von huggingface.co/ggerganov/whisper.cpp in Anzeige-Reihenfolge
/// (Port von <c>ModelCatalog.kt</c>, Werte unverändert). Kein Modell liegt im Paket.
/// </summary>
public static class ModelCatalog
{
    /// <summary>302 → CDN; der HttpClient folgt. Range-Anfragen werden unterstützt.</summary>
    public const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    private const long Mb = 1_000_000L;
    private const long Gib = 1024L * 1024L * 1024L;

    public static readonly OfflineModel Tiny = new(
        "tiny", "Tiny", "ggml-tiny-q5_1.bin", BaseUrl + "ggml-tiny-q5_1.bin", 32_152_673L,
        "818710568da3ca15689e31a743197b520007872ff9576237bda97bd1b469c3d7", 250 * Mb);

    public static readonly OfflineModel Base = new(
        "base", "Base", "ggml-base-q5_1.bin", BaseUrl + "ggml-base-q5_1.bin", 59_707_625L,
        "422f1ae452ade6f30a004d7e5c6a43195e4433bc370bf23fac9cc591f01a8898", 355 * Mb);

    public static readonly OfflineModel Small = new(
        "small", "Small", "ggml-small-q5_1.bin", BaseUrl + "ggml-small-q5_1.bin", 190_085_487L,
        "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb", 430 * Mb, Recommended: true);

    public static readonly OfflineModel LargeV3Turbo = new(
        "large-v3-turbo", "Large v3 Turbo", "ggml-large-v3-turbo-q5_0.bin", BaseUrl + "ggml-large-v3-turbo-q5_0.bin", 574_041_195L,
        "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2", 1_000 * Mb, 6 * Gib);

    public static IReadOnlyList<OfflineModel> Models { get; } = [Tiny, Base, Small, LargeV3Turbo];

    /// <summary>Standard-Download und Rückfall für unbekannte IDs (= <c>offline.model</c>-Standard).</summary>
    public static OfflineModel Default => Small;

    public static OfflineModel? Find(string? id) => Models.FirstOrDefault(m => m.Id == id);

    public static OfflineModel ById(string? id) => Find(id) ?? Default;
}

/// <summary>Modellordner (Port von <c>ModelStore.kt</c>): fertige Datei und Teildatei <c>&lt;datei&gt;.part</c>.</summary>
public sealed class ModelStore(string dir)
{
    public const string PartSuffix = ".part";

    public string Dir { get; } = dir;

    public string File(OfflineModel model) => Path.Combine(Dir, model.FileName);

    public string PartFile(OfflineModel model) => Path.Combine(Dir, model.FileName + PartSuffix);

    /// <summary>Fertig = Datei da und genau so groß wie im Katalog (die Prüfsumme prüft der Download).</summary>
    public bool IsInstalled(OfflineModel model)
    {
        var f = new FileInfo(File(model));
        return f.Exists && f.Length == model.Bytes;
    }

    public IReadOnlyList<OfflineModel> Installed(IEnumerable<OfflineModel>? models = null) =>
        [.. (models ?? ModelCatalog.Models).Where(IsInstalled)];

    /// <summary>Löscht Datei und Teildatei; true, wenn danach keine mehr da ist.</summary>
    public bool Delete(OfflineModel model)
    {
        System.IO.File.Delete(File(model));
        System.IO.File.Delete(PartFile(model));
        return !System.IO.File.Exists(File(model));
    }

    public bool EnsureDir()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Freier Platz auf dem Laufwerk des Ordners (bzw. des nächsten vorhandenen Elternordners).</summary>
    public long FreeBytes()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(Dir));
        return string.IsNullOrEmpty(root) ? 0 : new DriveInfo(root).AvailableFreeSpace;
    }
}
