using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Management;

/// <summary>Ergebnis eines Abgleichs (für Seite „Firma“, Assistent, Log).</summary>
public enum SyncOutcome
{
    NotManaged,
    Pending,
    Synced,
    NotModified,
    Blocked,
    Revoked,
    Offline,
    Failed,
}

/// <summary>Ergebnis von „Firma verlassen“.</summary>
public enum LeaveOutcome
{
    Left,
    NotAllowed,
    Offline,
    Failed,
}

/// <summary>Wohin die Firmen-Anbindung Werte und Richtlinie schreibt: laufende App oder Dateien (CLI).</summary>
public interface IManagedTarget
{
    WhisperLoomSettings Settings { get; }

    PolicySettings Policy { get; }

    /// <summary>Speichert Einstellungen und Richtlinie (samt Werten der festen Pfade) und wendet sie an.</summary>
    Task SaveAsync(WhisperLoomSettings settings, PolicySettings policy);

    /// <summary>
    /// Speichert nur den Zustand der Anbindung (Einstellungen); Richtlinie und festgehaltene Werte bleiben, wie sie sind
    /// (W2-FIX N-1: sonst würden lokal geänderte Werte bei 304 zu neuen Fest-Werten).
    /// </summary>
    Task SaveStateAsync(WhisperLoomSettings settings);
}
