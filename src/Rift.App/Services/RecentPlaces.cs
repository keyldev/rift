using System.Diagnostics;
using System.Runtime.Versioning;
using Rift.Core;
using Rift.Core.Localization;
using Rift.Shell;

namespace Rift.App.Services;

/// <summary>Remembers opened folders and mirrors them into the taskbar jump list.</summary>
public static class RecentPlaces
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);
    private static readonly RecentFolders Folders = new(RecentFolders.DefaultFile);
    [SupportedOSPlatform("windows")]
    private static readonly Lazy<StaWorker> Worker = new(() => new StaWorker("Rift jump list"));
    private static readonly Timer FlushTimer = new(_ => _ = FlushAsync());
    private static (string Recent, string NewWindow, string NewWindowTip)? _labels;

    /// <summary>Builds the jump list once at startup, so the tasks are there before any navigation.</summary>
    public static void Initialize()
    {
        // Read on the UI thread: --lang sets the culture there.
        _labels = (Strings.JumpRecent, Strings.JumpNewWindow, Strings.JumpNewWindowTip);
        FlushTimer.Change(Delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Newest first.</summary>
    public static IReadOnlyList<string> Items => Folders.Items;

    public static void Visit(string path)
    {
        if (Folders.Add(path))
            FlushTimer.Change(Delay, Timeout.InfiniteTimeSpan);
    }

    private static async Task FlushAsync()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                await UpdateJumpListAsync();
            Folders.Save();
        }
        catch (Exception ex)
        {
            // The jump list is a nicety; never let it disturb the app.
            Trace.WriteLine($"Jump list update failed: {ex}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task UpdateJumpListAsync()
    {
        if (_labels is not { } l || Environment.ProcessPath is not { } exe)
            return;
        var labels = new JumpList.Labels(l.Recent, l.NewWindow, l.NewWindowTip);
        var removed = await Worker.Value.Invoke(() => JumpList.Update(exe, Folders.Items, labels));
        Folders.Remove(removed);
    }
}
