using System.Windows;
namespace PixelForge.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Tests can supply their own window before entering the dispatcher.
        if (Windows.Count == 0) new MainWindow().Show();
    }
}
