using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Rift.App.Services;
using Rift.Core;
using Rift.Core.Localization;
using Rift.Core.Operations;

namespace Rift.App.ViewModels;

/// <summary>A tag as the sidebar shows it: a colored dot and a name; a click lists everything tagged.</summary>
public sealed record SidebarTag(string Id, string Name, IBrush Brush)
{
    public string Path => Locations.Tag(Id, Name);
}

/// <summary>Tags: the sidebar list, tagging the selection, making a new tag.</summary>
public partial class MainWindowViewModel
{
    public ObservableCollection<SidebarTag> Tags { get; } = [];

    public bool HasTags => Tags.Count > 0;

    private void LoadTags()
    {
        Tags.Clear();
        foreach (var tag in AppSettings.Current.Tags)
            Tags.Add(new SidebarTag(tag.Id, tag.Name, new SolidColorBrush(Color.Parse(tag.Color))));
        OnPropertyChanged(nameof(HasTags));
    }

    [RelayCommand]
    private Task OpenTag(SidebarTag tag) => ActiveTab.NavigateTo(tag.Path);

    /// <summary>The selection's paths, for tagging; empty where rows aren't real files.</summary>
    public IReadOnlyList<string> TaggablePaths => ActiveTab.CanEditItems ? ActiveTab.SelectedPaths : [];

    public void ToggleTag(string tagId)
    {
        if (TaggablePaths is { Count: > 0 } paths)
            AppSettings.Tags.Toggle(paths, tagId);
    }

    /// <summary>Context menu → Tags → New tag…: asks for a name, makes the tag and puts it on the selection.</summary>
    [RelayCommand]
    private async Task NewTag()
    {
        var paths = TaggablePaths;
        var result = await ShowDialogAsync(new DialogViewModel
        {
            Title = Strings.TagNewTitle,
            Input = "",
            Buttons =
            [
                new DialogButton(Strings.AddTag, 1, IsPrimary: true),
                new DialogButton(Strings.CommonCancel, 0),
            ],
        });
        var name = result.Input?.Trim();
        if (result.Button != 1 || string.IsNullOrEmpty(name))
            return;
        var colors = Accents.TagColors;
        var tag = new TagDefinition(Guid.NewGuid().ToString("N")[..8], name, colors[AppSettings.Current.Tags.Count % colors.Count]);
        AppSettings.Update(s => s.Tags = [.. s.Tags, tag]);
        if (paths.Count > 0)
            AppSettings.Tags.Toggle(paths, tag.Id);
    }

    /// <summary>Files Rift moved or renamed keep their tags.</summary>
    private static void FollowMoves(OperationJournal journal, bool undo = false)
    {
        var moves = journal.Moves;
        if (undo)
        {
            foreach (var (from, to) in moves.Reverse())
                AppSettings.Tags.Move(to, from);
        }
        else
        {
            foreach (var (from, to) in moves)
                AppSettings.Tags.Move(from, to);
        }
    }
}
