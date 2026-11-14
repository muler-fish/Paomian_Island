using System.IO;
using System.Windows;

namespace Paomian.KeyboardPrototype;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new ReaderWindow();
        MainWindow = window;
        if (e.Args is ["--verify", var output])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            EventHandler? verify = null;
            verify = async (_, _) =>
            {
                window.ContentRendered -= verify;
                try
                {
                    await PrototypeChecks.RunAsync(window, Path.GetFullPath(output));
                    Shutdown(0);
                }
                catch (Exception error)
                {
                    Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
                    Shutdown(1);
                }
            };
            window.ContentRendered += verify;
        }

        window.Show();
    }
}
