using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rift.Core;

/// <summary>What the user chose in Settings. Saved to %AppData%\Rift\settings.json.</summary>
public sealed class SettingsState
{
    /// <summary>"dark", "light" or "system".</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>One of <see cref="Accents.All"/> by id.</summary>
    public string Accent { get; set; } = "azure";

    /// <summary>"system", or a culture Rift is translated to ("en", "ru").</summary>
    public string Language { get; set; } = "system";

    /// <summary>"solid", "mica" or "acrylic" (Windows 11; falls back to solid elsewhere).</summary>
    public string Backdrop { get; set; } = "solid";

    /// <summary>Null: follow Explorer's own setting.</summary>
    public bool? ShowHidden { get; set; }

    /// <summary>Each folder opens with the view and sort it had last time.</summary>
    public bool RememberFolderViews { get; set; } = true;

    /// <summary>The crash report the user already saw the notice for; older ones don't bring it back.</summary>
    public DateTime? CrashNoticeSeen { get; set; }

    /// <summary>The installed Rift looks for a new version on GitHub after start and daily, and downloads it.</summary>
    public bool CheckForUpdates { get; set; } = true;

    public ColumnsState Columns { get; set; } = new();

    /// <summary>Command id → gesture, only where the user changed the default. An empty gesture means "no key".</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = [];

    public List<TagDefinition> Tags { get; set; } = [];
}

public sealed class ColumnsState
{
    public bool Modified { get; set; } = true;
    public bool Created { get; set; }
    public bool Type { get; set; } = true;
    public bool Size { get; set; } = true;
    public double ModifiedWidth { get; set; } = 130;
    public double CreatedWidth { get; set; } = 130;
    public double TypeWidth { get; set; } = 110;
    public double SizeWidth { get; set; } = 80;
}

public sealed record TagDefinition(string Id, string Name, string Color);

/// <summary>Accent colors; each has a shade for the dark theme and one for the light theme, so contrast stays equal.</summary>
public sealed record Accent(string Id, string Dark, string Light);

public static class Accents
{
    public static IReadOnlyList<Accent> All { get; } =
    [
        new("azure", "#4C8DFF", "#2563EB"),
        new("ember", "#FF8A3D", "#E8590C"),
        new("mint", "#34D3A0", "#0E9F6E"),
        new("iris", "#9B87FF", "#6D4AFF"),
        new("rose", "#FF5C8A", "#E11D5C"),
    ];

    public static Accent Find(string? id) => All.FirstOrDefault(a => a.Id == id) ?? All[0];

    /// <summary>Colors offered for new tags, in order.</summary>
    public static IReadOnlyList<string> TagColors { get; } = ["#F06A6A", "#6FD39B", "#5EB6F0", "#F2B84B", "#B48CF2", "#F28CC4", "#8E9BAD"];
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SettingsState))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(Dictionary<string, FolderView>))]
internal sealed partial class SettingsJson : JsonSerializerContext;

public static class SettingsStore
{
    public static string File => Path.Combine(SessionStore.Folder, "settings.json");

    /// <summary>The saved settings, or defaults when there are none or the file is damaged.</summary>
    public static SettingsState Load(string? file = null) =>
        JsonFile.Read(file ?? File, SettingsJson.Default.SettingsState) ?? new SettingsState();

    public static void Save(SettingsState state, string? file = null) =>
        JsonFile.Write(file ?? File, state, SettingsJson.Default.SettingsState);
}

/// <summary>Small JSON files that must never take the app down: read failures give null, writes swap atomically.</summary>
internal static class JsonFile
{
    public static T? Read<T>(string file, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) where T : class
    {
        try
        {
            return System.IO.File.Exists(file) ? JsonSerializer.Deserialize(System.IO.File.ReadAllText(file), type) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public static void Write<T>(string file, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp";
        System.IO.File.WriteAllText(temp, JsonSerializer.Serialize(value, type));
        System.IO.File.Move(temp, file, overwrite: true);
    }
}
