using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Setup;

/// <summary>Firmen-Beitritt (Assistent und Seite „Firma“); kopiert die Geräte-ID für die IT.</summary>
public partial class CompanyJoinView : UserControl
{
    public CompanyJoinView() => InitializeComponent();

    private async void OnCopyDevice(object? sender, RoutedEventArgs e)
    {
        // Doku: Avalonia 12 Avalonia.Base.xml, ClipboardExtensions.SetTextAsync (abgerufen 2026-10-30).
        if (DataContext is CompanyJoinViewModel { DeviceId: { Length: > 0 } id } && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(id);
            CopyDeviceText.Text = Loc.Instance["Action_Copied"];
        }
    }
}
