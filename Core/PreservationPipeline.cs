using System.IO;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;

namespace KpcLauncher.Core;

/// <summary>
/// What was written when an archive finished, so a later check can tell whether the files on
/// disk are still the ones Steam delivered without downloading them again.
/// </summary>
public sealed record ArchiveStamp(string Manifest, int Files, long Bytes, string? ContentSha256 = null);

public sealed class PreservationPipeline(
    LauncherConfig config,
    SteamInstall steam,
    SteamAuthorization authorization,
    IReporter reporter)
{
    private const string CompletionStamp = ".kpdl-complete";
    private const string StagingMarker = ".kpc-staging";

    /// <param name="verifyExisting">
    /// Re-examines archives that are already marked complete. Steam has no verify-only mode,
    /// so this compares the files on disk against what was recorded when they arrived and only
    /// downloads again if that comparison fails.
    /// </param>
    public async Task RunAsync(bool verifyExisting, CancellationToken cancellationToken)
    {
        if (!authorization.IsCurrent)
            throw new SteamDownloadException("Authorize Steam before continuing.");
        SafePaths.NoLinks(config.StorageRoot);
        if (SafePaths.Within(config.StorageRoot, steam.Root) || SafePaths.Within(steam.Root, config.StorageRoot) ||
            SafePaths.Within(config.StorageRoot, AppContext.BaseDirectory) ||
            SafePaths.Within(config.StorageRoot, LauncherConfig.AppDataDir))
            throw new SteamDownloadException("Choose a storage folder separate from Steam and the launcher folders.");
        Directory.CreateDirectory(config.StorageRoot);

        // A second launcher must not issue a competing command or move shared staging files.
        var lockPath = Path.Combine(steam.Root, "steamapps", "content", ".kpc-download.lock");
        SafePaths.NoLinks(lockPath);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        using var downloadLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var needed = LauncherConfig.RequiredArchives
            .Where(archive => !Survives(archive, verifyExisting, cancellationToken))
            .ToList();

        if (needed.Count == 0)
        {
            reporter.Step("Preservation complete");
            reporter.Log("Both required archives are stored locally.", LogLevel.Good);
            return;
        }

        await steam.EnsureReadyAsync(reporter, cancellationToken).ConfigureAwait(false);
        steam.RequireAccount(authorization);

        foreach (var archive in needed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            steam.RequireAccount(authorization);
            var directory = config.ArchiveDirectory(archive);

            var staging = steam.StagingDirectory(LauncherConfig.AppId, LauncherConfig.DepotId);
            steam.RequireDepotIdle(LauncherConfig.AppId, LauncherConfig.DepotId);
            PrepareStaging(staging, archive.ManifestId);

            reporter.Step($"Downloading {archive.Label}");
            reporter.Log($"{archive.Label} -> {directory}", LogLevel.Dim);

            var produced = await new DepotDownload(steam, authorization, reporter)
                .RunAsync(
                    LauncherConfig.AppId,
                    LauncherConfig.DepotId,
                    archive.ManifestId,
                    archive.Label,
                    cancellationToken)
                .ConfigureAwait(false);

            reporter.Step($"Filing {archive.Label}");
            steam.RequireAccount(authorization);
            await FileIntoPlaceAsync(produced, directory, cancellationToken).ConfigureAwait(false);

            reporter.Step($"Verifying {archive.Label}");
            WriteStamp(directory, archive, cancellationToken);
            ClearStagingMarker(staging);
            reporter.Log($"{archive.Label} preserved in {directory}", LogLevel.Good);
        }

        reporter.Step("Preservation complete");
        reporter.Log("Both required archives are stored locally.", LogLevel.Good);
    }

    /// <summary>
    /// Decides whether an archive can be left alone. Downloading 27 GB again is never the way
    /// to answer "are these files still here", so a check reads the directory rather than the
    /// network, and only a genuine mismatch costs a download.
    /// </summary>
    private bool Survives(ArchiveSpec archive, bool verifyExisting, CancellationToken cancellationToken)
    {
        var stamp = ReadStamp(config.ArchiveDirectory(archive));
        if (stamp is null || stamp.Manifest != archive.ManifestId.ToString()) return false;

        if (!verifyExisting)
        {
            reporter.Log($"{archive.Label} is already preserved.", LogLevel.Dim);
            return true;
        }

        reporter.Step($"Checking {archive.Label}");
        var (files, bytes, digest) = Measure(config.ArchiveDirectory(archive), cancellationToken, reporter, $"Checking {archive.Label}");

        if (stamp.ContentSha256 is null)
        {
            reporter.Log($"{archive.Label} has no integrity receipt from its original download. " +
                "Steam will download/recheck it to establish one.", LogLevel.Warn);
            return false;
        }

        if (files == stamp.Files && bytes == stamp.Bytes && digest == stamp.ContentSha256)
        {
            reporter.Log(
                $"{archive.Label} verified: {files} files, {Human.Bytes(bytes)}, unchanged.", LogLevel.Good);
            return true;
        }

        reporter.Log(
            $"{archive.Label} no longer matches what Steam delivered "
            + $"(expected {stamp.Files} files / {Human.Bytes(stamp.Bytes)}, "
            + $"found {files} / {Human.Bytes(bytes)}). Downloading it again.", LogLevel.Warn);
        return false;
    }

    private void WriteStamp(string directory, ArchiveSpec archive, CancellationToken cancellationToken)
    {
        var (files, bytes, digest) = Measure(directory, cancellationToken, reporter, $"Verifying {archive.Label}");
        if (files == 0) throw new SteamDownloadException("Steam left an empty archive; it was not marked complete.");
        var stamp = new ArchiveStamp(archive.ManifestId.ToString(), files, bytes, digest);
        var path = Path.Combine(directory, CompletionStamp);
        SafePaths.NoLinks(path);
        File.WriteAllText(path, JsonSerializer.Serialize(stamp));
    }

    /// <summary>Hashes archive paths and contents, ignoring the receipt itself.</summary>
    internal static (int Files, long Bytes, string Digest) Measure(string directory, CancellationToken cancellationToken,
        IReporter? reporter = null, string step = "Verifying files")
    {
        if (!Directory.Exists(directory)) return (0, 0, "");

        var files = 0;
        var bytes = 0L;

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        var inputs = SafePaths.Files(directory).Where(f => Path.GetRelativePath(directory, f.FullName) != CompletionStamp)
            .OrderBy(f => Path.GetRelativePath(directory, f.FullName), StringComparer.Ordinal).ToArray();
        var total = inputs.Sum(f => f.Length);
        long checkedBytes = 0;
        var lastReport = DateTime.MinValue;
        foreach (var file in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(directory, file.FullName);
            using var input = file.OpenRead();
            using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                fileHash.AppendData(buffer, 0, read);
                checkedBytes += read;
                if (DateTime.UtcNow - lastReport >= TimeSpan.FromMilliseconds(200) || checkedBytes == total)
                {
                    reporter?.Progress(new StepProgress(step, checkedBytes, total,
                        $"SHA-256: {Human.Bytes(checkedBytes)} of {Human.Bytes(total)}"));
                    lastReport = DateTime.UtcNow;
                }
            }
            // JSON escapes separators and newlines in paths; one unambiguous entry per line.
            digest.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                Path = relative.Replace('\\', '/'), Size = file.Length,
                Hash = Convert.ToHexString(fileHash.GetHashAndReset()),
            }) + "\n"));
            files++;
            bytes += file.Length;
        }

        return (files, bytes, Convert.ToHexString(digest.GetHashAndReset()));
    }

    /// <summary>
    /// Reads a completion stamp. Older stamps held only the manifest id as plain text, so those
    /// are still accepted and simply carry no counts to verify against.
    /// </summary>
    private static ArchiveStamp? ReadStamp(string directory)
    {
        try
        {
            var path = Path.Combine(directory, CompletionStamp);
            SafePaths.NoLinks(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 16384) return null;

            var text = File.ReadAllText(path).Trim();
            if (text.StartsWith('{'))
                return JsonSerializer.Deserialize<ArchiveStamp>(text);

            return text.Length > 0 ? new ArchiveStamp(text, 0, 0) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Every manifest of this depot stages into the same directory, so a previous archive's
    /// files have to be cleared before Steam merges them into the next one. A marker records
    /// which manifest is staged there, so an interrupted download of the same archive is left
    /// alone for Steam to resume.
    /// </summary>
    private void PrepareStaging(string staging, ulong manifestId)
    {
        SafePaths.NoLinks(staging);
        var marker = Path.Combine(Path.GetDirectoryName(staging)!, StagingMarker);
        SafePaths.NoLinks(marker);
        var staged = File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;

        if (staged == manifestId.ToString() && Directory.Exists(staging))
        {
            reporter.Log("Resuming a partial download already staged by Steam.", LogLevel.Dim);
            return;
        }

        if (Directory.Exists(staging))
        {
            var backup = staging + ".previous-" + Guid.NewGuid().ToString("N");
            reporter.Log($"Keeping the previous staged files at {backup}.", LogLevel.Dim);
            Directory.Move(staging, backup);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, manifestId.ToString());
    }

    private static void ClearStagingMarker(string staging)
    {
        try
        {
            var marker = Path.Combine(Path.GetDirectoryName(staging)!, StagingMarker);
            SafePaths.NoLinks(marker);
            File.Delete(marker);
        }
        catch (Exception) { /* the next run overwrites it anyway */ }
    }

    private async Task FileIntoPlaceAsync(string from, string to, CancellationToken cancellationToken)
    {
        SafePaths.NoLinks(from);
        SafePaths.NoLinks(to);
        _ = SafePaths.Files(from).Count();
        if (!Directory.Exists(from))
            throw new SteamDownloadException(
                $"Steam reported the download finished but left nothing in {from}.");

        if (Directory.Exists(to))
        {
            var backup = to + ".previous-" + Guid.NewGuid().ToString("N");
            Directory.Move(to, backup);
            reporter.Log($"Previous archive kept at {backup}.", LogLevel.Dim);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);

        if (string.Equals(Path.GetPathRoot(from), Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase))
        {
            // Same volume, so filing the archive is a rename however large it is.
            await MoveWithRetryAsync(from, to, cancellationToken).ConfigureAwait(false);
            return;
        }

        reporter.Log(
            "The storage folder is on a different drive from Steam, so these files have to be "
            + "copied instead of moved. Choosing a folder on the Steam drive makes this instant.",
            LogLevel.Warn);

        await CopyTreeAsync(from, to, reporter, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        SafePaths.NoLinks(from);
        try { Directory.Delete(from, recursive: true); }
        catch (Exception ex) { reporter.Log($"Could not clear Steam's staging folder: {ex.Message}", LogLevel.Warn); }
    }

    /// <summary>
    /// Steam can still be closing handles in the instant after it reports the download
    /// complete, so a rename that fails is retried briefly before it is treated as an error.
    /// </summary>
    private async Task MoveWithRetryAsync(string from, string to, CancellationToken cancellationToken)
    {
        const int Attempts = 10;

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < Attempts)
            {
                if (attempt == 1)
                    reporter.Log("Waiting for Steam to release the files.", LogLevel.Dim);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal static async Task CopyTreeAsync(string from, string to, IReporter reporter, CancellationToken cancellationToken)
    {
        var files = SafePaths.Files(from).ToArray();
        var total = files.Sum(file => file.Length);
        long copied = 0;
        var buffer = new byte[1024 * 1024];

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(from, file.FullName);
            var destination = Path.Combine(to, relative);
            SafePaths.NoLinks(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;
                reporter.Progress(new StepProgress("Copying to the storage folder", copied, total,
                    $"{Human.Bytes(copied)} of {Human.Bytes(total)}"));
            }
        }
    }

    public static int CompletedCount(LauncherConfig config) =>
        LauncherConfig.RequiredArchives.Count(archive => IsComplete(config, archive));

    private static bool IsComplete(LauncherConfig config, ArchiveSpec archive) =>
        ReadStamp(config.ArchiveDirectory(archive))?.Manifest == archive.ManifestId.ToString();
}
