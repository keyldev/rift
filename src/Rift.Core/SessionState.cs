using System.Text.Json;
using Rift.Core.Operations;
using System.Text.Json.Serialization;

namespace Rift.Core;

/// <summary>Everything needed to bring a window back as it was: layout, tabs, their views, the window itself.</summary>
public sealed class SessionState
{
    public LayoutState Layout { get; set; } = new();
    /// <summary>Index of the active pane in reading order.</summary>
    public int ActivePane { get; set; }
    public bool Zoomed { get; set; }
    public bool Dark { get; set; } = true;
    /// <summary>Not part of a workspace: a workspace opens in whatever window you have.</summary>
    public WindowPlacement? Window { get; set; }
}

/// <summary>A layout node: either <see cref="Pane"/>, or a split with <see cref="First"/> and <see cref="Second"/>.</summary>
public sealed class LayoutState
{
    public PaneState? Pane { get; set; }
    public SplitDirection Direction { get; set; }
    public double Ratio { get; set; } = 0.5;
    public LayoutState? First { get; set; }
    public LayoutState? Second { get; set; }
}

public sealed class PaneState
{
    public List<TabState> Tabs { get; set; } = [];
    public int Selected { get; set; }
    public bool Preview { get; set; }
    public double PreviewWidth { get; set; } = 320;
}

public sealed record TabState
{
    public string Path { get; set; } = "";
    /// <summary>View mode name (Details, Tiles, Thumbnails); a string so the UI's enum can evolve.</summary>
    public string View { get; set; } = "Details";
    public SortColumn Sort { get; set; }
    public bool Descending { get; set; }
    public bool Pinned { get; set; }
}

public sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SessionState))]
internal sealed partial class SessionJson : JsonSerializerContext;

/// <summary>
/// Reads and writes sessions: the last one (session.json, restored at start) and named workspaces
/// (%AppData%\Rift\workspaces\*.json). A damaged file is ignored, never fatal.
/// </summary>
public static class SessionStore
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rift");
    public static string LastSessionFile => Path.Combine(Folder, "session.json");
    public static string WorkspacesFolder => Path.Combine(Folder, "workspaces");

    public static SessionState? Load(string file)
    {
        try
        {
            if (!File.Exists(file))
                return null;
            var state = JsonSerializer.Deserialize(File.ReadAllText(file), SessionJson.Default.SessionState);
            return state is not null && Clean(state.Layout) ? state : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public static void Save(string file, SessionState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        // Write next to it and swap, so a crash mid-write never leaves half a session.
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, SessionJson.Default.SessionState));
        File.Move(temp, file, overwrite: true);
    }

    public static string WorkspaceFile(string name) => Path.Combine(WorkspacesFolder, FileNames.Clean(Sanitize(name)) + ".json");

    public static IReadOnlyList<string> Workspaces()
    {
        try
        {
            return Directory.Exists(WorkspacesFolder)
                ? Directory.EnumerateFiles(WorkspacesFolder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()
                    .Order(NaturalStringComparer.Instance).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

    /// <summary>Drops empty tabs and panes; false if nothing usable is left.</summary>
    internal static bool Clean(LayoutState node)
    {
        if (node.Pane is { } pane)
        {
            pane.Tabs.RemoveAll(t => string.IsNullOrWhiteSpace(t.Path));
            pane.Selected = Math.Clamp(pane.Selected, 0, Math.Max(0, pane.Tabs.Count - 1));
            return pane.Tabs.Count > 0;
        }
        var first = node.First is not null && Clean(node.First);
        var second = node.Second is not null && Clean(node.Second);
        if (first && second)
            return true;
        // One side is broken: this node becomes the other side.
        var keep = first ? node.First : second ? node.Second : null;
        if (keep is null)
            return false;
        (node.Pane, node.Direction, node.Ratio, node.First, node.Second) = (keep.Pane, keep.Direction, keep.Ratio, keep.First, keep.Second);
        return true;
    }
}
