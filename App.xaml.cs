using System.Windows;
using System.Windows.Threading;

namespace UpperMachine;

public partial class App : Application
{
    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        SplashWindow splashWindow = new();
        splashWindow.Show();

        await Dispatcher.Yield(DispatcherPriority.Background);
        await Task.Delay(900);

        MainWindow mainWindow = new();
        MainWindow = mainWindow;

        mainWindow.Loaded += (_, _) =>
        {
            if (splashWindow.IsVisible)
            {
                splashWindow.Close();
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;
        };

        mainWindow.Show();
    }
}
