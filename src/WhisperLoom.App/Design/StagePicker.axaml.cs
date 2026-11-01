using Avalonia.Controls;
using Avalonia.Input;

namespace WhisperLoom.App.Design;

/// <summary>
/// Ansicht zu <see cref="StagePickerViewModel"/>. Hält selbst den Tastaturfokus und übersetzt Tasten: ↑/↓ und Tab/Umschalt+Tab
/// bewegen die Markierung (Tab verlässt den Wähler nicht – HIG: Listen mit eigener Tastenbedienung), 1–9 wählt sofort, Enter
/// wählt die markierte Stufe, Esc bricht ab.
/// </summary>
// Doku: docs.avaloniaui.net/docs/concepts/input/keyboard (KeyDown, Handled; abgerufen 2026-11-01).
public partial class StagePicker : UserControl
{
    public StagePicker() => InitializeComponent();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is StagePickerViewModel vm && Handle(vm, e.Key, e.KeyModifiers))
        {
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    /// <summary>Taste auf den Wähler anwenden; false = nicht seine Taste.</summary>
    public static bool Handle(StagePickerViewModel vm, Key key, KeyModifiers modifiers)
    {
        switch (key)
        {
            case Key.Down:
                vm.Move(1);
                return true;
            case Key.Up:
                vm.Move(-1);
                return true;
            case Key.Tab:
                vm.Move(modifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                return true;
            case Key.Enter:
                vm.Confirm();
                return true;
            case Key.Escape:
                vm.Cancel();
                return true;
            case >= Key.D1 and <= Key.D9 when modifiers == KeyModifiers.None:
                return vm.ChooseNumber(key - Key.D0);
            case >= Key.NumPad1 and <= Key.NumPad9 when modifiers == KeyModifiers.None:
                return vm.ChooseNumber(key - Key.NumPad0);
            default:
                return false;
        }
    }
}
