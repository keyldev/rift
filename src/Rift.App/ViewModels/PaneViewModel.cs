using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Rift.App.ViewModels;

/// <summary>A group of tabs shown side by side with other groups.</summary>
public partial class PaneViewModel : ViewModelBase
{
    public PaneViewModel(params string[] paths)
    {
        foreach (var p in paths)
            Tabs.Add(new TabViewModel(p));
        _selectedTab = Tabs[0];
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    [ObservableProperty] private TabViewModel _selectedTab;
    [ObservableProperty] private bool _isActive;

    // Drop zone shown while files are dragged over the pane.
    [ObservableProperty] private bool _isDropTarget;
    [ObservableProperty] private bool _dropIsCopy;
    [ObservableProperty] private string _dropTitle = "";
    [ObservableProperty] private string _dropHint = "";

    [RelayCommand]
    private void NewTab()
    {
        var tab = new TabViewModel(SelectedTab.Path);
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private void CloseTab(TabViewModel tab)
    {
        if (Tabs.Count < 2)
            return;
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (SelectedTab == tab)
            SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
    }
}
