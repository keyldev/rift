using Rift.Core;
using Rift.Core.Localization;

namespace Rift.App.ViewModels;

public sealed record SidebarItem(string Label, string Path, string Icon, double? Used = null, string? Meta = null)
{
    public bool HasUsage => Used is not null;
    public string Tip => Locations.IsVirtual(Path) ? Label : Path;

    public static IReadOnlyList<SidebarItem> QuickAccess()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            new(Strings.PlaceDesktop, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Icon.Desktop"),
            new(Strings.PlaceDownloads, System.IO.Path.Combine(home, "Downloads"), "Icon.Download"),
            new(Strings.PlaceDocuments, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Icon.Document"),
            new(Strings.PlaceHome, home, "Icon.Home"),
            new(Strings.PlaceThisPc, Locations.ThisPc, "Icon.Pc"),
            new(Strings.PlaceLibraries, Locations.LibrariesFolder, "Icon.Library"),
            new(Strings.PlaceNetwork, Locations.Network, "Icon.Network"),
            new(Strings.PlaceRecycleBin, Locations.RecycleBin, "Icon.Trash"),
        ];
    }

    /// <summary>Ready drives with a usage bar; a drive still thinking (a sleeping share) by its letter alone, for now.</summary>
    public static IReadOnlyList<SidebarItem> Drives(IEnumerable<DriveFacts> drives)
    {
        var list = new List<SidebarItem>();
        foreach (var d in drives)
        {
            var icon = d.Type == DriveType.Network ? "Icon.Server" : "Icon.Drive";
            if (d.IsSlow)
            {
                list.Add(new($"({d.Letter})", d.Name, icon));
                continue;
            }
            // Empty card readers and DVD drives stay out of the sidebar; This PC still lists them.
            if (!d.IsReady)
                continue;
            var label = string.IsNullOrWhiteSpace(d.Label) ? Strings.DriveLocal : d.Label;
            var used = d.TotalSize > 0 ? 1 - (double)d.FreeSpace / d.TotalSize : 0;
            list.Add(new($"{label} ({d.Letter})", d.Name, icon, used,
                Strings.DriveFree(SizeFormatter.Format(d.FreeSpace), SizeFormatter.Format(d.TotalSize))));
        }
        return list;
    }
}
