using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rift.App.Controls;
using Rift.Core;
using Rift.Core.Localization;

namespace Rift.App.ViewModels;

public sealed record DriveOption(DriveFacts Drive, string Label)
{
    public string Path => Drive.Name;
}

public sealed partial class DiskUsageRow : ObservableObject
{
    public DiskUsageRow(
        DiskUsageCategory category,
        string label,
        IBrush brush,
        long bytes,
        long fileCount,
        double fraction,
        bool showFileCount)
    {
        Category = category;
        Label = label;
        Brush = brush;
        Bytes = bytes;
        FileCount = fileCount;
        Fraction = fraction;
        SizeText = SizeFormatter.Format(bytes);
        PercentText = FormatPercent(fraction);
        CountText = showFileCount ? Strings.DiskFileCount(fileCount) : "";
        HasCount = showFileCount;
    }

    public DiskUsageCategory Category { get; }
    public string Label { get; }
    public IBrush Brush { get; }
    public long Bytes { get; }
    public long FileCount { get; }
    public double Fraction { get; }
    public double PercentValue => Math.Clamp(Fraction * 100, 0, 100);
    public string SizeText { get; }
    public string PercentText { get; }
    public string CountText { get; }
    public bool HasCount { get; }

    [ObservableProperty] private bool _isHighlighted;

    internal static string FormatPercent(double fraction)
    {
        var pct = fraction * 100;
        if (pct <= 0)
            return "0%";
        if (pct < 0.1)
            return "<0.1%";
        return pct >= 10 ? $"{pct:0.#}%" : $"{pct:0.0}%";
    }
}

