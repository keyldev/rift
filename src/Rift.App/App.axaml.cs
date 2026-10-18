using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System.Globalization;
using Avalonia.Markup.Xaml;
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

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(args),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}