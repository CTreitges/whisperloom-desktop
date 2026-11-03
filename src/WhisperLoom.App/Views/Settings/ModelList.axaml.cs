using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace WhisperLoom.App.Views.Settings;

/// <summary>
/// Modell-Liste der Einstellungen (Whisper bei „Erkennung“, mitgelieferte Textmodelle bei „Text“): Zeilen aus
/// <see cref="ViewModels.IModelRow"/>, optional eigene Dateien (<see cref="ViewModels.CustomWhisperRow"/>) in derselben
/// Optionsgruppe. Der Name der Liste kommt von der <see cref="Design.SettingRow"/> nicht an, darum trägt jedes Optionsfeld
/// seinen eigenen Namen.
/// </summary>
public partial class ModelList : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ModelsProperty = AvaloniaProperty.Register<ModelList, IEnumerable?>(nameof(Models));
    public static readonly StyledProperty<IEnumerable?> FilesProperty = AvaloniaProperty.Register<ModelList, IEnumerable?>(nameof(Files));

    public ModelList() => InitializeComponent();

    public IEnumerable? Models { get => GetValue(ModelsProperty); set => SetValue(ModelsProperty, value); }

    public IEnumerable? Files { get => GetValue(FilesProperty); set => SetValue(FilesProperty, value); }

    /// <summary>Eine Optionsgruppe je Liste (Modelle und Dateien zusammen).</summary>
    public string GroupName { get; } = "models-" + Guid.NewGuid().ToString("N");
}
