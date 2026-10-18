using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rift.App.Services;
using Rift.Core;
using Rift.Core.Localization;

namespace Rift.App.ViewModels;

public sealed record Choice(string Value, string Label);

public sealed partial class AccentChoice(Accent accent) : ObservableObject
{
    public Accent Accent { get; } = accent;
    [ObservableProperty] private bool _isSelected;

    /// <summary>The shade for the theme on screen.</summary>
    [ObservableProperty] private IBrush _brush = Brushes.Transparent;

    public void UseTheme(bool dark) => Brush = new SolidColorBrush(Color.Parse(dark ? Accent.Dark : Accent.Light));
}

/// <summary>One command in the Hotkeys list.</summary>
public sealed partial class HotkeyRow(AppCommand command) : ObservableObject
{
    public AppCommand Command { get; } = command;
    public string Title => Command.Title;
    public string Icon => Command.Icon;

    [ObservableProperty] private bool _isRecording;

    public string KeysText => IsRecording ? Strings.HotkeyPress
        : Command.Keys.Count == 0 ? Strings.HotkeyNone
        : string.Join(", ", Command.Keys.Select(Gestures.Format));

    public bool IsCustomized => Command.IsCustomized;

    public void Refresh()
    {
        OnPropertyChanged(nameof(KeysText));
        OnPropertyChanged(nameof(IsCustomized));
    }

    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(KeysText));
}

public sealed partial class TagRow : ObservableObject
{
    public TagRow(TagDefinition tag)
    {
        Id = tag.Id;
        _name = tag.Name;
        _color = tag.Color;
    }

    public string Id { get; }
    [ObservableProperty] private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Brush))]
    private string _color;

    public IBrush Brush => new SolidColorBrush(Avalonia.Media.Color.Parse(Color));

    public TagDefinition ToDefinition() => new(Id, string.IsNullOrWhiteSpace(Name) ? Strings.TagUnnamed : Name.Trim(), Color);
}

