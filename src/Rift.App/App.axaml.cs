using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System.Globalization;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Rift.App.Services;
using Rift.App.ViewModels;
using Rift.App.Views;
using Rift.Core;
using Rift.Core.Localization;

namespace Rift.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = StartupArgs.Parse(desktop.Args ?? []);
            // Must be set before any view is built: XAML reads strings once, at load.
            if (args.Language is { } lang && Strings.Supported.Contains(lang))
                Strings.Culture = new CultureInfo(lang);

            // Started plainly (Start menu, taskbar): bring back the last session. Started with paths: open exactly those.
            var session = args.HasPaths ? null : SessionStore.Load(SessionStore.LastSessionFile);
            var vm = new MainWindowViewModel(args, session);
            var window = new MainWindow { DataContext = vm };
            window.RestorePlacement(session?.Window);
            desktop.MainWindow = window;

            RecentPlaces.Initialize();
            Program.Instance?.Listen(forwarded =>
                Dispatcher.UIThread.Post(() => vm.OpenFromAnotherInstance(StartupArgs.Parse(forwarded))));
        }

        base.OnFrameworkInitializationCompleted();
    }
}