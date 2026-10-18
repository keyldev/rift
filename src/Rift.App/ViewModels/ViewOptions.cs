using CommunityToolkit.Mvvm.ComponentModel;
using Rift.Shell;

namespace Rift.App.ViewModels;

/// <summary>View preferences shared by every tab. Not saved yet: settings arrive in 0.6.</summary>
public sealed partial class ViewOptions : ObservableObject
{
    public static ViewOptions Current { get; } = new();

    /// <summary>Hidden and system files; starts from Explorer's own setting.</summary>
    [ObservableProperty] private bool _showHidden = OperatingSystem.IsWindows() && ExplorerSettings.ShowsHiddenFiles();
}
