using Avalonia.Headless.XUnit;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Management;
using WhisperLoom.App.ViewModels;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests;

/// <summary>
/// W3-REST Befund 3: Legt die Firma das Ziel des Zugangs fest (Anbieter bzw. Adresse Fest), nutzt die App den eigenen Key unter
/// Verwaltung nicht und verwirft ihn beim Verlassen. Das Key-Feld sagt das nach dem Speichern — wie die CLI-Warnung von
/// <c>key set</c> — statt nur „Key gespeichert.“. Ohne Fest-Ziel bleibt es bei „Key gespeichert.“. Nie mit dem Key-Wert.
/// </summary>
public sealed class KeyFieldCompanyTargetTest : IDisposable
{
    private const string Secret = "test-key-not-real-91";

    private readonly string _dir = Directory.CreateTempSubdirectory("wl-keytarget-").FullName;
    private readonly WhisperLoomSettings _settings = new();
    private readonly MemorySecretStore _user = new();

    public KeyFieldCompanyTargetTest()
    {
        _settings.Management.ServerUrl = "https://wl.example.com";
        _settings.Management.State = "approved";
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<string> SaveWith(PolicySettings policy, string language)
    {
        Loc.Instance.SetLanguage(language);
        var store = new CompanySecretStore(_user, new MemorySecretStore(), () => _settings, () => policy,
            () => new Dictionary<string, KeyTarget>(), new OwnKeyTargets(_dir));
        var key = new KeyFieldViewModel(SecretNames.Stt, store, new FakeAccessChecker(), () => ("openai", ""));
        await key.LoadAsync();
        key.ReplaceCommand.Execute(null);
        key.NewValue = Secret;
        await key.SaveKeyCommand.ExecuteAsync(null);
        Assert.Equal(Secret, _user.Values[SecretNames.Stt]); // gespeichert wird trotzdem (wie die CLI)
        Assert.DoesNotContain(Secret, key.Status, StringComparison.Ordinal);
        return key.Status;
    }

    [AvaloniaFact]
    public async Task Fest_Ziel_der_Firma_zeigt_den_Hinweis_statt_gespeichert()
    {
        var policy = new PolicySettings { Locked = ["stt.providerId"] };

        Assert.Equal("Key gespeichert, aber das Ziel legt die Firma fest: Unter Verwaltung wird dieser Key nicht genutzt und beim Verlassen gelöscht.",
            await SaveWith(policy, "de"));
        Assert.Equal("Key saved, but your company sets the target: while managed, this key is not used and is deleted when you leave.",
            await SaveWith(policy, "en"));
    }

    [AvaloniaFact]
    public async Task Ohne_Fest_Ziel_bleibt_es_bei_gespeichert()
    {
        Assert.Equal("Key gespeichert.", await SaveWith(PolicySettings.None, "de"));
        Assert.Equal("Key saved.", await SaveWith(PolicySettings.None, "en"));

        // Fest-Anbieter, aber eigene Adresse gegen die Vorgabe: dann wählt der Nutzer das Ziel (wie die CLI).
        _settings.Management.Overrides.Add("stt.baseUrl");
        Assert.Equal("Key gespeichert.", await SaveWith(new PolicySettings { Locked = ["stt.providerId"] }, "de"));
    }

    [AvaloniaFact]
    public async Task Ohne_Verwaltung_kein_Hinweis()
    {
        _settings.Management.ServerUrl = "";
        Assert.Equal("Key gespeichert.", await SaveWith(new PolicySettings { Locked = ["stt.providerId"] }, "de"));
    }
}
