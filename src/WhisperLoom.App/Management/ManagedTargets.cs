using WhisperLoom.App.Secrets;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Management;

/// <summary>Ziel in Dateien (CLI <c>join</c>): settings.json und policy.json im Datenordner; eine laufende Instanz lädt danach neu.</summary>
public sealed class FileManagedTarget(SettingsStore store, PolicyStore policies) : IManagedTarget
{
    public WhisperLoomSettings Settings { get; private set; } = store.Load();

    public PolicySettings Policy { get; private set; } = policies.Load();

    public Task SaveAsync(WhisperLoomSettings settings, PolicySettings policy)
    {
        policies.Save(policy, settings);
        store.Save(settings);
        Settings = settings;
        Policy = policy;
        return Task.CompletedTask;
    }

    public Task SaveStateAsync(WhisperLoomSettings settings)
    {
        store.Save(settings);
        Settings = settings;
        return Task.CompletedTask;
    }
}

/// <summary>Ziel in der laufenden App: Richtlinie speichern und setzen, Einstellungen über den Controller anwenden.</summary>
public sealed class HostManagedTarget(DictationController controller, PolicyStore policies, Func<Func<Task>, Task> onUiThread) : IManagedTarget
{
    public WhisperLoomSettings Settings => controller.Settings;

    public PolicySettings Policy => controller.Policy;

    public Task SaveAsync(WhisperLoomSettings settings, PolicySettings policy) => onUiThread(async () =>
    {
        policies.Save(policy, settings);
        policy.Enforce(settings);
        controller.Policy = policy;
        await controller.ApplySettingsAsync(settings);
    });

    public Task SaveStateAsync(WhisperLoomSettings settings) => onUiThread(async () =>
    {
        controller.Policy.Enforce(settings);
        await controller.ApplySettingsAsync(settings);
    });
}

/// <summary>Bausteine der Firmen-Anbindung für App und CLI.</summary>
public static class ManagementFactory
{
    /// <summary>Ablage der verteilten Firmen-Keys: gleiches System, Präfix <c>&lt;Präfix&gt;company:</c>.</summary>
    internal static Task<ISecretStore> CompanyStoreAsync(string dataDir, string prefix, CancellationToken ct) =>
        SecretStoreFactory.CreateAsync(dataDir, prefix + CompanySecretStore.PrefixSuffix, ct);

    public static ManagementApi Api(HttpClient http) =>
        new(http, EnrollInput.Platform(), Updates.AppVersion.Current);
}
