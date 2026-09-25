using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace KpcLauncher.Core;

public sealed class LauncherUpdater
{
    public const string RepositoryUrl = "https://github.com/aloisakp/KPC-Project";

    private UpdateManager? manager;
    public bool IsInstalledBuild => !LinuxSteamBridge.IsConfigured && manager?.IsInstalled == true;

    public string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "0.6.2";

    public async Task<UpdateInfo?> CheckAsync()
    {
        // Installer restarts can outlive the Wine process that owns the native helper.
        // Native mode upgrades use the portable bundle and a fresh helper session.
        if (LinuxSteamBridge.IsConfigured) return null;
        manager=new UpdateManager(new GithubSource(RepositoryUrl,null,false));
        return manager.IsInstalled ? await manager.CheckForUpdatesAsync().ConfigureAwait(false) : null;
    }

    public async Task DownloadAndApplyAsync(
        UpdateInfo update,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        if(!IsInstalledBuild)throw new InvalidOperationException("Install the launcher before applying updates. Native Linux helper mode uses manual portable updates.");
        await manager.DownloadUpdatesAsync(update,progress,cancellationToken).ConfigureAwait(false);
        manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
}
