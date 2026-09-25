using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using KpcLauncher.Core;

namespace KpcLauncher;

public sealed record Fact(string Caption, string Value);

public sealed partial class MainViewModel : INotifyPropertyChanged, IReporter, IDisposable
{
    private const int MaxLogLines = 3000;

    private readonly Ui _ui = new();
    private readonly LauncherUpdater _updater = new();

    /// <summary>
    /// The installed Steam client, which performs every account operation. The launcher has
    /// no Steam session of its own to hold.
    /// </summary>
    private SteamInstall? _steam;
    private SteamAuthorization? _authorization = SteamAuthorization.Load();

    private CancellationTokenSource? _work;
    private LauncherUpdate? _pendingUpdate;
    private DateTime _lastProgressPush = DateTime.MinValue;
    private DateTime _lastProgressLog = DateTime.MinValue;

    public LauncherConfig Config { get; }
    public ObservableCollection<LogLine> Log { get; } = [];
    public ObservableCollection<Fact> Facts { get; } = [];

    public MainViewModel()
    {
        Config = LauncherConfig.Load();
        _steam = SteamInstall.FindConfigured(Config);

        _storageRoot = Config.StorageRoot;
        InitializeTesterCommands();
        TrySaveConfig();

        DownloadCommand = new RelayCommand(() => _ = DownloadAsync(), () => !IsBusy && _steam is not null);
        AuthorizeCommand = new RelayCommand(() => _ = AuthorizeAsync(), () => !IsBusy);
        ForgetAccountCommand = new RelayCommand(() => _ = RunGuarded("Disconnecting account", _ =>
        {
            SteamAuthorization.Forget();
            _testers.Forget();
            ClearTesterAccess();
            _authorization = null;
            return Task.CompletedTask;
        }), () => !IsBusy && HasAuthorization);
        CancelCommand = new RelayCommand(() => _work?.Cancel(), () => CanCancel);
        BrowseCommand = new RelayCommand(BrowseStorageRoot, () => !IsBusy);
        OpenFolderCommand = new RelayCommand(OpenStorageFolder);
        OpenLogCommand = new RelayCommand(OpenLogFile);
        OpenSteamFolderCommand = new RelayCommand(OpenSteamFolder, () => _steam is not null);
        BrowseSteamFolderCommand = new RelayCommand(BrowseSteamFolder, () => !IsBusy);
        DetectSteamCommand = new RelayCommand(DetectSteam, () => !IsBusy);
        CheckUpdatesCommand = new RelayCommand(() => _ = CheckForUpdatesAsync(), () => !IsBusy);
        ToggleLogCommand = new RelayCommand(() => IsLogVisible = !IsLogVisible);

        RefreshState();
        AppendLog(new LogLine(LogLevel.Dim, $"Storage root: {Config.StorageRoot}"));
        AppendLog(new LogLine(LogLevel.Dim, $"Log file: {CrashLog.Path}"));
    }

    public async Task InitializeAsync()
    {
        if (!HasAuthorization || _testers.Session is null) await AuthorizeAsync();
        else await CheckTesterAccessAsync();
        await CheckForUpdatesAsync();

        if (_steam is null)
        {
            Log_("Steam could not be found. " + SteamFolderHelp, LogLevel.Warn);
            return;
        }

        Log_($"Steam found at {_steam.Root}.", LogLevel.Dim);
        Log_("Downloads are performed by Steam itself. Its signed-in account must match your browser authorization.", LogLevel.Info);
        RefreshState();
    }

    // Navigation

    private bool _isHome = true;
    public bool IsHome
    {
        get => _isHome;
        set
        {
            if (!Set(ref _isHome, value)) return;
            if (value) IsSettings = false;
        }
    }

    private bool _isSettings;
    public bool IsSettings
    {
        get => _isSettings;
        set
        {
            if (!Set(ref _isSettings, value)) return;
            if (value) IsHome = false;
            else TrySaveConfig();
        }
    }

