using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Rift.App.ViewModels;
using Rift.Core;

namespace Rift.App.Services;

/// <summary>
/// The user's settings at runtime: loaded once at start, applied live (theme, accent, backdrop, columns, hidden files),
/// written back a moment after the last change.
/// </summary>
public static class AppSettings
{
    private static readonly Timer SaveTimer = new(_ => SaveNow());

    public static SettingsState Current { get; private set; } = new();

    /// <summary>Mica is a Windows 11 backdrop (build 22000 and later).</summary>
    public static bool MicaSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    public static TagStore Tags { get; private set; } = new(null);

    public static FolderViewStore FolderViews { get; private set; } = new(null);

    /// <summary>Settings changed (theme, accent, tags list, hotkeys…); the UI refreshes what depends on them.</summary>
    public static event Action? Changed;

    /// <summary>Reads settings and the stores. Call before the first window is built.</summary>
    public static void Load()
    {
        Current = SettingsStore.Load();
        Tags = new TagStore(TagStore.DefaultFile);
        FolderViews = new FolderViewStore(FolderViewStore.DefaultFile);

        var options = ViewOptions.Current;
        if (Current.ShowHidden is { } hidden)
            options.ShowHidden = hidden;
        options.PropertyChanged += OnViewOptionsChanged;

        var columns = options.Columns;
        var c = Current.Columns;
        (columns.ShowModified, columns.ShowCreated, columns.ShowType, columns.ShowSize) = (c.Modified, c.Created, c.Type, c.Size);
        (columns.ModifiedWidth, columns.CreatedWidth, columns.TypeWidth, columns.SizeWidth) = (c.ModifiedWidth, c.CreatedWidth, c.TypeWidth, c.SizeWidth);
        columns.PropertyChanged += OnColumnsChanged;
    }

    /// <summary>Theme, accent and the Fluent palette; the backdrop is the window's job (it needs the window).</summary>
    public static void ApplyLook()
    {
        if (Application.Current is not { } app)
            return;
        app.RequestedThemeVariant = Current.Theme switch
        {
            "light" => ThemeVariant.Light,
            "system" => ThemeVariant.Default,
            _ => ThemeVariant.Dark,
        };

        var accent = Accents.Find(Current.Accent);
        SetAccent(app, ThemeVariant.Dark, Color.Parse(accent.Dark));
        SetAccent(app, ThemeVariant.Light, Color.Parse(accent.Light));
    }

    private static void SetAccent(Application app, ThemeVariant variant, Color color)
    {
        // The brushes are shared by everything that uses them, so recoloring them recolors the app at once.
        void Brush(string key, byte alpha)
        {
            if (app.TryGetResource(key, variant, out var value) && value is SolidColorBrush brush)
                brush.Color = Color.FromArgb(alpha, color.R, color.G, color.B);
        }
        Brush("Rift.Accent", 0xFF);
        Brush("Rift.AccentBg", 0x26);
        Brush("Rift.AccentBgHover", 0x38);
        Brush("Rift.AccentLine", 0x80);

        // Fluent's own controls (check boxes, focused text boxes, progress bars) follow the same accent.
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent && fluent.Palettes.TryGetValue(variant, out var palette))
            palette.Accent = color;
    }

    /// <summary>
    /// Mica/Acrylic shows through the window background and the chrome (title bar, sidebar, status bar);
    /// the panes stay solid so text keeps its contrast. Called by the window once it knows what Windows granted.
    /// </summary>
    public static void SetSeeThrough(bool seeThrough)
    {
        if (Application.Current is not { } app)
            return;
        foreach (var (variant, chrome, window) in new[] { (ThemeVariant.Dark, "#18181B", "#111113"), (ThemeVariant.Light, "#FFFFFF", "#F3F3F1") })
        {
            Set(app, variant, "Rift.Chrome", Color.Parse(chrome), seeThrough ? (byte)0x55 : (byte)0xFF);
            Set(app, variant, "Rift.WindowBg", Color.Parse(window), seeThrough ? (byte)0x88 : (byte)0xFF);
        }
    }

    private static void Set(Application app, ThemeVariant variant, string key, Color color, byte alpha)
    {
        if (app.TryGetResource(key, variant, out var value) && value is SolidColorBrush brush)
            brush.Color = Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    /// <summary>Changes something and saves it soon; <paramref name="change"/> runs right away.</summary>
    public static void Update(Action<SettingsState> change)
    {
        change(Current);
        ApplyLook();
        Changed?.Invoke();
        SaveTimer.Change(TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);
    }

    /// <summary>On exit: don't lose a change made in the last moment.</summary>
    public static void SaveNow()
    {
        try
        {
            SettingsStore.Save(Current);
            FolderViews.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Saving settings failed", ex);
        }
    }

    private static void OnViewOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewOptions.ShowHidden))
            Update(s => s.ShowHidden = ViewOptions.Current.ShowHidden);
    }

    private static void OnColumnsChanged(object? sender, PropertyChangedEventArgs e)
    {
        var columns = ViewOptions.Current.Columns;
        Current.Columns = new ColumnsState
        {
            Modified = columns.ShowModified,
            Created = columns.ShowCreated,
            Type = columns.ShowType,
            Size = columns.ShowSize,
            ModifiedWidth = columns.ModifiedWidth,
            CreatedWidth = columns.CreatedWidth,
            TypeWidth = columns.TypeWidth,
            SizeWidth = columns.SizeWidth,
        };
        SaveTimer.Change(TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);
    }
}
