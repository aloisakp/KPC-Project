using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace KpcLauncher.Core;

public sealed record LauncherUpdate(string Version, object Payload);

public sealed class LauncherUpdater
{
    public const string RepositoryUrl = "https://github.com/aloisakp/KPC-Project";

    private UpdateManager? manager;
    public bool IsInstalledBuild => manager?.IsInstalled == true;

    public string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "0.7.0";

    public async Task<LauncherUpdate?> CheckAsync()
    {
        manager=new UpdateManager(new GithubSource(RepositoryUrl,null,false));
        var update = manager.IsInstalled ? await manager.CheckForUpdatesAsync().ConfigureAwait(false) : null;
        return update is null ? null : new LauncherUpdate(update.TargetFullRelease.Version.ToString(), update);
    }

    public async Task DownloadAndApplyAsync(
        LauncherUpdate candidate,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        if(!IsInstalledBuild)throw new InvalidOperationException("Install the launcher before applying updates. ");
        var update = (UpdateInfo)candidate.Payload;
        await manager!.DownloadUpdatesAsync(update,progress,cancellationToken).ConfigureAwait(false);
        manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
}