    private bool _isLogVisible;
    public bool IsLogVisible { get => _isLogVisible; private set => Set(ref _isLogVisible, value); }

    // State

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanCancel));
            Requery();
        }
    }

    public bool CanCancel => IsBusy && _work is { IsCancellationRequested: false };

    private string _stepName = "Ready";
    public string StepName { get => _stepName; private set => Set(ref _stepName, value); }

    private string _statusLine = "";
    public string StatusLine { get => _statusLine; private set => Set(ref _statusLine, value); }

    private double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    private bool _progressIndeterminate;
    public bool ProgressIndeterminate
    {
        get => _progressIndeterminate;
        private set => Set(ref _progressIndeterminate, value);
    }

    private string _steamStatus = "";
    public string SteamStatus { get => _steamStatus; private set => Set(ref _steamStatus, value); }
    public bool IsSteamMissing => _steam is null;
    public string SteamFolderHelp => OperatingSystem.IsLinux()
        ? "Choose Steam's data folder containing steamapps and ubuntu12_32 or ubuntu12_64. Native and Flatpak Steam are detected automatically."
        : "Select the Windows Steam installation folder containing steam.exe.";
    public string ExportHelp => _steam?.IsNativeLinux == true
        ? "Character export is not yet supported with native Steam across Proton/Wine environments. Downloads and community play remain available for testing."
        : "Open KurtzPel through Steam, then enter the lobby with the character you want to export.";
    public string SteamFolder => _steam?.Root ?? (string.IsNullOrWhiteSpace(Config.SteamRoot)
        ? "Not found - select Steam folder" : Config.SteamRoot);

    private int _completedArchives;
    public int CompletedArchives
    {
        get => _completedArchives;
        private set
        {
            if (!Set(ref _completedArchives, value)) return;
            OnPropertyChanged(nameof(DownloadsComplete));
            OnPropertyChanged(nameof(DownloadButtonText));
        }
    }

    public bool DownloadsComplete => CompletedArchives == LauncherConfig.RequiredArchives.Count;
    public bool HasAuthorization => _authorization?.IsCurrent == true;
    public string AuthorizedAccount => HasAuthorization ? _authorization!.SteamId.ToString() : "Authorization required";
    public string DownloadButtonText => !HasAuthorization ? "Authorize Steam" : DownloadsComplete ? "Verify downloads" : "Install";

    private string _storageRoot;
    public string StorageRoot
    {
        get => _storageRoot;
        set
        {
            if (!Set(ref _storageRoot, value)) return;
            Config.StorageRoot = value;
            TrySaveConfig();
            RefreshState();
        }
    }

    private string _updateStatus = "Checking for launcher updates...";
    public string UpdateStatus
    {
        get => _updateStatus;
        private set
        {
            if (Set(ref _updateStatus, value)) RefreshFacts();
        }
    }

    public string UpdateButtonText => _pendingUpdate is null
        ? "Check for updates"
        : $"Install update {_pendingUpdate.Version}";

    public string CurrentVersion => _updater.CurrentVersion;

    // Commands

    public ICommand DownloadCommand { get; }
    public ICommand AuthorizeCommand { get; }
    public ICommand ForgetAccountCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand OpenSteamFolderCommand { get; }
    public ICommand BrowseSteamFolderCommand { get; }
    public ICommand DetectSteamCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand ToggleLogCommand { get; }

    // Updates

    private async Task CheckForUpdatesAsync()
    {
        if (_pendingUpdate is not null)
        {
            await InstallPendingUpdateAsync();
            return;
        }

        IsBusy = true;
        Step("Checking for updates");
        UpdateStatus = "Checking for launcher updates...";

        try
        {
            _pendingUpdate = await _updater.CheckAsync();
            OnPropertyChanged(nameof(CurrentVersion));
            OnPropertyChanged(nameof(UpdateButtonText));

            if (!_updater.IsInstalledBuild)
            {
                UpdateStatus = "Use the installed launcher for updates";
                Log_("Install the launcher to enable updates.", LogLevel.Dim);
                return;
            }

            if (_pendingUpdate is null)
            {
                UpdateStatus = "No new launcher update";
                Log_("No launcher update is available.", LogLevel.Dim);
                return;
            }

            UpdateStatus = $"Update {_pendingUpdate.Version} is available";
            Log_($"Launcher update {_pendingUpdate.Version} is available.", LogLevel.Good);

            await InstallPendingUpdateAsync();
        }
        catch (System.Net.Http.HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            UpdateStatus = "No accessible update release";
            Log_("The update repository is private or has no published release yet. Game downloads are still available.", LogLevel.Dim);
        }
        catch (Exception ex)
        {
            CrashLog.WriteException("update check", ex);
            UpdateStatus = "Update check unavailable";
            Log_($"Could not check for launcher updates: {ex.Message}", LogLevel.Warn);
        }
        finally
        {
            IsBusy = false;
            StepName = DownloadsComplete ? "Downloads preserved" : "Ready";
            StatusLine = "";
            Requery();
        }
    }

    private async Task InstallPendingUpdateAsync()
    {
        if (_pendingUpdate is null) return;

        var version = _pendingUpdate.Version.ToString();
        if (!await DesktopUi.Confirm("KPC Launcher update",
                $"KPC Launcher {version} is available.\n\nDownload and open the installer now?")) return;

        using var work = new CancellationTokenSource();
        _work = work;
        IsBusy = true;
        OnPropertyChanged(nameof(CanCancel));
        Requery();
        StepName = $"Downloading launcher {version}";
        ProgressIndeterminate = false;
        Progress = 0;

        try
        {
            await _updater.DownloadAndApplyAsync(
                _pendingUpdate,
                value => _ui.Post(() =>
                {
                    Progress = value;
                    StatusLine = $"{value}%";
                }),
                work.Token);
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            UpdateStatus = $"Update {version} is available";
            Log_("Launcher update cancelled.", LogLevel.Warn);
        }
        catch (Exception ex)
        {
            CrashLog.WriteException("launcher update", ex);
            UpdateStatus = $"Update {version} could not be installed";
            Log_($"Launcher update failed: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            _work = null;
            IsBusy = false;
            ProgressIndeterminate = false;
            StepName = "Ready";
            StatusLine = "";
            Requery();
        }
    }

    // Downloads

    private Task AuthorizeAsync() => RunGuarded("Authorizing Steam account", async ct =>
    {
        ClearTesterAccess();
        var steamId = await _testers.SignInAsync(this, ct).ConfigureAwait(false);
        var authorization = new SteamAuthorization(steamId, DateTimeOffset.UtcNow);
        authorization.Save();
        _authorization = authorization;
        Log_("Steam account authorized. Downloads will use the same account in your Steam client.", LogLevel.Good);
        if (_steam?.ActiveSteamId is { } active && active != steamId)
            Log_("Your Steam client is using another account. Switch it to the authorized account before Install.", LogLevel.Warn);
        await RefreshTesterAccessAsync(ct).ConfigureAwait(false);
        await PrepareLocalTesterDataAsync(ct, refreshAccess:false).ConfigureAwait(false);
    });

    private async Task DownloadAsync()
    {
        if (!HasAuthorization) { await AuthorizeAsync(); return; }
        if (_steam is null)
        {
            Log_("Steam could not be found. Select its installation folder in Settings > Steam.",
                LogLevel.Error);
            return;
        }

        var recheck = DownloadsComplete;

        await RunGuarded(recheck ? "Verifying preserved downloads" : "Preparing",
            async cancellationToken =>
            {
                TrySaveConfig();
                // Existing-file hashing can run before the pipeline's first await.
                // Start it on a worker so the window and Cancel remain responsive.
                await Task.Run(() => new PreservationPipeline(Config, _steam, _authorization!, this, OnArchiveReadyAsync)
                    .RunAsync(recheck, cancellationToken), cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    private async void BrowseStorageRoot()
    {
        try
        {
            var folder = await DesktopUi.PickFolder("Choose where preserved downloads will be stored", Config.StorageRoot);
            if (folder is null) return;
            StorageRoot = folder;
            Log_($"Storage root: {Config.StorageRoot}", LogLevel.Dim);
        }
        catch (Exception ex) { await DesktopUi.Inform("Storage folder", ex.Message); }
    }
    private void OpenStorageFolder() => OpenFolder(Config.StorageRoot, create: true);
    private async void BrowseSteamFolder()
    {
        try
        {
            var folder = await DesktopUi.PickFolder(SteamFolderHelp, SteamFolder);
            if (folder is not null) SetSteamFolder(folder);
        }
        catch (Exception ex) { await DesktopUi.Inform("Steam folder", ex.Message); }
    }
    private async void DetectSteam()
    {
        try { SetSteamFolder(null); }
        catch (Exception ex) { await DesktopUi.Inform("Steam folder", ex.Message); }
    }

    internal void SetSteamFolder(string? root)
    {
        if (IsBusy) return;
        var steam = SteamInstall.Find(root);
        if (!string.IsNullOrWhiteSpace(root) && steam is null)
            throw new IOException("Steam could not use that folder. " + SteamFolderHelp);

        var previous = Config.SteamRoot;
        Config.SteamRoot = string.IsNullOrWhiteSpace(root) ? "" : steam!.Root;
        try { Config.Save(); }
        catch { Config.SteamRoot = previous; throw; }
        _steam = steam;
        RefreshState();
        Log_(steam is null ? "Steam was not detected. Select its installation folder."
            : $"Steam folder: {steam.Root}", steam is null ? LogLevel.Warn : LogLevel.Good);
    }

    private void OpenSteamFolder()
    {
        if (_steam is null) return;
        OpenFolder(_steam.Root);
    }

    private void OpenFolder(string path, bool create = false)
    {
        try
        {
            path = Path.GetFullPath(path);
            if (create) Directory.CreateDirectory(path);
            var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") : "xdg-open")
                { UseShellExecute = false };
            info.ArgumentList.Add(path);
            Process.Start(info)?.Dispose();
        }
        catch (Exception ex) { Log_($"Could not open the folder: {ex.Message}", LogLevel.Warn); }
    }

    private void OpenLogFile()
    {
        if (!File.Exists(CrashLog.Path))
        {
            Log_("No log file exists yet.", LogLevel.Warn);
            return;
        }

        try { Process.Start(new ProcessStartInfo(CrashLog.Path) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { Log_($"Could not open the log: {ex.Message}", LogLevel.Warn); }
    }

    private void RefreshState(bool resetStep = true) => _ui.Post(() =>
    {
        CompletedArchives = PreservationPipeline.CompletedCount(Config);
        SteamStatus = DescribeSteam();
        OnPropertyChanged(nameof(SteamFolder));
        OnPropertyChanged(nameof(IsSteamMissing));
        OnPropertyChanged(nameof(ExportHelp));
        OnPropertyChanged(nameof(HasAuthorization));
        OnPropertyChanged(nameof(AuthorizedAccount));
        OnPropertyChanged(nameof(DownloadButtonText));
        RefreshTesterProperties();
        RefreshFacts();
        if (!IsBusy && resetStep)
            StepName = !HasAuthorization ? "Authorize Steam to continue" : DownloadsComplete ? "Downloads preserved" : "Ready";
        Requery();
    });

    private string DescribeSteam() => _steam switch
    {
        null => "Not found - select Steam folder",
        _ when _steam.ActiveSteamId is { } active => !HasAuthorization ? "Authorization required" :
            active == _authorization!.SteamId ? "Account matches" : "Different account",
        _ when _steam.IsClientRunning => "Running, signed out",
        _ => "Not running",
    };

    private void RefreshFacts()
    {
        if (!DesktopUi.CheckAccess())
        {
            DesktopUi.Post(RefreshFacts);
            return;
        }

        Facts.Clear();
        Facts.Add(new Fact("STATUS", DownloadsComplete ? "Preserved" : "Not downloaded"));
        Facts.Add(new Fact("FILE SETS", $"{CompletedArchives} of {LauncherConfig.RequiredArchives.Count} complete"));
        Facts.Add(new Fact("STEAM", SteamStatus));
        Facts.Add(new Fact("AUTHORIZED ACCOUNT", AuthorizedAccount));
        Facts.Add(new Fact("UPDATES", UpdateStatus));
        Facts.Add(new Fact("TESTER ACCESS", TesterAccessStatus));
        Facts.Add(new Fact("LOCATION", Config.StorageRoot));
    }

    // Long-running work helper

    private async Task RunGuarded(string description, Func<CancellationToken, Task> body)
    {
        using var work = new CancellationTokenSource();
        _work = work;
        IsBusy = true;
        Step(description);

        try
        {
            await body(work.Token).ConfigureAwait(false);
            Step("Complete");
            _ui.Post(() => StatusLine = "");
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            Log_("Launcher operation cancelled. A transfer already handed to Steam may continue.", LogLevel.Warn);
            Step("Cancelled");
        }
        catch (OperationCanceledException ex)
        {
            Log_($"Timed out: {ex.Message}", LogLevel.Error);
            Step("Failed");
        }
        catch (Exception ex)
        {
            CrashLog.WriteException(description, ex);
            Log_(ex.Message, LogLevel.Error);
            if (ex.InnerException is { } inner && inner.Message != ex.Message)
                Log_($"  ({inner.Message})", LogLevel.Dim);
            Step("Failed");
        }
        finally
        {
            _work = null;
            _ui.Post(() => { IsBusy = false; ProgressIndeterminate = false; });
            RefreshState(resetStep: false);
        }
    }

    // IReporter

    void IReporter.Log(string text, LogLevel level) => Log_(text, level);

    private void Log_(string text, LogLevel level = LogLevel.Info) =>
        _ui.Post(() => AppendLog(new LogLine(level, text)));

    private void AppendLog(LogLine line)
    {
        CrashLog.Write(line.Level == LogLevel.Info ? line.Text : $"[{line.Level}] {line.Text}");
        Log.Add(line);
        while (Log.Count > MaxLogLines) Log.RemoveAt(0);
        LogAppended?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? LogAppended;

    void IReporter.Step(string name) => Step(name);

    private void Step(string name) => _ui.Post(() =>
    {
        StepName = name;
        StatusLine = "";
        ProgressIndeterminate = true;
        Progress = 0;
    });

    void IReporter.Progress(StepProgress progress)
    {
        var now = DateTime.UtcNow;
        var finished = progress.Total > 0 && progress.Done >= progress.Total;

        if (finished || (now - _lastProgressLog).TotalSeconds >= 30)
        {
            _lastProgressLog = now;
            CrashLog.Write($"    {progress.Step}: {progress.Detail}");
        }

        if (!finished && (now - _lastProgressPush).TotalMilliseconds < 100) return;
        _lastProgressPush = now;

        _ui.Post(() =>
        {
            StepName = progress.Step;
            StatusLine = progress.Detail;
            ProgressIndeterminate = progress.Total <= 0;
            Progress = progress.Fraction * 100;
        });
    }

    // Plumbing

    private sealed class Ui { public void Post(Action action) => DesktopUi.Post(action); }
    private static void Requery() => DesktopUi.Post(RelayCommand.Requery);

    public void TrySaveConfig()
    {
        try { Config.Save(); }
        catch (Exception ex) { CrashLog.Write($"could not save settings: {ex.Message}"); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    public void Dispose()
    {
        _work?.Cancel();
        _testers.Dispose();
    }
}
