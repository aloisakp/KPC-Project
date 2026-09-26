using System.IO;

namespace KpcLauncher.Core;

internal static class SteamResumeState
{
    /// <summary>
    /// Steam keeps depot transfer/staging counters outside the depot directory.
    /// A completed record can survive moving or deleting the files and produce an
    /// immediate empty completion. Keep the exact record as a backup before retrying.
    /// Caller holds the launcher download lock and has checked that this depot is idle.
    /// </summary>
    internal static void Retire(string staging, uint appId, uint depotId, ulong manifestId, IReporter reporter)
    {
        SafePaths.NoLinks(staging);
        var parent = Path.GetDirectoryName(staging)!;
        foreach (var name in new[] { $"state_{appId}_{depotId}_{manifestId}.patch", $"state_{appId}_{depotId}.patch" })
        {
            var path = Path.Combine(parent, name);
            SafePaths.NoLinks(path);
            if (!File.Exists(path)) continue;
            var backup = path + ".previous-" + Guid.NewGuid().ToString("N");
            File.Move(path, backup);
            reporter.Log($"Resetting Steam's saved download progress; previous record kept at {backup}.", LogLevel.Dim);
        }
    }
}
