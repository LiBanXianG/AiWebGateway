using System.Windows;

namespace AiWebGateway;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mainWindow = new MainWindow();
        mainWindow.Show();
        mainWindow.Hide();
    }
}