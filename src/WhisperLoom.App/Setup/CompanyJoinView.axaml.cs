using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace WhisperLoom.App.Setup;

/// <summary>Firmen-Beitritt (Assistent und Seite „Firma“); kopiert die Geräte-ID für die IT.</summary>
public partial class CompanyJoinView : UserControl
{
    private CompanyJoinViewModel? _vm;

    public CompanyJoinView() => InitializeComponent();

    /// <summary>Feldfehler: Fokus auf das erste fehlerhafte Feld (UX-6 N11).</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.FocusField -= OnFocusField;
        }
        _vm = DataContext as CompanyJoinViewModel;
        if (_vm is not null)
        {
            _vm.FocusField += OnFocusField;
        }
    }

    private void OnFocusField(string name) => this.FindControl<TextBox>(name)?.Focus();

    private async void OnCopyDevice(object? sender, RoutedEventArgs e)
    {
        // Doku: Avalonia 12 Avalonia.Base.xml, ClipboardExtensions.SetTextAsync (abgerufen 2026-10-30).
        if (DataContext is CompanyJoinViewModel { DeviceId: { Length: > 0 } id } && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(id);
            Design.CopyFeedback.Show(CopyDeviceText, CopyDeviceDone);
        }
    }
}
