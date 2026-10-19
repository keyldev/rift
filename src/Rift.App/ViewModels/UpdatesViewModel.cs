using System.Diagnostics;
using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rift.App.Services;
using Rift.Core.Localization;
using Velopack;
using Velopack.Sources;

namespace Rift.App.ViewModels;

public enum UpdateState
{
    /// <summary>Not installed through Rift's setup (a dev build, or the plain zip of 0.8 and earlier): nothing to update in place.</summary>
    NotInstalled,
    Idle,
    Checking,
    UpToDate,
    Downloading,
    /// <summary>Downloaded; installs on restart (or on the next start, by Velopack itself).</summary>
    Ready,
    Failed,
}

/// <summary>
/// Updates for the installed Rift, from GitHub Releases through Velopack. Checks shortly after start and
/// then daily (unless turned off in Settings), downloads in the background, and offers a restart;
/// an update that's downloaded but not applied installs on the next start anyway.
/// </summary>
public sealed partial class UpdatesViewModel : ObservableObject
{
    private const string Repository = "https://github.com/keyldev/rift";
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly UpdateManager? _manager;
    private VelopackAsset? _ready;
    private DispatcherTimer? _timer;

    public UpdatesViewModel()
    {
        try
        {
            // RIFT_UPDATE_SOURCE points at a local folder of packages, for trying an update without a release.
            var manager = Environment.GetEnvironmentVariable("RIFT_UPDATE_SOURCE") is { Length: > 0 } local
                ? new UpdateManager(local)
                : new UpdateManager(new GithubSource(Repository, null, prerelease: false));
            if (manager.IsInstalled)
                _manager = manager;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Updates are off: {ex.Message}");
        }

        State = _manager is null ? UpdateState.NotInstalled : UpdateState.Idle;
        if (_manager?.UpdatePendingRestart is { } pending)
        {
            _ready = pending;
            NewVersion = pending.Version.ToString();
            State = UpdateState.Ready;
        }
    }

    /// <summary>The running version, without the commit hash the SDK appends.</summary>
    public string Version { get; } =
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?")
        .Split('+')[0];

    public string VersionText => Strings.UpdateVersion(Version);

    public bool IsInstalled => _manager is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(HasStatus), nameof(IsReady), nameof(CanCheck), nameof(ButtonText))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    private UpdateState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(ButtonText))]
    private string _newVersion = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int _progress;

    public bool IsReady => State == UpdateState.Ready;
    public bool CanCheck => State is UpdateState.Idle or UpdateState.UpToDate or UpdateState.Failed;
    public string ButtonText => Strings.UpdateButton(NewVersion);

    public string StatusText => State switch
    {
        UpdateState.NotInstalled => Strings.UpdateNotInstalled,
        UpdateState.Checking => Strings.UpdateChecking,
        UpdateState.UpToDate => Strings.UpdateUpToDate,
        UpdateState.Downloading => Strings.UpdateDownloading(NewVersion, Progress),
        UpdateState.Ready => Strings.UpdateReady(NewVersion),
        UpdateState.Failed => Strings.UpdateFailed,
        _ => "",
    };

    public bool HasStatus => StatusText.Length > 0;

    /// <summary>Raised after the update is handed to Velopack: the window closes normally (saving the session) and it restarts Rift.</summary>
    public event Action? RestartRequested;

    /// <summary>Starts the automatic checks; once, when the window is up.</summary>
    public void Start()
    {
        if (_manager is null || _timer is not null)
            return;
        _timer = new DispatcherTimer { Interval = FirstCheck };
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = Interval;
            if (AppSettings.Current.CheckForUpdates)
                _ = CheckAsync();
        };
        _timer.Start();
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    public async Task CheckAsync()
    {
        if (_manager is not { } manager || !CanCheck)
            return;
        State = UpdateState.Checking;
        try
        {
            // Off the UI thread: the first call reads the install's metadata from disk before going online.
            var info = await Task.Run(manager.CheckForUpdatesAsync);
            if (info is null)
            {
                State = UpdateState.UpToDate;
                return;
            }

            NewVersion = info.TargetFullRelease.Version.ToString();
            Progress = 0;
            State = UpdateState.Downloading;
            await Task.Run(() => manager.DownloadUpdatesAsync(info, p => Dispatcher.UIThread.Post(() => Progress = p)));
            _ready = info.TargetFullRelease;
            State = UpdateState.Ready;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Update check failed: {ex}");
            State = UpdateState.Failed;
        }
    }

    [RelayCommand]
    private void Restart()
    {
        if (_manager is null || _ready is null)
            return;
        // Velopack's updater waits for this process to exit, swaps in the new version and starts Rift again.
        _manager.WaitExitThenApplyUpdates(_ready, silent: true, restart: true);
        RestartRequested?.Invoke();
    }
}
