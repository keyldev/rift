using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rift.Core;

namespace Rift.App.ViewModels;

/// <summary>A group of tabs; the window holds several of them in a split layout.</summary>
public partial class PaneViewModel : ViewModelBase
{
    public PaneViewModel(params string[] paths) : this(paths.Select(p => new TabViewModel(p)))
    {
    }

    public PaneViewModel(IEnumerable<TabViewModel> tabs)
    {
        foreach (var tab in tabs)
            Tabs.Add(tab);
        _selectedTab = Tabs[0];
    }

    /// <summary>Called when the last tab closes: the window removes the pane (unless it's the only one).</summary>
    public Func<PaneViewModel, bool>? CloseRequested { get; set; }

    /// <summary>A tab was closed (for "reopen closed tab").</summary>
    public Action<TabState>? TabClosed { get; set; }

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    [ObservableProperty] private TabViewModel _selectedTab;
    [ObservableProperty] private bool _isActive;

    /// <summary>Alt+P: the preview pane on the right of the file list.</summary>
    [ObservableProperty] private bool _isPreviewOpen;
    [ObservableProperty] private double _previewWidth = 320;

    public PreviewViewModel Preview { get; } = new();

    /// <summary>The detail columns this pane has room for.</summary>
    public PaneColumns Columns { get; } = new();

    [RelayCommand]
    private void TogglePreview() => IsPreviewOpen = !IsPreviewOpen;

    public void ResizePreview(double delta) => PreviewWidth = Math.Clamp(PreviewWidth + delta, 200, 900);

    // Drop zone shown while files are dragged over the pane.
    [ObservableProperty] private bool _isDropTarget;
    [ObservableProperty] private bool _dropIsCopy;
    [ObservableProperty] private string _dropTitle = "";
    [ObservableProperty] private string _dropHint = "";

    /// <summary>Where a dragged tab would land: a highlighted part of the pane (null when no tab is dragged over).</summary>
    [ObservableProperty] private TabDropZone? _tabDropZone;

    [RelayCommand]
    private void NewTab() => OpenTab(SelectedTab.Path);

    public TabViewModel OpenTab(string path)
    {
        var tab = new TabViewModel(path);
        Attach(tab, Tabs.Count);
        return tab;
    }

    /// <summary>Puts a tab (new, or taken from another pane) at <paramref name="index"/> and selects it. Pinned tabs stay in front.</summary>
    public void Attach(TabViewModel tab, int index)
    {
        var pinned = Tabs.Count(t => t.IsPinned);
        index = tab.IsPinned ? Math.Min(index, pinned) : Math.Clamp(index, pinned, Tabs.Count);
        Tabs.Insert(index, tab);
        SelectedTab = tab;
    }

    /// <summary>Takes a tab out without closing it (it's moving to another pane). False if it was the last one.</summary>
    public bool Detach(TabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return false;
        if (Tabs.Count == 1)
        {
            Tabs.Remove(tab);
            return false;
        }
        if (SelectedTab == tab)
            SelectedTab = Tabs[index + 1 < Tabs.Count ? index + 1 : index - 1];
        Tabs.Remove(tab);
        return true;
    }

    /// <summary>Ctrl+W and the × button. Pinned tabs ignore it: unpin first, or use Close in the tab's menu.</summary>
    [RelayCommand]
    private void CloseTab(TabViewModel tab)
    {
        if (!tab.IsPinned)
            Close(tab);
    }

    /// <summary>Closes a tab whatever it is; closing the last tab closes the pane.</summary>
    public void Close(TabViewModel tab)
    {
        if (!Tabs.Contains(tab))
            return;
        if (Tabs.Count == 1)
        {
            // The last tab of the only pane stays: there must be somewhere to look.
            if (CloseRequested?.Invoke(this) == true)
            {
                TabClosed?.Invoke(tab.ToState());
                tab.Dispose();
            }
            return;
        }
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        TabClosed?.Invoke(tab.ToState());
        tab.Dispose();
        if (SelectedTab == tab)
            SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
    }

    public void CloseOthers(TabViewModel keep)
    {
        foreach (var tab in Tabs.Where(t => t != keep && !t.IsPinned).ToList())
            Close(tab);
        SelectedTab = keep;
    }

    public void CloseToTheRight(TabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        foreach (var t in Tabs.Skip(index + 1).Where(t => !t.IsPinned).ToList())
            Close(t);
    }

    /// <summary>A copy of the tab right after it: same folder, view and sort, its own history from here on.</summary>
    public TabViewModel Duplicate(TabViewModel tab)
    {
        var copy = tab.Clone();
        Attach(copy, Tabs.IndexOf(tab) + 1);
        return copy;
    }

    /// <summary>Pinned tabs move to the front, keep their place and survive "close others".</summary>
    public void TogglePin(TabViewModel tab)
    {
        Tabs.Remove(tab);
        tab.IsPinned = !tab.IsPinned;
        Attach(tab, tab.IsPinned ? Tabs.Count(t => t.IsPinned) : Tabs.Count(t => t.IsPinned));
    }

    /// <summary>Ctrl+Tab / Ctrl+Shift+Tab.</summary>
    public void SelectNextTab(int step)
    {
        var index = Tabs.IndexOf(SelectedTab);
        SelectedTab = Tabs[((index + step) % Tabs.Count + Tabs.Count) % Tabs.Count];
    }

    public PaneState ToState() => new()
    {
        Tabs = Tabs.Select(t => t.ToState()).ToList(),
        Selected = Tabs.IndexOf(SelectedTab),
        Preview = IsPreviewOpen,
        PreviewWidth = PreviewWidth,
    };

    public static PaneViewModel FromState(PaneState state)
    {
        var pane = new PaneViewModel(state.Tabs.Select(TabViewModel.FromState))
        {
            IsPreviewOpen = state.Preview,
            PreviewWidth = Math.Clamp(state.PreviewWidth, 200, 900),
        };
        pane.SelectedTab = pane.Tabs[Math.Clamp(state.Selected, 0, pane.Tabs.Count - 1)];
        return pane;
    }
}

public enum TabDropZone
{
    /// <summary>Into this pane, as one more tab.</summary>
    Center,
    Left,
    Right,
    Top,
    Bottom,
}
