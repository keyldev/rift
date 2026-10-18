using CommunityToolkit.Mvvm.Input;
using Rift.Core;

namespace Rift.App.ViewModels;

/// <summary>Tab commands: duplicate, pin, close others, reopen closed (Ctrl+Shift+T).</summary>
public partial class MainWindowViewModel
{
    private const int ClosedTabsKept = 20;
    private readonly Stack<TabState> _closedTabs = new();

    /// <summary>A pane reports every tab it closes, so Ctrl+Shift+T can bring it back.</summary>
    private void RememberClosed(TabState tab)
    {
        _closedTabs.Push(tab);
        if (_closedTabs.Count > ClosedTabsKept)
        {
            var keep = _closedTabs.Take(ClosedTabsKept).Reverse().ToList();
            _closedTabs.Clear();
            foreach (var t in keep)
                _closedTabs.Push(t);
        }
    }

    [RelayCommand]
    private void ReopenTab()
    {
        if (_closedTabs.TryPop(out var state))
        {
            ActivePane.Attach(TabViewModel.FromState(state), ActivePane.Tabs.Count);
            FocusFilesRequested?.Invoke();
        }
    }

    [RelayCommand]
    private void DuplicateTab() => ActivePane.Duplicate(ActivePane.SelectedTab);

    [RelayCommand]
    private void TogglePinTab() => ActivePane.TogglePin(ActivePane.SelectedTab);

    [RelayCommand]
    private void CloseOtherTabs() => ActivePane.CloseOthers(ActivePane.SelectedTab);
}
