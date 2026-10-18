using Rift.Core;
using Rift.Core.Localization;

namespace Rift.App.ViewModels;

public sealed record SidebarItem(string Label, string Path, string Icon, double? Used = null, string? Meta = null)
{
    public bool HasUsage => Used is not null;

    public static IReadOnlyList<SidebarItem> QuickAccess()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            new(Strings.PlaceDesktop, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Icon.Desktop"),
            new(Strings.PlaceDownloads, System.IO.Path.Combine(home, "Downloads"), "Icon.Download"),
            new(Strings.PlaceDocuments, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Icon.Document"),
            new(Strings.PlaceHome, home, "Icon.Home"),
        ];
    }

    public static IReadOnlyList<SidebarItem> Drives()
    {
        var list = new List<SidebarItem>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady)
                    continue;
                var letter = d.Name.TrimEnd('\\');
                var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? Strings.DriveLocal : d.VolumeLabel;
                var used = d.TotalSize > 0 ? 1 - (double)d.AvailableFreeSpace / d.TotalSize : 0;
                var icon = d.DriveType == DriveType.Network ? "Icon.Server" : "Icon.Drive";
                list.Add(new($"{label} ({letter})", d.Name, icon, used,
                    Strings.DriveFree(SizeFormatter.Format(d.AvailableFreeSpace), SizeFormatter.Format(d.TotalSize))));
            }
            catch (IOException)
            {
                // Disk went away while we were reading it (card reader, unplugged USB).
            }
        }
        return list;
    }
}
