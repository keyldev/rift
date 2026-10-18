using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rift.Core;
using Rift.Core.Localization;
using Rift.Core.Operations;
using Rift.Shell;

namespace Rift.App.ViewModels;

/// <summary>File operations: copy/move between panes, delete, rename, new folder, clipboard, undo.</summary>
public partial class MainWindowViewModel
{
    private readonly Stack<(string Title, OperationJournal Journal)> _undo = new();
    private readonly Queue<DialogViewModel> _pendingDialogs = new();

    public OperationsViewModel Operations { get; } = new();

    /// <summary>The dialog currently shown over the window, if any.</summary>
    [ObservableProperty] private DialogViewModel? _dialog;

    /// <summary>Native window handle for clipboard and shell calls; set by the window.</summary>
    public Func<nint> WindowHandle { get; set; } = () => 0;

    /// <summary>The view moves keyboard focus back to the file list (after a dialog closes).</summary>
    public event Action? FocusFilesRequested;

    private TabViewModel ActiveTab => ActivePane.SelectedTab;
    private IEnumerable<TabViewModel> AllTabs => Panes.SelectMany(p => p.Tabs);

    // ---------- dialogs ----------

    public async Task<DialogResult> ShowDialogAsync(DialogViewModel dialog)
    {
        if (Dialog is null)
            Dialog = dialog;
        else
            _pendingDialogs.Enqueue(dialog);

        var result = await dialog.Result;
        Dialog = _pendingDialogs.Count > 0 ? _pendingDialogs.Dequeue() : null;
        if (Dialog is null)
            FocusFilesRequested?.Invoke();
        return result;
    }

    private Task ShowErrorAsync(string title, string message) =>
        ShowDialogAsync(DialogViewModel.Error(title, message, Strings.CommonOk));

    // ---------- copy / move ----------

    // Operations run side by side, so the command stays enabled while one is in progress.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task CopyToOther() => TransferToOtherAsync(TransferMode.Copy);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task MoveToOther() => TransferToOtherAsync(TransferMode.Move);

    private Task TransferToOtherAsync(TransferMode mode)
    {
        var sources = ActiveTab.SelectedPaths;
        if (!CanTransfer || OtherPane is not { } other || sources.Count == 0 || !ActiveTab.CanEditItems || !other.SelectedTab.IsFileSystem)
            return Task.CompletedTask;
        return TransferAsync(mode, sources, other.SelectedTab.Path);
    }

    /// <summary>Runs a copy or move as a card in the operations panel; used by F5/F6, paste and drag-n-drop.</summary>
    public async Task TransferAsync(TransferMode mode, IReadOnlyList<string> sources, string destination)
    {
        var verb = mode == TransferMode.Copy ? Strings.OpCopy : Strings.OpMove;
        var title = Strings.OpTitle(verb, Describe(sources), PathCrumbs.Title(destination));
        var op = Operations.Start(title);

        var transfer = new FileTransfer(mode, destination, new DialogConflictResolver(this),
            new Progress<TransferProgress>(op.Update), op.PauseToken, op.Token);
        var result = await transfer.RunAsync(sources);

        op.Complete(result.Canceled, result.Errors);
        FollowMoves(result.Journal);
        Remember(title, result.Journal);
        await RefreshAsync(sources.Select(Path.GetDirectoryName).Append(destination), destination, result.Targets);
    }

    // ---------- delete ----------

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task Delete() => DeleteAsync(permanent: false);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task DeletePermanently() => DeleteAsync(permanent: true);

