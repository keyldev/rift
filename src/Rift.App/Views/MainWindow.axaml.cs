using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Rift.App.ViewModels;

namespace Rift.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Catch Tab before regular focus navigation swallows it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            if (Vm is { } vm)
            {
                vm.WindowHandle = () => TryGetPlatformHandle()?.Handle ?? 0;
                vm.FocusFilesRequested += FocusActivePane;
            }
            LeftPane.FocusFiles();
        };
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void FocusActivePane()
    {
        if (Vm is { } vm)
            (vm.ActivePane == vm.Right && vm.IsSplit ? RightPane : LeftPane).FocusFiles();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // While a dialog is open it owns the keyboard: Enter = primary button, Esc = cancel, nothing leaks to hotkeys.
        if (Vm?.Dialog is { } dialog)
        {
            if (e.Key == Key.Enter)
                dialog.ChoosePrimary();
            else if (e.Key == Key.Escape)
                dialog.ChooseCancel();
            if (e.Key is not (Key.Tab or Key.Space))
                e.Handled = true;
            return;
        }

        if (e.Key != Key.Tab || e.KeyModifiers != KeyModifiers.None || Vm is not { IsSplit: true } vm)
            return;

        vm.SwitchPaneCommand.Execute(null);
        (vm.ActivePane == vm.Left ? LeftPane : RightPane).FocusFiles();
        e.Handled = true;
    }
}
