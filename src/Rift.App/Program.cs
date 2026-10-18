using Avalonia;
using Rift.Core;
using Rift.Core.Localization;
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
        // For installers and manual setup: Rift.exe --register-shell / --unregister-shell
        if (OperatingSystem.IsWindows() && args is [var flag] && flag.StartsWith("--", StringComparison.Ordinal))
        {
            switch (flag)
            {
                case "--register-shell":
                    ExplorerIntegration.Register(Environment.ProcessPath!, Strings.ExplorerMenuText);
                    return 0;
                case "--unregister-shell":
                    ExplorerIntegration.Unregister();
                    return 0;
            }
        }

        var parsed = StartupArgs.Parse(args);
        if (!parsed.NewWindow)
        {
            Instance = InstanceChannel.TryClaim(InstanceChannel.DefaultName);
            // Restarting (language change): the old window is closing, wait for it to let go.
            for (var i = 0; parsed.Restart && Instance is null && i < 100; i++)
            {
                Thread.Sleep(100);
                Instance = InstanceChannel.TryClaim(InstanceChannel.DefaultName);
            }
            if (Instance is null)
            {
                if (OperatingSystem.IsWindows())
                    Foreground.AllowAny();
                // If the running window doesn't answer (hung, closing), open our own instead.
                if (InstanceChannel.SendAsync(InstanceChannel.DefaultName, parsed.ToForwardedArgs(), TimeSpan.FromSeconds(3))
                    .GetAwaiter().GetResult())
                    return 0;
            }
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Instance?.Dispose();
        }
    }

    /// <summary>Set when this process owns the single Rift window; later launches send their paths here.</summary>
    public static InstanceChannel? Instance { get; private set; }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Not WinUIComposition (Avalonia's default on Windows 10+): it deadlocks disposing the window on close
            // while icons and thumbnails are still rendering, and the process lingers with no window.
            .With(new Win32PlatformOptions { CompositionMode = [Win32CompositionMode.DirectComposition, Win32CompositionMode.RedirectionSurface] })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
