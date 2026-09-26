using System.IO;

namespace KpcLauncher.Core;

internal static class ArchiveRecovery
{
    internal static string? Find(SteamInstall steam, LauncherConfig config, ArchiveSpec archive,
        IReporter reporter, CancellationToken cancellationToken)
    {
        var filename = $"{LauncherConfig.DepotId}_{archive.ManifestId}.manifest";
        SteamDepotManifest? manifest = null;
        foreach (var cache in new[] { "depotcache", "steamapps/depotcache", "ubuntu12_32/depotcache", "ubuntu12_64/depotcache" })
        {
            var path = Path.Combine(steam.Root, cache, filename);
            if (!File.Exists(path)) continue;
            try { manifest = SteamDepotManifest.Read(path, LauncherConfig.DepotId, archive.ManifestId); break; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException or SteamDownloadException)
            { reporter.Log($"Cannot use Steam's cached manifest for recovery: {ex.Message}", LogLevel.Warn); }
        }
        if (manifest is null) return null;
        var bases = new[] { config.ArchiveDirectory(archive) }
            .Concat(steam.StagingDirectories(LauncherConfig.AppId, LauncherConfig.DepotId));
        foreach (var basis in bases)
        {
            foreach (var candidate in Candidates(basis))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (manifest.Matches(candidate, reporter, archive.Label, cancellationToken)) return candidate;
            }
        }
        return null;
    }

    private static IEnumerable<string> Candidates(string basis)
    {
        SafePaths.NoLinks(basis);
        if (Directory.Exists(basis)) yield return basis;
        var parent = Path.GetDirectoryName(basis)!;
        if (!Directory.Exists(parent)) yield break;
        var prefix = Path.GetFileName(basis) + ".previous-";
        foreach (var sibling in Directory.EnumerateDirectories(parent))
        {
            var name = Path.GetFileName(sibling);
            if (name.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParseExact(name[prefix.Length..], "N", out _))
            {
                SafePaths.NoLinks(sibling);
                yield return sibling;
            }
        }
    }
}
