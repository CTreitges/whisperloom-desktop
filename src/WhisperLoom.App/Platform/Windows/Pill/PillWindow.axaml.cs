using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using WhisperLoom.App.Platform.Windows.Overlay;

namespace WhisperLoom.App.Platform.Windows.Pill;

public sealed partial class PillViewModel : ObservableObject
{
    [ObservableProperty]
    private double _level;

    /// <summary>Losgelassen: Verarbeitung läuft (Lauf-Balken statt Pegel).</summary>
    [ObservableProperty]
    private bool _processing;
}

/// <summary>Pegel-Pille beim Halten (fokusfrei).</summary>
public partial class PillWindow : Window
{
    public PillWindow()
    {
        InitializeComponent();
        NoActivate.Apply(this);
    }

    public PillWindow(PillViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