/// <summary>The Settings screen (Ctrl+,). Every change applies at once and is saved; only the language needs a restart.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    private readonly string _startLanguage = AppSettings.Current.Language;

    public SettingsViewModel(MainWindowViewModel main)
    {
        _main = main;
        Accents = [.. Rift.Core.Accents.All.Select(a => new AccentChoice(a))];
        Hotkeys = [.. AppCommands.All.Select(c => new HotkeyRow(c))];
        Load();
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsDark))
                ShowAccentsFor(main.IsDark);
        };
        ShowAccentsFor(main.IsDark);
    }

    [ObservableProperty] private bool _isOpen;

    public IReadOnlyList<Choice> Themes { get; } =
    [
        new("dark", Strings.ThemeDark),
        new("light", Strings.ThemeLight),
        new("system", Strings.ThemeSystem),
    ];

    public IReadOnlyList<Choice> Backdrops { get; } =
    [
        new("solid", Strings.BackdropSolid),
        new("mica", Strings.BackdropMica),
    ];

    public bool MicaSupported => AppSettings.MicaSupported;

    public IReadOnlyList<Choice> Languages { get; } =
    [
        new("system", Strings.LanguageSystem),
        new("en", "English"),
        new("ru", "Русский"),
    ];

    public IReadOnlyList<AccentChoice> Accents { get; }
    public IReadOnlyList<HotkeyRow> Hotkeys { get; }
    public ObservableCollection<TagRow> Tags { get; } = [];

    [ObservableProperty] private Choice? _theme;
    [ObservableProperty] private Choice? _backdrop;
    [ObservableProperty] private Choice? _language;
    [ObservableProperty] private bool _rememberFolderViews;
    [ObservableProperty] private string? _hotkeyNotice;

    public ViewOptions View => ViewOptions.Current;
    public MainWindowViewModel Main => _main;

    /// <summary>The language differs from the one Rift started with: it needs a restart.</summary>
    public bool NeedsRestart => (Language?.Value ?? "system") != _startLanguage;

    private bool _loading;

    private void Load()
    {
        _loading = true;
        var s = AppSettings.Current;
        Theme = Themes.FirstOrDefault(t => t.Value == s.Theme) ?? Themes[0];
        Backdrop = Backdrops.FirstOrDefault(b => b.Value == s.Backdrop) ?? Backdrops[0];
        Language = Languages.FirstOrDefault(l => l.Value == s.Language) ?? Languages[0];
        RememberFolderViews = s.RememberFolderViews;
        foreach (var accent in Accents)
            accent.IsSelected = accent.Accent.Id == Rift.Core.Accents.Find(s.Accent).Id;
        Tags.Clear();
        foreach (var tag in s.Tags)
            Tags.Add(new TagRow(tag));
        _loading = false;
    }

    [RelayCommand]
    private void Open()
    {
        Load();
        HotkeyNotice = null;
        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        StopRecording();
        SaveTags();
        IsOpen = false;
    }

    partial void OnThemeChanged(Choice? value)
    {
        if (!_loading && value is not null)
            AppSettings.Update(s => s.Theme = value.Value);
    }

    partial void OnBackdropChanged(Choice? value)
    {
        if (!_loading && value is not null)
            AppSettings.Update(s => s.Backdrop = value.Value);
    }

    partial void OnLanguageChanged(Choice? value)
    {
        if (!_loading && value is not null)
            AppSettings.Update(s => s.Language = value.Value);
        OnPropertyChanged(nameof(NeedsRestart));
    }

    partial void OnRememberFolderViewsChanged(bool value)
    {
        if (!_loading)
            AppSettings.Update(s => s.RememberFolderViews = value);
    }

    private void ShowAccentsFor(bool dark)
    {
        foreach (var accent in Accents)
            accent.UseTheme(dark);
    }

    [RelayCommand]
    private void PickAccent(AccentChoice choice)
    {
        foreach (var accent in Accents)
            accent.IsSelected = accent == choice;
        AppSettings.Update(s => s.Accent = choice.Accent.Id);
    }

    /// <summary>Starts Rift again, so the new language reaches every screen. The session comes back as it was.</summary>
    [RelayCommand]
    private void Restart()
    {
        if (Environment.ProcessPath is not { } exe)
            return;
        _main.SaveSession();
        AppSettings.SaveNow();
        Process.Start(new ProcessStartInfo(exe, "--restart") { UseShellExecute = false });
        RestartRequested?.Invoke();
    }

    /// <summary>The window closes itself; the new process waits for this one to let go.</summary>
    public event Action? RestartRequested;

    // ---------- hotkeys ----------

    public HotkeyRow? Recording => Hotkeys.FirstOrDefault(h => h.IsRecording);

    [RelayCommand]
    private void Record(HotkeyRow row)
    {
        StopRecording();
        HotkeyNotice = null;
        row.IsRecording = true;
    }

    public void StopRecording()
    {
        foreach (var row in Hotkeys)
            row.IsRecording = false;
    }

    /// <summary>A key pressed while a row records. Esc cancels; Backspace clears the command's keys.</summary>
    public bool OnKey(Key key, KeyModifiers modifiers)
    {
        if (Recording is not { } row)
            return false;
        if (key == Key.Escape && modifiers == KeyModifiers.None)
        {
            StopRecording();
            return true;
        }
        if (key == Key.Back && modifiers == KeyModifiers.None)
        {
            SetKeys(row.Command, "");
            StopRecording();
            return true;
        }
        if (Gestures.FromKeyPress(key, modifiers) is not { } gesture)
            return true; // a lone modifier or a plain letter: keep waiting

        // A key belongs to one command: take it away from whoever had it.
        if (AppCommands.Using(gesture, row.Command) is { } other)
        {
            SetKeys(other, string.Join(", ", other.Keys.Where(k => !k.Equals(gesture)).Select(k => k.ToString())));
            HotkeyNotice = Strings.HotkeyMoved(Gestures.Format(gesture), other.Title);
        }
        SetKeys(row.Command, gesture.ToString());
        StopRecording();
        return true;
    }

    [RelayCommand]
    private void ResetKeys(HotkeyRow row)
    {
        AppSettings.Update(s => s.Hotkeys.Remove(row.Command.Id));
        RefreshHotkeys();
    }

    [RelayCommand]
    private void ResetAllKeys()
    {
        AppSettings.Update(s => s.Hotkeys.Clear());
        HotkeyNotice = null;
        RefreshHotkeys();
    }

    private void SetKeys(AppCommand command, string keys)
    {
        AppSettings.Update(s =>
        {
            if (keys == command.DefaultKeys)
                s.Hotkeys.Remove(command.Id);
            else
                s.Hotkeys[command.Id] = keys;
        });
        RefreshHotkeys();
    }

    private void RefreshHotkeys()
    {
        foreach (var row in Hotkeys)
            row.Refresh();
    }

    // ---------- tags ----------

    [RelayCommand]
    private void AddTag()
    {
        var color = Rift.Core.Accents.TagColors[Tags.Count % Rift.Core.Accents.TagColors.Count];
        Tags.Add(new TagRow(new TagDefinition(Guid.NewGuid().ToString("N")[..8], Strings.TagNew(Tags.Count + 1), color)));
        SaveTags();
    }

    [RelayCommand]
    private void CycleTagColor(TagRow tag)
    {
        var colors = Rift.Core.Accents.TagColors;
        var index = colors.ToList().IndexOf(tag.Color);
        tag.Color = colors[(index + 1) % colors.Count];
        SaveTags();
    }

    [RelayCommand]
    private void DeleteTag(TagRow tag)
    {
        Tags.Remove(tag);
        AppSettings.Tags.Forget(tag.Id);
        SaveTags();
    }

    /// <summary>Names are saved when the box loses focus or the screen closes.</summary>
    public void SaveTags() => AppSettings.Update(s => s.Tags = Tags.Select(t => t.ToDefinition()).ToList());
}
