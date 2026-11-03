using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.Overlay;

namespace WhisperLoom.App.Platform.Windows.Pill;

public sealed partial class PillViewModel : ObservableObject
{
    public PillViewModel() => Loc.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(AccessibleName));

    [ObservableProperty]
    private double _level;

    /// <summary>Losgelassen: Verarbeitung läuft (Lauf-Balken statt Pegel).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName), nameof(Moving), nameof(ShowBar))]
    private bool _processing;

    /// <summary>Bewegung erlaubt (Systemeinstellung „Animationen anzeigen“); aus = beim Verarbeiten nur das ruhende Symbol.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Moving), nameof(ShowBar))]
    private bool _motion = true;

    public bool Moving => Processing && Motion;

    public bool ShowBar => !Processing || Motion;

    /// <summary>Name für Screenreader: „WhisperLoom – Aufnahme läuft“ bzw. „… Verarbeitung läuft“ (die Pille zeigt kein Wort).</summary>
    public string AccessibleName => Loc.Instance[Processing ? "Pill_Processing" : "Pill_Recording"];
}

/// <summary>Pegel-Pille beim Halten (fokusfrei), folgt dem Systemthema (Punkt 3).</summary>
public partial class PillWindow : Window
{
    public PillWindow()
    {
        InitializeComponent();
        NoActivate.Apply(this);
        Design.SystemTheme.Follow(this, Application.Current?.PlatformSettings);
    }

    public PillWindow(PillViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
