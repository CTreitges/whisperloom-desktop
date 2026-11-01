using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace WhisperLoom.App.Views.Settings;

/// <summary>
/// Modellwahl mit „Vom Server laden“ (ViewModel: <see cref="ViewModels.Settings.ModelPickerViewModel"/>). Gibt den
/// Accessible Name der Zeile an die Auswahl weiter; der Knopf heißt „Modelle vom Server laden · &lt;Zeile&gt;“.
/// </summary>
public partial class ModelPickerView : UserControl
{
    public ModelPickerView() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AutomationProperties.NameProperty || change.Property == AutomationProperties.HelpTextProperty)
        {
            var name = AutomationProperties.GetName(this);
            AutomationProperties.SetName(ChoiceBox, name);
            AutomationProperties.SetHelpText(ChoiceBox, AutomationProperties.GetHelpText(this));
            AutomationProperties.SetName(LoadButton, $"{Localization.Loc.Instance["Models_Load"]} · {name}");
        }
    }
}
