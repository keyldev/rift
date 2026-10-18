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

        // Tab перехватываем до того, как его съест обычная навигация фокусом.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => LeftPane.FocusFiles();
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || e.KeyModifiers != KeyModifiers.None || Vm is not { IsSplit: true } vm)
            return;

        vm.SwitchPaneCommand.Execute(null);
        (vm.ActivePane == vm.Left ? LeftPane : RightPane).FocusFiles();
        e.Handled = true;
    }
}
