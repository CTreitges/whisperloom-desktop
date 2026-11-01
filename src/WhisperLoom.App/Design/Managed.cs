using Avalonia;
using Avalonia.Controls;
using WhisperLoom.App.Localization;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Design;

/// <summary>
/// Woher die Stufen (<see cref="ManagedState"/>, W2-C0) kommen. Ein Mechanismus für alle Seiten: <see cref="SettingRow"/>,
/// <see cref="SettingsGroup"/> und <see cref="SettingsPage"/> fragen nur diese Schnittstelle.
/// </summary>
public interface IManagedSource
{
    /// <summary>Wer verwaltet (Firmenname oder „deine IT“); null = nichts verwaltet.</summary>
    string? ManagerName { get; }

    ManagedState StateOf(string path);

    /// <summary>Hat der Nutzer eine Vorgabe geändert (User-Override)? Dann bietet die Zeile „Zurücksetzen“ an.</summary>
    bool IsOverridden(string path);

    /// <summary>„Auf Firmenvorgabe zurücksetzen“: neue Einstellungen mit dem Firmenwert unter <paramref name="path"/>.</summary>
    WhisperLoomSettings Reset(WhisperLoomSettings current, string path);
}

/// <summary>
/// Stufen aus der Richtlinie (<see cref="PolicySettings.StateOf"/>: Provisioning-Datei oder Firmen-Server). Tastenkürzel
/// sind dort nie Fest. Overrides und Zurücksetzen über <see cref="ManagedDefaults"/>.
/// </summary>
public sealed class PolicyManagedSource(PolicySettings policy, Func<WhisperLoomSettings> settings) : IManagedSource
{
    public string? ManagerName =>
        !policy.IsActive ? null
        : settings().Management.TenantName is { Length: > 0 } tenant ? tenant
        : Loc.Instance["Managed_It"];

    public ManagedState StateOf(string path) => policy.StateOf(path);

    public bool IsOverridden(string path) => ManagedDefaults.IsOverridden(settings(), path);

    public WhisperLoomSettings Reset(WhisperLoomSettings current, string path) => ManagedDefaults.Reset(current, policy, path);
}

/// <summary>Stufen-Quelle plus Aktion „Auf Firmenvorgabe zurücksetzen“; vererbt sich über <see cref="Managed.ContextProperty"/>.</summary>
public sealed class ManagedContext(IManagedSource source, Func<string, Task> resetToDefault)
{
    public static ManagedContext None { get; } = new(new PolicyManagedSource(PolicySettings.None, () => new WhisperLoomSettings()), _ => Task.CompletedTask);

    public IManagedSource Source { get; } = source;

    public Task ResetToDefaultAsync(string path) => resetToDefault(path);
}

/// <summary>Angehängte, vererbte Eigenschaft: das Fenster setzt sie einmal, alle Zeilen darunter lesen sie.</summary>
public sealed class Managed : AvaloniaObject
{
    public static readonly AttachedProperty<ManagedContext?> ContextProperty =
        AvaloniaProperty.RegisterAttached<Managed, Control, ManagedContext?>("Context", inherits: true);

    public static ManagedContext? GetContext(Control control) => control.GetValue(ContextProperty);

    public static void SetContext(Control control, ManagedContext? value) => control.SetValue(ContextProperty, value);
}
