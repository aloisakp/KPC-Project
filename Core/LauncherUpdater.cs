using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace KpcLauncher.Core;

public sealed class LauncherUpdater
{
    public const string RepositoryUrl = "https://github.com/aloisakp/KPC-Project";

    private UpdateManager? _manager;

    public bool IsInstalledBuild => _manager?.IsInstalled == true;

    public string CurrentVersion =>
        _manager?.CurrentVersion?.ToString()
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? "development";

    public async Task<UpdateInfo?> CheckAsync()
    {
        _manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false));
        if (!_manager.IsInstalled)
            return null;

        return await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
    }

    public async Task DownloadAndApplyAsync(
        UpdateInfo update,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        if (_manager is null)
            throw new InvalidOperationException("Check for updates before applying one.");

        await _manager.DownloadUpdatesAsync(update, progress, cancellationToken).ConfigureAwait(false);
        _manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
}
