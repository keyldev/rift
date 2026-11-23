using System.Globalization;
using Rift.Core;
using Rift.Core.Localization;

namespace Rift.Core.Tests;

public class DiskUsageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rift-usage-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("app.exe", DiskUsageCategory.Apps)]
    [InlineData("Core.DLL", DiskUsageCategory.Apps)]
    [InlineData("driver.sys", DiskUsageCategory.Apps)]
    [InlineData("setup.msi", DiskUsageCategory.Apps)]
    [InlineData("photo.JPG", DiskUsageCategory.Media)]
    [InlineData("clip.mkv", DiskUsageCategory.Media)]
    [InlineData("song.flac", DiskUsageCategory.Media)]
    [InlineData("backup.7z", DiskUsageCategory.Archives)]
    [InlineData("win.iso", DiskUsageCategory.Archives)]
    [InlineData("report.pdf", DiskUsageCategory.Documents)]
    [InlineData("Program.cs", DiskUsageCategory.Documents)]
    [InlineData("assets.pak", DiskUsageCategory.Other)]
    [InlineData("README", DiskUsageCategory.Other)]
    [InlineData("trailing.", DiskUsageCategory.Other)]
    public void Classifies_extensions_into_expected_categories(string name, DiskUsageCategory expected)
    {
        Assert.Equal(expected, DiskUsageClassifier.Classify(name));
    }

    [Fact]
    public void Scans_nested_folders_aggregates_sizes_and_computes_system_remainder()
    {
        var binDir = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;
        var nestedDir = Directory.CreateDirectory(Path.Combine(binDir, "nested")).FullName;
        var mediaDir = Directory.CreateDirectory(Path.Combine(_root, "media")).FullName;

        File.WriteAllBytes(Path.Combine(binDir, "app.exe"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(nestedDir, "helper.dll"), new byte[500]);
        File.WriteAllBytes(Path.Combine(mediaDir, "movie.mp4"), new byte[4000]);
        File.WriteAllBytes(Path.Combine(_root, "archive.zip"), new byte[800]);
        File.WriteAllBytes(Path.Combine(_root, "notes.md"), new byte[300]);
        File.WriteAllBytes(Path.Combine(_root, "cache.bin"), new byte[400]);

        // Scanned = 7000 bytes; Total = 20000, Free = 10000 -> Used = 10000 -> System remainder = 3000.
        var snapshots = new List<DiskUsageSnapshot>();
        var result = DiskUsageScanner.Scan(_root, totalBytes: 20_000, freeBytes: 10_000, snapshots.Add, CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.True(snapshots.Count >= 2);
        Assert.False(snapshots[0].IsComplete);
        Assert.Equal(7_000, result.ScannedBytes);
        Assert.Equal(6, result.ScannedFiles);
        Assert.Equal(3, result.ScannedFolders);

        var byCat = result.Buckets.ToDictionary(b => b.Category);
        Assert.Equal(10_000, byCat[DiskUsageCategory.Free].Bytes);
        Assert.Equal(1_500, byCat[DiskUsageCategory.Apps].Bytes);
        Assert.Equal(2, byCat[DiskUsageCategory.Apps].FileCount);
        Assert.Equal(4_000, byCat[DiskUsageCategory.Media].Bytes);
        Assert.Equal(800, byCat[DiskUsageCategory.Archives].Bytes);
        Assert.Equal(300, byCat[DiskUsageCategory.Documents].Bytes);
        Assert.Equal(400, byCat[DiskUsageCategory.Other].Bytes);
        Assert.Equal(3_000, byCat[DiskUsageCategory.System].Bytes);
    }

    [Fact]
    public void Sorts_buckets_by_size_count_and_name()
    {
        var previous = Strings.Culture;
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        try
        {
            DiskUsageBucket[] buckets =
            [
                new(DiskUsageCategory.Media, 500, 2),
                new(DiskUsageCategory.Apps, 200, 15),
                new(DiskUsageCategory.Archives, 900, 1),
            ];

            var bySize = DiskUsageScanner.Sort(buckets, DiskUsageSort.Size);
            Assert.Equal([DiskUsageCategory.Archives, DiskUsageCategory.Media, DiskUsageCategory.Apps], bySize.Select(b => b.Category));

            var byCount = DiskUsageScanner.Sort(buckets, DiskUsageSort.Count);
            Assert.Equal([DiskUsageCategory.Apps, DiskUsageCategory.Media, DiskUsageCategory.Archives], byCount.Select(b => b.Category));

            var byName = DiskUsageScanner.Sort(buckets, DiskUsageSort.Name);
            // "Apps & binaries", "Archives & images", "Media"
            Assert.Equal([DiskUsageCategory.Apps, DiskUsageCategory.Archives, DiskUsageCategory.Media], byName.Select(b => b.Category));
        }
        finally
        {
            Strings.Culture = previous;
        }
    }

    [Fact]
    public void Respects_cancellation_token()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            DiskUsageScanner.Scan(_root, 10_000, 5_000, null, cts.Token));
    }
}
