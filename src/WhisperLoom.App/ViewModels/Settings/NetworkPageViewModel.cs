namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>Seite „Netzwerk“: Proxy; die Adresse gilt nur bei „Manuell“.</summary>
public sealed class NetworkPageViewModel : SettingsPageViewModel
{
    public NetworkPageViewModel(SettingsViewModel session) : base("network", "Connections", "Blue", session)
    {
        session.PropertyChanged += OnSessionChanged;
    }

    public bool IsManual => Session.ProxyMode.Value == "manual";

    public override void Dispose()
    {
        Session.PropertyChanged -= OnSessionChanged;
        base.Dispose();
    }

    private void OnSessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.ProxyMode))
        {
            OnPropertyChanged(nameof(IsManual));
        }
    }
}
