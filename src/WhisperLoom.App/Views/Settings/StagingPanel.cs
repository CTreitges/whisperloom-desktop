using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace WhisperLoom.App.Views.Settings;

/// <summary>
/// Ablage für den Vorbau der Einstellungsseiten (PERF-2), unsichtbar für Screenreader (W3-PERF N7). Nur
/// <c>AccessibilityView="Raw"</c> reicht nicht: Das blendet nur das Panel aus, seine Kinder bleiben im Automation-Baum
/// (Headless-Test <c>SettingsPagesFollowUpTest</c>). Deshalb meldet der Peer keine Kinder.
/// </summary>
// Doku: Context7 /avaloniaui/avalonia-docs, docs/concepts/accessibility (abgerufen 2026-11-02).
public sealed class StagingPanel : Panel
{
    public StagingPanel()
    {
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new StagingPeer(this);

    private sealed class StagingPeer(StagingPanel owner) : ControlAutomationPeer(owner)
    {
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => [];
    }
}
