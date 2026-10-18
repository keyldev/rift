using Avalonia;
using Rift.Shell;

namespace Rift.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Для установщика и ручной настройки: Rift.exe --register-shell / --unregister-shell
        if (OperatingSystem.IsWindows() && args is [var flag] && flag.StartsWith("--", StringComparison.Ordinal))
        {
            switch (flag)
            {
                case "--register-shell":
                    ExplorerIntegration.Register(Environment.ProcessPath!);
                    return 0;
                case "--unregister-shell":
                    ExplorerIntegration.Unregister();
                    return 0;
            }
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
