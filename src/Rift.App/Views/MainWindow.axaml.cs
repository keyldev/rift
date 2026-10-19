using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Rift.App.Services;
using Rift.App.ViewModels;
using Rift.Core;

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
        Panes.CreateView = pane => new PaneView { DataContext = pane };
        Panes.RatioChanged = (split, ratio) => Vm?.SetRatio(split, ratio);
        Closing += OnClosing;
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
                vm.FocusSearchRequested += () => ActiveView()?.FocusSearch();
                vm.LayoutChanged += BuildLayout;
                Win32Properties.AddWndProcHookCallback(this, OnWndProc);
                Activated += (_, _) => vm.RefreshDrives();
                ApplyHotkeys();
                ApplyBackdrop();
                AppSettings.Changed += ApplyHotkeys;
                // Later, not inside the click that changed it: switching the composition mode mid-input hangs on Windows 10.
                AppSettings.Changed += () => Dispatcher.UIThread.Post(ApplyBackdrop, DispatcherPriority.Background);
                vm.Placement = SavePlacement;
                vm.Settings.RestartRequested += CloseLater;
                vm.Settings.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SettingsViewModel.IsOpen) && !vm.Settings.IsOpen)
                        FocusActivePane();
                };
            }
            BuildLayout();
            FocusActivePane();
        };
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private const uint WmDeviceChange = 0x0219;
    private const nint DbtDeviceArrival = 0x8000, DbtDeviceRemoveComplete = 0x8004;

    /// <summary>A USB stick or a card plugged in or pulled out: the drives in the sidebar and This PC follow.</summary>
    private nint OnWndProc(nint hwnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmDeviceChange && wParam is DbtDeviceArrival or DbtDeviceRemoveComplete)
            Vm?.RefreshDrives(devicesChanged: true);
        return 0;
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => CloseLater();

    /// <summary>
    /// Closes outside the current input event: Avalonia 12 deadlocks disposing the composition surface
    /// when Close() runs inside a click handler (the process lingered without a window).
    /// </summary>
    private void CloseLater() => Dispatcher.UIThread.Post(Close, DispatcherPriority.Background);

    private void OnDialogInputAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox box)
            Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Loaded);
    }

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

    private void FocusActivePane() => Dispatcher.UIThread.Post(() => ActiveView()?.FocusFiles(), DispatcherPriority.Loaded);

    private PaneView? ActiveView() => Vm is { } vm ? Panes.ViewFor(vm.ActivePane) as PaneView : null;

    /// <summary>
    /// Saves the session. With a backdrop effect on, Avalonia can deadlock disposing the window's composition
    /// surface (seen with acrylic on Windows 10): everything is saved by then, so a watchdog ends the process
    /// if it hasn't ended by itself a few seconds later.
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        Vm?.SaveSession();
        AppSettings.SaveNow();
        if (ActualTransparencyLevel != WindowTransparencyLevel.Mica)
            return;
        new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(4));
            Environment.Exit(0);
        }) { IsBackground = true, Name = "Rift exit watchdog" }.Start();
    }

    /// <summary>Asks Windows for Mica or Acrylic; panes go see-through only if it actually said yes.</summary>
    private void ApplyBackdrop()
    {
        // Only Mica, only on Windows 11. The hint is left alone otherwise: setting it at all (even to None) moves
        // the window to transparent composition, and Avalonia then deadlocks disposing it on close.
        var mica = AppSettings.Current.Backdrop == "mica" && AppSettings.MicaSupported;
        if (mica)
            TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
        else if (TransparencyLevelHint.Count > 0)
            TransparencyLevelHint = [];
        AppSettings.SetSeeThrough(mica && ActualTransparencyLevel == WindowTransparencyLevel.Mica);
    }

    /// <summary>Window-wide shortcuts from the command list, with the user's own keys from Settings.</summary>
    private void ApplyHotkeys()
    {
        if (Vm is not { } vm)
            return;
        KeyBindings.Clear();
        foreach (var command in AppCommands.All)
        {
            var run = new CommunityToolkit.Mvvm.Input.RelayCommand(() => command.Run(vm));
            foreach (var gesture in command.Keys)
                KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = run });
        }
    }

    private void BuildLayout()
    {
        if (Vm is { } vm)
            Panes.Build(vm.Layout, vm.IsZoomed ? vm.ActivePane : null);
    }

    private WindowPlacement SavePlacement() => new()
    {
        X = Position.X,
        Y = Position.Y,
        // The size to come back to: when maximized, the size it had before.
        Width = Width,
        Height = Height,
        Maximized = WindowState == WindowState.Maximized,
    };

    /// <summary>Puts the window where the last session left it, if that spot is still on a screen.</summary>
    public void RestorePlacement(WindowPlacement? placement)
    {
        if (placement is null || placement.Width < MinWidth || placement.Height < MinHeight)
            return;
        Width = placement.Width;
        Height = placement.Height;
        var position = new PixelPoint(placement.X, placement.Y);
        if (Screens.All.Any(s => s.WorkingArea.Contains(position + new PixelPoint(40, 40))))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = position;
        }
        if (placement.Maximized)
            WindowState = WindowState.Maximized;
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

        // Settings own the keyboard: shortcut recording and Esc first; keys aimed elsewhere (focus still
        // in a file list) are swallowed, so Delete can't delete files behind the Settings screen.
        if (Vm?.Settings is { IsOpen: true } settings)
        {
            if (settings.OnKey(e.Key, e.KeyModifiers))
                e.Handled = true;
            else if (e.Key == Key.Escape)
            {
                settings.CloseCommand.Execute(null);
                e.Handled = true;
            }
            else if ((e.Source as Visual)?.FindAncestorOfType<SettingsView>(includeSelf: true) is null)
                e.Handled = true;
            return;
        }

        // While a dialog is open it owns the keyboard: Enter = primary button, Esc = cancel, nothing leaks to hotkeys.
        if (Vm?.Dialog is { } dialog)
        {
            if (e.Key == Key.Enter)
                dialog.ChoosePrimary();
            else if (e.Key == Key.Escape)
                dialog.ChooseCancel();
            // Typing goes to the dialog's text box, if it has one; nothing else leaks to hotkeys.
            if (e.Key is not (Key.Tab or Key.Space) && !dialog.HasInput)
                e.Handled = true;
            else if (e.Key is Key.Enter or Key.Escape)
                e.Handled = true;
            return;
        }

        if (e.Key != Key.Tab || Vm is not { } vm)
            return;

        switch (e.KeyModifiers)
        {
            // Tab / Shift+Tab: next / previous pane.
            case KeyModifiers.None or KeyModifiers.Shift:
                if (!vm.HasManyPanes || vm.IsZoomed)
                    return;
                vm.FocusPane(e.KeyModifiers == KeyModifiers.Shift ? -1 : 1);
                break;
            // Ctrl+Tab / Ctrl+Shift+Tab: next / previous tab in the pane.
            case KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift):
                vm.ActivePane.SelectNextTab(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                FocusActivePane();
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
