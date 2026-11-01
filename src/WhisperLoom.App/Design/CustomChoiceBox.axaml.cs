using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;

namespace WhisperLoom.App.Design;

/// <summary>Ansicht zu <see cref="CustomChoiceViewModel"/>. Gibt den Accessible Name der Zeile an Liste und Textfeld weiter.</summary>
public partial class CustomChoiceBox : UserControl
{
    public CustomChoiceBox()
    {
        InitializeComponent();
        // Enter übernimmt wie das Verlassen des Feldes (HIG: Textfelder bestätigen mit Return).
        CustomBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Avalonia.Data.BindingOperations.GetBindingExpressionBase(CustomBox, TextBox.TextProperty)?.UpdateSource();
            }
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AutomationProperties.NameProperty || change.Property == AutomationProperties.HelpTextProperty)
        {
            var name = AutomationProperties.GetName(this);
            AutomationProperties.SetName(Box, name);
            AutomationProperties.SetName(CustomBox, name);
            AutomationProperties.SetHelpText(Box, AutomationProperties.GetHelpText(this));
        }
    }
}
