using System.IO;
using System.Windows;

namespace Paomian.KeyboardPrototype;

public partial class App : Application
{
    internal bool IsDark { get; private set; }

    internal void SetTheme(bool dark)
    {
        if (IsDark == dark) return;
        var dictionaries = Resources.MergedDictionaries;
        dictionaries[0] = new ResourceDictionary { Source = new Uri($"Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        IsDark = dark;
    }

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
