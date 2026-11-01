using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Management;

/// <summary>Texte der Firmen-Anbindung nach Fehlercode bzw. Zustand (<c>docs/server-api.md</c> §1.2, §1.3), DE/EN.</summary>
public static class ManagementTexts
{
    /// <summary>Fehlercodes mit eigenem Text; unbekannte Codes bekommen den allgemeinen Text (Vertrag: nach HTTP-Status).</summary>
    public static readonly IReadOnlyList<string> KnownErrors =
    [
        "invalid_request", "device_unknown", "code_invalid", "not_found", "device_pending", "device_blocked", "tenant_blocked",
        "leave_not_allowed", "too_many_pending", "client_outdated", "rate_limited", "internal_error", "service_unavailable",
        "offline", "config_invalid",
    ];

    public static string Error(string? code) =>
        Loc.Instance["Company_Err_" + (code is not null && KnownErrors.Contains(code) ? code : "other")];

    /// <summary>Hinweis bei Sperre bzw. Widerruf: Firmen-Zugänge aus, eigene Einstellungen bleiben.</summary>
    public static string Notice(Core.Contracts.Settings.ManagementSettings m) =>
        Loc.Instance.Format(m.State == "blocked" ? "Company_Notice_blocked" : "Company_Notice_revoked", Tenant(m));

    /// <summary>Firmenname oder „deine Firma“.</summary>
    public static string Tenant(Core.Contracts.Settings.ManagementSettings m) =>
        m.TenantName.Length > 0 ? m.TenantName : Loc.Instance["Company_Unnamed"];

    /// <summary>Anzeige des Zustands (<c>management.state</c>).</summary>
    public static string State(string state) =>
        Loc.Instance["Company_State_" + (state is "pending" or "approved" or "blocked" or "revoked" ? state : "none")];
}
