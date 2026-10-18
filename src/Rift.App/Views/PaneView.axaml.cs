using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Rift.App.ViewModels;

namespace Rift.App.Views;

public partial class PaneView : UserControl
{
    public PaneView()
    {
        InitializeComponent();

        // Клик в любом месте панели делает её активной, даже если событие уже обработал дочерний контрол.
        AddHandler(PointerPressedEvent, (_, _) => Activate(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, _) => Activate(), RoutingStrategies.Bubble);

        Files.DoubleTapped += (_, _) => OpenSelected();
        Files.KeyDown += OnFilesKeyDown;
        Files.SelectionChanged += (_, _) => SyncSelection();
    }

    private PaneViewModel? Pane => DataContext as PaneViewModel;

    public void FocusFiles() => Files.Focus();

    private void Activate()
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel main && Pane is { } pane)
            main.ActivePane = pane;
    }

    private void OpenSelected()
    {
        if (Pane?.SelectedTab is { } tab && Files.SelectedItem is FileItem item)
            _ = tab.Open(item);
    }

    private void OnFilesKeyDown(object? sender, KeyEventArgs e)
    {
        if (Pane?.SelectedTab is not { } tab)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                OpenSelected();
                e.Handled = true;
                break;
            case Key.Back:
                tab.UpCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void SyncSelection()
    {
        if (Pane?.SelectedTab is not { } tab)
            return;
        tab.Selected.Clear();
        if (Files.SelectedItems is { } items)
        {
            foreach (var item in items.OfType<FileItem>())
                tab.Selected.Add(item);
        }
        tab.UpdateStatus();
    }
}