/// <summary>Overlay that scans a drive off the UI thread and displays a color-coded donut chart + sorted category breakdown.</summary>
public sealed partial class DiskUsageViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    private CancellationTokenSource? _scanCts;
    private DiskUsageSnapshot? _snapshot;
    private bool _wasCancelled;
    private bool _switchingDrive;

    public DiskUsageViewModel(MainWindowViewModel main)
    {
        _main = main;
        _mode = Modes[0];
        _sort = SortModes[0];
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsDark) && IsOpen)
                RebuildDisplay();
        };
    }

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private IReadOnlyList<DriveOption> _drives = [];
    [ObservableProperty] private DriveOption? _selectedDrive;
    [ObservableProperty] private bool _hasDrives;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private IReadOnlyList<DiskUsageSlice> _slices = [];
    [ObservableProperty] private IReadOnlyList<DiskUsageRow> _rows = [];
    [ObservableProperty] private DiskUsageCategory? _highlightedCategory;

    [ObservableProperty] private string _centerPrimary = "";
    [ObservableProperty] private string _centerTitle = "";
    [ObservableProperty] private string _centerDetail = "";

    public IReadOnlyList<Choice> Modes { get; } =
    [
        new("whole", Strings.DiskModeWhole),
        new("used", Strings.DiskModeUsed),
    ];

    public IReadOnlyList<Choice> SortModes { get; } =
    [
        new("size", Strings.DiskSortSize),
        new("count", Strings.DiskSortCount),
        new("name", Strings.DiskSortName),
    ];

    [ObservableProperty] private Choice? _mode;
    [ObservableProperty] private Choice? _sort;

    private bool IncludeFree => Mode?.Value != "used";

    private DiskUsageSort CurrentSort => Sort?.Value switch
    {
        "count" => DiskUsageSort.Count,
        "name" => DiskUsageSort.Name,
        _ => DiskUsageSort.Size,
    };

    /// <summary>Opens the overlay and starts scanning <paramref name="drivePath"/> (or the active tab's drive).</summary>
    public void Open(string? drivePath = null)
    {
        CancelScan();
        IsOpen = true;
        _snapshot = null;
        _wasCancelled = false;
        HighlightedCategory = null;
        Slices = [];
        Rows = [];
        CenterPrimary = "";
        CenterTitle = "";
        CenterDetail = "";
        StatusText = "";

        var preferredRoot = ResolveRoot(drivePath) ?? ResolveRoot(_main.ActivePane.SelectedTab.Path);
        _ = LoadDrivesAndStartAsync(preferredRoot);
    }

    [RelayCommand]
    private void Close()
    {
        CancelScan();
        IsOpen = false;
    }

    [RelayCommand]
    private void Stop()
    {
        if (!IsScanning)
            return;
        _wasCancelled = true;
        CancelScan();
        IsScanning = false;
        UpdateStatus();
    }

    [RelayCommand]
    private void Rescan()
    {
        if (SelectedDrive is { } drive)
            StartScan(drive.Drive);
    }

    public void SetHoveredCategory(DiskUsageCategory? category)
    {
        if (HighlightedCategory != category)
            HighlightedCategory = category;
    }

    partial void OnSelectedDriveChanged(DriveOption? value)
    {
        if (_switchingDrive || !IsOpen)
            return;
        if (value is not null)
            StartScan(value.Drive);
    }

    partial void OnModeChanged(Choice? value)
    {
        if (value is not null)
            RebuildDisplay();
    }

    partial void OnSortChanged(Choice? value)
    {
        if (value is not null)
            RebuildDisplay();
    }

    partial void OnHighlightedCategoryChanged(DiskUsageCategory? value)
    {
        foreach (var row in Rows)
            row.IsHighlighted = value == row.Category;
        UpdateCenterText();
    }

    private async Task LoadDrivesAndStartAsync(string? preferredRoot)
    {
        var facts = await Task.Run(() => DriveReader.Read(DriveReader.Patience));
        if (!IsOpen)
            return;

        var ready = facts
            .Where(d => d.IsReady && d.TotalSize > 0)
            .Select(d =>
            {
                var name = string.IsNullOrWhiteSpace(d.Label) ? Strings.DriveLocal : d.Label;
                return new DriveOption(d, $"{d.Letter} {name}");
            })
            .ToList();

        _switchingDrive = true;
        try
        {
            Drives = ready;
            HasDrives = ready.Count > 0;
            if (ready.Count == 0)
            {
                SelectedDrive = null;
                StatusText = Strings.DiskNoDrives;
                return;
            }

            SelectedDrive = ready.FirstOrDefault(d =>
                preferredRoot is not null && string.Equals(d.Path, preferredRoot, StringComparison.OrdinalIgnoreCase))
                ?? ready[0];
        }
        finally
        {
            _switchingDrive = false;
        }

        if (SelectedDrive is { } target)
            StartScan(target.Drive);
    }

    private void StartScan(DriveFacts drive)
    {
        CancelScan();
        _wasCancelled = false;
        _snapshot = null;
        HighlightedCategory = null;
        IsScanning = true;

        var cts = new CancellationTokenSource();
        _scanCts = cts;
        var token = cts.Token;

        _ = Task.Run(() =>
        {
            try
            {
                // Re-read fresh total/free right before scanning so numbers are up to date.
                var fresh = RefreshDrive(drive);
                DiskUsageScanner.ScanDrive(fresh, snap =>
                {
                    if (token.IsCancellationRequested)
                        return;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!IsOpen || _scanCts != cts)
                            return;
                        _snapshot = snap;
                        if (snap.IsComplete)
                            IsScanning = false;
                        RebuildDisplay();
                    });
                }, token);
            }
            catch (OperationCanceledException)
            {
                // User stopped, closed, or switched drives.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Disk usage scan failed for {drive.Name}", ex);
                Dispatcher.UIThread.Post(() =>
                {
                    if (_scanCts == cts)
                    {
                        IsScanning = false;
                        StatusText = ex.Message;
                    }
                });
            }
        }, token);
    }

    private static DriveFacts RefreshDrive(DriveFacts fallback)
    {
        try
        {
            var info = new DriveInfo(fallback.Name);
            if (info.IsReady)
                return fallback with { TotalSize = info.TotalSize, FreeSpace = info.AvailableFreeSpace };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return fallback;
    }

    private void CancelScan()
    {
        if (_scanCts is not { } cts)
            return;
        _scanCts = null;
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        cts.Dispose();
    }

    private void RebuildDisplay()
    {
        if (_snapshot is not { } snap)
            return;

        var dark = _main.IsDark;
        var buckets = IncludeFree
            ? snap.Buckets
            : [.. snap.Buckets.Where(b => b.Category != DiskUsageCategory.Free)];

        var denominator = IncludeFree
            ? snap.AccountedTotalBytes
            : Math.Max(snap.UsedBytes, snap.ScannedBytes);

        var sorted = DiskUsageScanner.Sort(buckets, CurrentSort);
        var slices = new List<DiskUsageSlice>(sorted.Count);
        var rows = new List<DiskUsageRow>(sorted.Count);

        foreach (var b in sorted)
        {
            // Keep Free and all 5 main file categories visible; show System only when > 0.
            if (b.Category == DiskUsageCategory.System && b.Bytes <= 0)
                continue;

            var fraction = denominator > 0 ? (double)b.Bytes / denominator : 0;
            var brush = BrushFor(b.Category, dark);
            var label = LabelFor(b.Category, snap.IsComplete);
            var showCount = b.Category is not (DiskUsageCategory.Free or DiskUsageCategory.System);

            if (fraction > 0)
                slices.Add(new(b.Category, fraction, brush));

            var row = new DiskUsageRow(b.Category, label, brush, b.Bytes, b.FileCount, fraction, showCount)
            {
                IsHighlighted = HighlightedCategory == b.Category,
            };
            rows.Add(row);
        }

        // Chart arcs read best ordered by size (largest first) starting at 12 o'clock,
        // while keeping Free space anchored first when visible so the green arc stays steady.
        Slices = [.. slices
            .OrderBy(s => s.Category == DiskUsageCategory.Free ? 0 : s.Category == DiskUsageCategory.System ? 2 : 1)
            .ThenByDescending(s => s.Fraction)];
        Rows = rows;
        UpdateCenterText();
        UpdateStatus();
    }

    private void UpdateCenterText()
    {
        if (_snapshot is not { } snap)
            return;

        if (HighlightedCategory is { } hot && Rows.FirstOrDefault(r => r.Category == hot) is { } row)
        {
            CenterPrimary = row.SizeText;
            CenterTitle = row.Label;
            CenterDetail = row.HasCount ? $"{row.PercentText} · {row.CountText}" : row.PercentText;
            return;
        }

        var usedPct = snap.TotalBytes > 0 ? (double)snap.UsedBytes / snap.TotalBytes : 0;
        CenterPrimary = IncludeFree
            ? DiskUsageRow.FormatPercent(usedPct)
            : SizeFormatter.Format(snap.UsedBytes);
        CenterTitle = Strings.DiskUsedOf(SizeFormatter.Format(snap.UsedBytes), SizeFormatter.Format(snap.TotalBytes));
        CenterDetail = Strings.DiskHoverHint;
    }

    private void UpdateStatus()
    {
        if (_snapshot is not { } snap)
            return;

        if (IsScanning)
        {
            StatusText = Strings.DiskScanning(snap.ScannedFiles, SizeFormatter.Format(snap.ScannedBytes));
            return;
        }

        var main = _wasCancelled
            ? Strings.DiskScanStopped(snap.ScannedFiles)
            : Strings.DiskScanDone(snap.ScannedFiles, snap.ScannedFolders);

        StatusText = snap.SkippedEntries > 0
            ? $"{main} · {Strings.DiskSkipped(snap.SkippedEntries)}"
            : main;
    }

    private static string LabelFor(DiskUsageCategory category, bool isComplete) =>
        category == DiskUsageCategory.System && !isComplete
            ? Strings.DiskCatScanning
            : DiskUsageClassifier.Label(category);

    private static IBrush BrushFor(DiskUsageCategory category, bool dark) => new SolidColorBrush(Color.Parse(category switch
    {
        DiskUsageCategory.Free => dark ? "#3DD68C" : "#16A34A",
        DiskUsageCategory.Apps => dark ? "#F4C430" : "#D97706",
        DiskUsageCategory.Media => dark ? "#A78BFA" : "#7C3AED",
        DiskUsageCategory.Archives => dark ? "#FB923C" : "#EA580C",
        DiskUsageCategory.Documents => dark ? "#38BDF8" : "#0284C7",
        DiskUsageCategory.Other => dark ? "#94A3B8" : "#64748B",
        DiskUsageCategory.System => dark ? "#3F3F46" : "#CBD5E1",
        _ => dark ? "#94A3B8" : "#64748B",
    }));

    private static string? ResolveRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Locations.IsVirtual(path))
            return null;
        try
        {
            return Path.GetPathRoot(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
