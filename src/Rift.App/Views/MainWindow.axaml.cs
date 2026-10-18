using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Rift.App.ViewModels;

namespace Rift.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Catch Tab before regular focus navigation swallows it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        PaletteScrim.PointerPressed += OnPaletteScrimPressed;
        PaletteList.Tapped += OnPaletteItemTapped;
        Opened += (_, _) =>
        {
            if (Vm is { } vm)
            {
                vm.WindowHandle = () => TryGetPlatformHandle()?.Handle ?? 0;
                vm.SetClipboardText = text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
                vm.FocusFilesRequested += FocusActivePane;
                vm.ActivateRequested += BringToFront;
                vm.Palette.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(CommandPaletteViewModel.IsOpen))
                        return;
                    if (vm.Palette.IsOpen)
                        Dispatcher.UIThread.Post(() => PaletteBox.Focus(), DispatcherPriority.Loaded);
                    else
                        FocusActivePane();
                };
                vm.FocusSearchRequested += () => (vm.ActivePane == vm.Right && vm.IsSplit ? RightPane : LeftPane).FocusSearch();
            }
            LeftPane.FocusFiles();
        };
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnPaletteScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        // A click outside the palette card closes it.
        if (e.Source == PaletteScrim)
            Vm?.Palette.Close();
    }

    private void OnPaletteItemTapped(object? sender, TappedEventArgs e)
    {
        if (Vm?.Palette is { } palette)
            _ = palette.RunSelectedAsync();
    }

    private void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        FocusActivePane();
    }

    private void FocusActivePane()
    {
        if (Vm is { } vm)
            (vm.ActivePane == vm.Right && vm.IsSplit ? RightPane : LeftPane).FocusFiles();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // The palette keeps typing in its box; arrows move through results, Enter runs one, Esc closes.
        if (Vm?.Palette is { IsOpen: true } palette)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    palette.Close();
                    e.Handled = true;
                    break;
                case Key.Down or Key.Up:
                    palette.Move(e.Key == Key.Down ? 1 : -1);
                    PaletteList.ScrollIntoView(palette.SelectedIndex);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    _ = palette.RunSelectedAsync();
                    e.Handled = true;
                    break;
            }
            return;
        }

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
