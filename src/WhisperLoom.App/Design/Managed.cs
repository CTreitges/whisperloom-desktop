using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using WhisperLoom.App.Localization;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Design;

/// <summary>Stufe einer Einstellung (gemeinsam.md): Fest = ausgeblendet, Vorgabe = sichtbar mit Kennzeichnung, Frei = normal.</summary>
public enum ManagedLevel
{
    Free,
    Default,
    Fixed,
}

/// <summary>
/// Woher die Stufen kommen. Heute aus <see cref="PolicySettings"/> (gesperrt = Fest); nach dem Merge von W2-C0 aus
/// dessen <c>ManagedState</c>. Ein Mechanismus für alle Seiten: <see cref="SettingRow"/>, <see cref="SettingsGroup"/>
/// und <see cref="SettingsPage"/> fragen nur diese Schnittstelle.
/// </summary>
public interface IManagedSource
{
    /// <summary>Wer verwaltet (Firmenname oder „deine IT“); null = nichts verwaltet.</summary>
    string? ManagerName { get; }

    ManagedLevel LevelOf(string path);

    /// <summary>Firmenwert einer Vorgabe (für „Auf Firmenvorgabe zurücksetzen“); null = keiner.</summary>
    JsonNode? DefaultOf(string path);
}

/// <summary>Stufen aus der heutigen Richtlinie: gesperrt = Fest, sonst Frei. Tastenkürzel sind nie Fest.</summary>
public sealed class PolicyManagedSource(PolicySettings policy) : IManagedSource
{
    public string? ManagerName => policy.IsActive ? Loc.Instance["Managed_It"] : null;

    public ManagedLevel LevelOf(string path) =>
        IsHotkey(path) || !policy.IsLocked(path) ? ManagedLevel.Free : ManagedLevel.Fixed;

    public JsonNode? DefaultOf(string path) => null;

    /// <summary>Tastenkürzel bleiben immer frei (Entscheidung Christof, gemeinsam.md).</summary>
    public static bool IsHotkey(string path) =>
        path.StartsWith("desktop.hotkey", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Stufen-Quelle plus Aktion „Auf Firmenvorgabe zurücksetzen“; vererbt sich über <see cref="Managed.ContextProperty"/>.</summary>
public sealed class ManagedContext(IManagedSource source, Func<string, Task> resetToDefault)
{
    public static ManagedContext None { get; } = new(new PolicyManagedSource(PolicySettings.None), _ => Task.CompletedTask);

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
