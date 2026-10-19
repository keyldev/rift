namespace Rift.Core;

/// <summary>A drive as Rift shows it. Sizes are 0 when it isn't ready or didn't answer in time.</summary>
/// <param name="IsSlow">Didn't answer in time (a mapped share whose server is asleep or gone); worth asking again later.</param>
public sealed record DriveFacts(string Name, DriveType Type, bool IsReady, string Label, long TotalSize, long FreeSpace, bool IsSlow = false)
{
    /// <summary>"C:" for "C:\".</summary>
    public string Letter => Name.TrimEnd('\\');
}

/// <summary>
/// Reads all drives at once, each on its own thread: a mapped network drive whose server is away can take
/// half a minute to say it isn't ready, and it shouldn't hold up the local disks or the window.
/// </summary>
public static class DriveReader
{
    /// <summary>How long <see cref="Read"/> waits for the slowest drive by default.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(500);

    /// <summary>Every drive; the ones that didn't answer within <paramref name="patience"/> come back with IsSlow set.</summary>
    public static IReadOnlyList<DriveFacts> Read(TimeSpan patience) => Read(DriveInfo.GetDrives(), patience, ReadOne);

    /// <summary>The waiting logic on its own, for tests: <paramref name="readOne"/> stands in for the disk.</summary>
    public static IReadOnlyList<DriveFacts> Read(IReadOnlyList<DriveInfo> drives, TimeSpan patience, Func<DriveInfo, DriveFacts> readOne)
    {
        var reads = drives.Select(d => Task.Run(() => readOne(d))).ToArray();
        Task.WaitAll(reads, patience);
        return drives.Select((d, i) => reads[i].IsCompletedSuccessfully
            ? reads[i].Result
            : new DriveFacts(d.Name, TypeOf(d), false, "", 0, 0, IsSlow: !reads[i].IsCompleted)).ToList();
    }

    private static DriveFacts ReadOne(DriveInfo d)
    {
        var type = TypeOf(d);
        try
        {
            return d.IsReady
                ? new DriveFacts(d.Name, type, true, d.VolumeLabel, d.TotalSize, d.AvailableFreeSpace)
                : new DriveFacts(d.Name, type, false, "", 0, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unplugged while we were looking at it, or a share we may not read.
            return new DriveFacts(d.Name, type, false, "", 0, 0);
        }
    }

    private static DriveType TypeOf(DriveInfo d)
    {
        try
        {
            return d.DriveType;
        }
        catch (IOException)
        {
            return DriveType.Unknown;
        }
    }
}