    private async Task DeleteAsync(bool permanent)
    {
        var tab = ActiveTab;
        if (tab.IsRecycleBin)
        {
            await DeleteFromBinAsync(tab.SelectedRecycled);
            return;
        }
        var paths = tab.SelectedPaths;
        if (paths.Count == 0 || !tab.CanEditItems)
            return;
        if (!OperatingSystem.IsWindows())
            permanent = true;

        // Like Explorer: the Recycle Bin needs no confirmation (it can be restored), permanent delete does.
        if (permanent)
        {
            var confirm = await ShowDialogAsync(new DialogViewModel
            {
                Title = Strings.DeletePermanentTitle,
                Message = Describe(paths),
                Details = [Strings.DeletePermanentWarning],
                Buttons =
                [
                    new DialogButton(Strings.DeletePermanent, 1, IsPrimary: true, IsDanger: true),
                    new DialogButton(Strings.CommonCancel, 0),
                ],
            });
            if (confirm.Button != 1)
                return;
        }

        var op = Operations.Start($"{(permanent ? Strings.OpDelete : Strings.OpRecycle)} · {Describe(paths)}", canPause: false);
        try
        {
            if (permanent)
            {
                var errors = await FileOps.DeletePermanentlyAsync(paths, new Progress<TransferProgress>(op.Update), op.Token);
                op.Complete(canceled: false, errors);
            }
            else if (OperatingSystem.IsWindows())
            {
                // The bin's clock and ours can disagree by a moment; FindRecent matches by path too.
                var since = DateTime.Now.AddSeconds(-5);
                var completed = await RecycleBin.SendAsync(paths, WindowHandle());
                op.Complete(canceled: !completed, []);
                RememberRecycled(op.Title, paths, since);
            }
        }
        catch (OperationCanceledException)
        {
            op.Complete(canceled: true, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            op.Complete(canceled: false, [new OperationError(paths[0], ex.Message)]);
        }

        await RefreshAsync(paths.Select(Path.GetDirectoryName).Append(tab.Path).Append(Locations.RecycleBin));
    }

    /// <summary>Ctrl+Z after Del: find what just went to the bin, so undo can bring it back.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void RememberRecycled(string title, IReadOnlyList<string> paths, DateTime since)
    {
        try
        {
            var journal = new OperationJournal();
            foreach (var item in RecycleBinStore.FindRecent(RecycleBin.Folders(), paths, since))
                journal.AddRecycled(item);
            Remember(title, journal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Undo just won't cover this delete.
        }
    }

    // ---------- Recycle Bin ----------

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Restore()
    {
        var items = ActiveTab.SelectedRecycled;
        if (items.Count == 0)
            return;

        var op = Operations.Start($"{Strings.OpRestore} · {Describe(items.Select(i => i.OriginalPath).ToList())}", canPause: false);
        var errors = new List<OperationError>();
        var restored = await Task.Run(() =>
        {
            var targets = new List<string>();
            foreach (var item in items)
            {
                try
                {
                    targets.Add(RecycleBinStore.Restore(item));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add(new OperationError(item.OriginalPath, ex.Message));
                }
            }
            return targets;
        });
        op.Complete(canceled: false, errors);
        if (OperatingSystem.IsWindows())
            RecycleBin.NotifyChanged();
        await RefreshAsync(restored.Select(Path.GetDirectoryName).Append(Locations.RecycleBin));
    }

    private async Task DeleteFromBinAsync(IReadOnlyList<RecycledItem> items)
    {
        if (items.Count == 0)
            return;
        var names = items.Select(i => i.OriginalPath).ToList();
        var confirm = await ShowDialogAsync(new DialogViewModel
        {
            Title = Strings.DeletePermanentTitle,
            Message = Describe(names),
            Details = [Strings.DeletePermanentWarning],
            Buttons =
            [
                new DialogButton(Strings.DeletePermanent, 1, IsPrimary: true, IsDanger: true),
                new DialogButton(Strings.CommonCancel, 0),
            ],
        });
        if (confirm.Button != 1)
            return;

        var op = Operations.Start($"{Strings.OpDelete} · {Describe(names)}", canPause: false);
        var errors = new List<OperationError>();
        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                try
                {
                    RecycleBinStore.Delete(item);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add(new OperationError(item.OriginalPath, ex.Message));
                }
            }
        });
        op.Complete(canceled: false, errors);
        if (OperatingSystem.IsWindows())
            RecycleBin.NotifyChanged();
        await RefreshAsync([Locations.RecycleBin]);
    }

    [RelayCommand]
    private async Task EmptyBin()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var count = RecycleBinStore.Read(RecycleBin.Folders()).Count;
        if (count == 0)
            return;
        var confirm = await ShowDialogAsync(new DialogViewModel
        {
            Title = Strings.EmptyBinTitle,
            Message = Strings.EmptyBinMessage(Strings.ItemsCount(count)),
            Buttons =
            [
                new DialogButton(Strings.MenuEmptyBin, 1, IsPrimary: true, IsDanger: true),
                new DialogButton(Strings.CommonCancel, 0),
            ],
        });
        if (confirm.Button != 1)
            return;
        RecycleBin.Empty(WindowHandle());
        await RefreshAsync([Locations.RecycleBin]);
    }

    // ---------- rename / new folder ----------

    [RelayCommand]
    private void Rename() => ActiveTab.BeginRename();

    public async Task CommitRenameAsync(TabViewModel tab, FileItem item)
    {
        if (!item.IsRenaming)
            return;
        var newName = item.EditName;
        tab.CancelRename();
        if (newName == item.Name)
            return;

        try
        {
            var (path, journal) = FileOps.Rename(item.FullPath, newName);
            FollowMoves(journal);
            Remember($"{item.Name} → {Path.GetFileName(path)}", journal);
            if (tab.IsSearch)
                tab.ReplaceEntry(item.FullPath, path);
            await RefreshAsync([Path.GetDirectoryName(item.FullPath)], tab.Path, [path]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await ShowErrorAsync(Strings.RenameFailedTitle, ex.Message);
        }
    }

    [RelayCommand]
    private async Task NewFolder()
    {
        var tab = ActiveTab;
        if (!tab.IsFileSystem)
            return;
        try
        {
            var (path, journal) = FileOps.CreateFolder(tab.Path, Strings.NewFolderName);
            Remember(Strings.NewFolderName, journal);
            await tab.RefreshAsync([path]);
            tab.BeginRename();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(Strings.NewFolderFailedTitle, ex.Message);
        }
    }

    // ---------- clipboard ----------

    [RelayCommand]
    private void CopyToClipboard() => SetClipboard(cut: false);

    [RelayCommand]
    private void CutToClipboard() => SetClipboard(cut: true);

    private void SetClipboard(bool cut)
    {
        var paths = ActiveTab.SelectedPaths;
        if (OperatingSystem.IsWindows() && paths.Count > 0 && ActiveTab.CanEditItems)
            FileClipboard.SetFiles(WindowHandle(), paths, cut);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Paste()
    {
        if (!OperatingSystem.IsWindows() || !ActiveTab.IsFileSystem || FileClipboard.GetFiles(WindowHandle()) is not { } clip)
            return;

        await TransferAsync(clip.Cut ? TransferMode.Move : TransferMode.Copy, clip.Paths, ActiveTab.Path);
        // A cut is consumed by the paste, as in Explorer.
        if (clip.Cut)
            FileClipboard.Clear(WindowHandle());
    }

    // ---------- undo ----------

    private void Remember(string title, OperationJournal journal)
    {
        if (!journal.IsEmpty)
            _undo.Push((title, journal));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Undo()
    {
        if (!_undo.TryPop(out var entry))
            return;

        var op = Operations.Start(Strings.OpUndoTitle(entry.Title), canPause: false);
        try
        {
            var moves = entry.Journal; // read before undo clears it
            FollowMovesBack(moves);
            var errors = await entry.Journal.UndoAsync(op.Token);
            op.Complete(canceled: false, errors);
        }
        catch (OperationCanceledException)
        {
            op.Complete(canceled: true, []);
        }

        if (OperatingSystem.IsWindows())
            RecycleBin.NotifyChanged();
        await Task.WhenAll(AllTabs.Select(t => t.RefreshAsync()));
    }

    private static void FollowMovesBack(OperationJournal journal) => FollowMoves(journal, undo: true);

    // ---------- helpers ----------

    /// <summary>Reloads every tab showing one of <paramref name="folders"/>; selects <paramref name="select"/> in <paramref name="selectIn"/>.</summary>
    private Task RefreshAsync(IEnumerable<string?> folders, string? selectIn = null, IReadOnlyList<string>? select = null)
    {
        var set = folders.OfType<string>().Select(NormalizeDir).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Search tabs can hold files from anywhere; their refresh only rechecks the hits, it doesn't search again.
        var refreshes = AllTabs
            .Where(t => t.IsSearch || set.Contains(NormalizeDir(t.Path)))
            .Select(t => selectIn is not null && select is { Count: > 0 } &&
                         string.Equals(NormalizeDir(t.Path), NormalizeDir(selectIn), StringComparison.OrdinalIgnoreCase)
                ? t.RefreshAsync(select)
                : t.RefreshAsync());
        return Task.WhenAll(refreshes);
    }

    private static string NormalizeDir(string path)
    {
        if (Locations.IsVirtual(path))
            return path;
        var full = Path.GetFullPath(path);
        return full.Length > 3 ? full.TrimEnd('\\') : full;
    }

    private static string Describe(IReadOnlyList<string> paths) =>
        paths.Count == 1 ? Strings.DeleteOne(Path.GetFileName(paths[0])) : Strings.ItemsCount(paths.Count);

    /// <summary>Asks the user through the conflict dialog; called from the transfer's background thread.</summary>
    private sealed class DialogConflictResolver(MainWindowViewModel owner) : IConflictResolver
    {
        public async Task<ConflictDecision> ResolveAsync(FileConflict c, CancellationToken ct)
        {
            var dialog = new DialogViewModel
            {
                Title = Strings.ConflictTitle,
                Message = Strings.ConflictMessage(Path.GetFileName(c.DestinationPath),
                    PathCrumbs.Title(Path.GetDirectoryName(c.DestinationPath)!)),
                Details =
                [
                    Strings.ConflictIncoming(SizeFormatter.Format(c.SourceSize), c.SourceModified.ToString("g")),
                    Strings.ConflictExisting(SizeFormatter.Format(c.DestinationSize), c.DestinationModified.ToString("g")),
                ],
                Buttons =
                [
                    new DialogButton(Strings.ConflictReplace, (int)ConflictAction.Replace, IsPrimary: true),
                    new DialogButton(Strings.ConflictKeepBoth, (int)ConflictAction.KeepBoth),
                    new DialogButton(Strings.ConflictSkip, (int)ConflictAction.Skip),
                    new DialogButton(Strings.CommonCancel, (int)ConflictAction.Cancel),
                ],
                CheckBoxText = Strings.ConflictApplyToAll,
            };
            var result = await Dispatcher.UIThread.InvokeAsync(() => owner.ShowDialogAsync(dialog));
            return new ConflictDecision((ConflictAction)result.Button, result.IsChecked);
        }
    }
}
