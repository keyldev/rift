using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Rift.Core;

namespace Rift.App.ViewModels;

public enum PaletteKind
{
    Command,
    Place,
    Recent,
    Tab,
    GoTo,
}

/// <param name="Detail">Shown dimmed after the title: a path, or where the place comes from.</param>
/// <param name="Gesture">Keyboard shortcut shown on the right, for commands.</param>
public sealed record PaletteItem(string Title, string? Detail, string Icon, PaletteKind Kind, Func<Task> Run, string? Gesture = null)
{
    public string KindText => Kind switch
    {
        PaletteKind.Command => Rift.Core.Localization.Strings.PaletteKindCommand,
        PaletteKind.Recent => Rift.Core.Localization.Strings.PaletteKindRecent,
        PaletteKind.Tab => Rift.Core.Localization.Strings.PaletteKindTab,
        _ => Rift.Core.Localization.Strings.PaletteKindPlace,
    };

    public bool HasGesture => Gesture is not null;
}

/// <summary>Ctrl+K: one box for commands, folders, recent places, open tabs and typed paths.</summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private const int MaxResults = 50;
    /// <summary>How long a keystroke waits for the typed path to be checked before the list shows without it.</summary>
    private static readonly TimeSpan QuickLookup = TimeSpan.FromMilliseconds(30);

    private readonly Func<IReadOnlyList<PaletteItem>> _source;
    private readonly Func<string, PaletteItem?> _goTo;
    private IReadOnlyList<PaletteItem> _items = [];
    /// <summary>The check of the typed path that's still on its way (a share that's slow to answer).</summary>
    private Task<PaletteItem?>? _pendingGoTo;

    /// <param name="source">Everything the palette can offer right now (rebuilt on every open).</param>
    /// <param name="goTo">An extra first item when the query is a path that exists. Runs off the UI thread: it touches the disk.</param>
    public CommandPaletteViewModel(Func<IReadOnlyList<PaletteItem>> source, Func<string, PaletteItem?> goTo)
    {
        _source = source;
        _goTo = goTo;
    }

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private int _selectedIndex;

    public ObservableCollection<PaletteItem> Results { get; } = [];

    public void Open()
    {
        _items = _source();
        Query = "";
        Update();
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    partial void OnQueryChanged(string value) => Update();

    public void Move(int delta)
    {
        if (Results.Count > 0)
            SelectedIndex = (SelectedIndex + delta + Results.Count) % Results.Count;
    }

    public async Task RunSelectedAsync()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count)
            return;
        var item = Results[SelectedIndex];
        // Enter on a typed path before the check came back: wait for it rather than run whatever is first.
        if (SelectedIndex == 0 && _pendingGoTo is { } pending && await pending is { } go)
            item = go;
        Close();
        await item.Run();
    }

    private void Update()
    {
        Results.Clear();

        IEnumerable<PaletteItem> ranked;
        if (Query.Trim().Length == 0)
        {
            // Nothing typed yet: recent places first, then everything in its natural order.
            ranked = _items.Where(i => i.Kind == PaletteKind.Recent).Concat(_items.Where(i => i.Kind != PaletteKind.Recent));
        }
        else
        {
            ranked = _items
                .Select(i => (Item: i, Score: Best(FuzzyMatcher.Score(Query, i.Title), FuzzyMatcher.Score(Query, i.Detail ?? "") - 500)))
                .Where(x => x.Score is not null)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Item);
        }

        foreach (var item in ranked.Take(MaxResults))
            Results.Add(item);
        SelectedIndex = Results.Count > 0 ? 0 : -1;
        LookUpPath();
    }

    /// <summary>
    /// Checks the typed text as a path. Directory.Exists on a share that's down waits for the network,
    /// so it runs in the background; a local path answers within a few ms and shows with the list.
    /// </summary>
    private void LookUpPath()
    {
        _pendingGoTo = null;
        if (Query.Length == 0)
            return;
        var query = Query;
        var lookup = Task.Run(() =>
        {
            try
            {
                return _goTo(query);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        });
        if (lookup.Wait(QuickLookup))
        {
            ShowGoTo(lookup.Result);
            return;
        }
        _pendingGoTo = lookup;
        lookup.ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (_pendingGoTo != lookup)
                return; // typed on since
            _pendingGoTo = null;
            ShowGoTo(t.Result);
        }), TaskScheduler.Default);
    }

    private void ShowGoTo(PaletteItem? go)
    {
        if (go is null)
            return;
        Results.Insert(0, go);
        // Keep the row the user moved to; otherwise the path becomes the first choice.
        SelectedIndex = SelectedIndex <= 0 ? 0 : SelectedIndex + 1;
    }

    private static int? Best(int? a, int? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}
