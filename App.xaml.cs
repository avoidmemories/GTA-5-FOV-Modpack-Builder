using System.Windows;
using FovApp;

namespace FovApp;

public partial class App : Application
{
    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0] == "--test")
        {
            Shutdown(Tests.Run(e.Args));
            return;
        }
        var win = new MainWindow();
        win.Show();
    }
}
