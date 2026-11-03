using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WhisperLoom.App.ViewModels;

namespace WhisperLoom.App.Design;

/// <summary>
/// Baustein „Optionsgruppe“ (zielstruktur.md §7, B20): 2–7 Werte als Optionsfelder untereinander, je mit Label und einer
/// Zeile Erklärung (<see cref="Choice.Description"/>). Tastatur nach APG „Radio Group“: Tab führt einmal in die Gruppe
/// (auf den gewählten Wert), Pfeiltasten wechseln und wählen. Den Namen der Gruppe setzt die <see cref="SettingRow"/>.
/// </summary>
// Doku: Context7 /avaloniaui/avalonia-docs, controls/input/buttons/radiobutton.md und docs/input-interaction/focus.md (abgerufen 2026-11-03).
public class OptionGroup : ItemsControl
{
    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<OptionGroup, object?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

    private readonly string _group = "options-" + Guid.NewGuid().ToString("N");

    static OptionGroup() => KeyboardNavigation.TabNavigationProperty.OverrideDefaultValue<OptionGroup>(KeyboardNavigationMode.Once);

    /// <summary>↑/← und ↓/→ wählen den vorigen bzw. nächsten Wert (am Ende wieder vorn) und fokussieren ihn.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.Key switch
        {
            Key.Down or Key.Right => 1,
            Key.Up or Key.Left => -1,
            _ => 0,
        };
        var buttons = Buttons.Where(b => b.IsEffectivelyEnabled).ToList();
        if (e.Handled || step == 0 || e.KeyModifiers != KeyModifiers.None || buttons.Count == 0)
        {
            return;
        }
        var current = buttons.FindIndex(b => b.IsFocused);
        var next = buttons[((current < 0 ? 0 : current + step) + buttons.Count) % buttons.Count];
        next.IsChecked = true;
        next.Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }

    /// <summary>Die Optionsfelder in Reihenfolge (Tests, Fokus).</summary>
    public IEnumerable<RadioButton> Buttons => GetRealizedContainers().OfType<RadioButton>();

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = null;
        return true;
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new RadioButton();

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is not RadioButton button)
        {
            return;
        }
        button.GroupName = _group;
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.VerticalContentAlignment = VerticalAlignment.Top;
        button.Content = Body(item);
        if (item is Choice choice)
        {
            button.SetValue(AutomationProperties.NameProperty, choice.Label);
            button.SetValue(AutomationProperties.HelpTextProperty, choice.Description ?? "");
        }
        button.IsChecked = Equals(item, SelectedItem);
        button.IsCheckedChanged -= OnChecked;
        button.IsCheckedChanged += OnChecked;
        if (button.IsChecked == true)
        {
            KeyboardNavigation.SetTabOnceActiveElement(this, button);
        }
    }

    protected override void ClearContainerForItemOverride(Control container)
    {
        if (container is RadioButton button)
        {
            button.IsCheckedChanged -= OnChecked;
        }
        base.ClearContainerForItemOverride(container);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedItemProperty)
        {
            foreach (var button in Buttons)
            {
                var on = Equals(ItemFromContainer(button), SelectedItem);
                button.IsChecked = on;
                if (on)
                {
                    KeyboardNavigation.SetTabOnceActiveElement(this, button);
                }
            }
        }
    }

    private void OnChecked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true } button && ItemFromContainer(button) is { } item && !Equals(item, SelectedItem))
        {
            SelectedItem = item;
            KeyboardNavigation.SetTabOnceActiveElement(this, button);
        }
    }

    private static Control Body(object? item)
    {
        if (item is not Choice choice)
        {
            return new TextBlock { Text = item?.ToString() ?? "", TextWrapping = TextWrapping.Wrap };
        }
        var label = new TextBlock { Text = choice.Label, TextWrapping = TextWrapping.Wrap };
        if (!choice.HasDescription)
        {
            return label;
        }
        var description = new TextBlock { Text = choice.Description, TextWrapping = TextWrapping.Wrap };
        description.Classes.Add("caption");
        return new StackPanel { Spacing = 2, Children = { label, description } };
    }
}
