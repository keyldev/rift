using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Rift.Core;
using Rift.Core.Localization;

namespace Rift.App.ViewModels;

/// <summary>Saving the layout and tabs: the last session (restored at start) and named workspaces.</summary>
public partial class MainWindowViewModel
{
    /// <summary>The window's position, filled in by the window when it closes.</summary>
    public Func<WindowPlacement?> Placement { get; set; } = () => null;

    public SessionState ToSession() => new()
    {
        Layout = ToState(Layout),
        ActivePane = Panes.ToList().IndexOf(ActivePane),
        Zoomed = IsZoomed,
        Dark = IsDark,
        Window = Placement(),
    };

    private static LayoutState ToState(LayoutNode<PaneViewModel> node) => node switch
    {
        LayoutLeaf<PaneViewModel> leaf => new LayoutState { Pane = leaf.Value.ToState() },
        LayoutSplit<PaneViewModel> split => new LayoutState
        {
            Direction = split.Direction,
            Ratio = split.Ratio,
            First = ToState(split.First),
            Second = ToState(split.Second),
        },
        _ => throw new UnreachableException(),
    };

    private LayoutNode<PaneViewModel> FromState(LayoutState state) => state.Pane is { } pane
        ? new LayoutLeaf<PaneViewModel>(Track(PaneViewModel.FromState(pane)))
        : new LayoutSplit<PaneViewModel>(state.Direction, FromState(state.First!), FromState(state.Second!), state.Ratio);

    /// <summary>Replaces the whole layout with a saved one (start-up, or opening a workspace).</summary>
    private void ApplySession(SessionState session)
    {
        var old = _layout is null ? [] : Panes;
        _layout = new LayoutTree<PaneViewModel>(FromState(session.Layout));
        foreach (var tab in old.SelectMany(p => p.Tabs))
            tab.Dispose();

        var panes = Panes;
        _recent.Clear();
        var active = panes[Math.Clamp(session.ActivePane, 0, panes.Count - 1)];
        _recent.AddRange(panes.OrderBy(p => p == active ? 0 : 1));
        foreach (var pane in panes)
            pane.IsActive = false;
        ActivePane = active;
        active.IsActive = true;
        IsZoomed = session.Zoomed && panes.Count > 1;
        OnPropertyChanged(nameof(HasManyPanes));
        LayoutChanged?.Invoke();
    }

    /// <summary>Called as the window closes; a failure only costs the next start its restore.</summary>
    public void SaveSession()
    {
        try
        {
            SessionStore.Save(SessionStore.LastSessionFile, ToSession());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Saving the session failed", ex);
        }
    }

    // ---------- workspaces ----------

    [RelayCommand]
    private async Task SaveWorkspace()
    {
        var result = await ShowDialogAsync(new DialogViewModel
        {
            Title = Strings.WorkspaceSaveTitle,
            Message = Strings.WorkspaceSaveMessage,
            Input = "",
            Buttons =
            [
                new DialogButton(Strings.WorkspaceSave, 1, IsPrimary: true),
                new DialogButton(Strings.CommonCancel, 0),
            ],
        });
        var name = result.Input?.Trim();
        if (result.Button != 1 || string.IsNullOrEmpty(name))
            return;
        try
        {
            var state = ToSession();
            state.Window = null; // a workspace opens in whatever window you have
            SessionStore.Save(SessionStore.WorkspaceFile(name), state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await ShowErrorAsync(Strings.ErrorTitle, ex.Message);
        }
    }

    public void OpenWorkspace(string name)
    {
        if (SessionStore.Load(SessionStore.WorkspaceFile(name)) is not { } state)
            return;
        ApplySession(state);
        FocusFilesRequested?.Invoke();
    }

    public void DeleteWorkspace(string name)
    {
        try
        {
            File.Delete(SessionStore.WorkspaceFile(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Deleting workspace {name} failed", ex);
        }
    }
}
