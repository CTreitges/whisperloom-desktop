using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace WhisperLoom.App.Tests;

public class MainWindowTest
{
    [AvaloniaFact]
    public void Shows_product_name()
    {
        var window = new MainWindow();
        window.Show();

        var headline = window.FindControl<TextBlock>("Headline");

        Assert.NotNull(headline);
        Assert.Equal("WhisperLoom", headline.Text);
        window.Close();
    }
}
