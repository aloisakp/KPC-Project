using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace KpcLauncher.Core;

public sealed class SteamDownloadException(string message) : Exception(message);

/// <summary>
/// Drives one depot download through the installed Steam client.
///
/// Steam accepts console commands on its own command line, so
/// "steam.exe +download_depot &lt;app&gt; &lt;depot&gt; &lt;manifest&gt;" runs a complete
/// transfer with no console window, no pasted command, and no credential reaching this
/// process. Progress and the final outcome are read back out of Steam's own logs.
///
/// The patterns below match Steam's format strings verbatim:
///   Downloading depot %u (%u files, %u MB) ...
///   Depot download complete : "%s" (manifest %llu)
///   Depot download failed : %s (%s)
/// </summary>
public sealed partial class DepotDownload(SteamInstall steam, SteamAuthorization authorization, IReporter reporter)
{
    /// <summary>How long Steam may go without acknowledging the command before giving up.</summary>
    private static readonly TimeSpan AcknowledgeTimeout = TimeSpan.FromMinutes(3);

    /// <summary>How long the transfer may go with no log activity and no growth on disk.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    [GeneratedRegex(@"Downloading depot (?<depot>\d+) \((?<files>\d+) files, (?<mb>\d+) MB\)")]
    private static partial Regex StartedPattern();

    [GeneratedRegex(@"Depot download complete : ""(?<dir>.*)"" \(manifest (?<manifest>\d+)\)")]
    private static partial Regex CompletePattern();

    [GeneratedRegex(@"Depot download failed : (?<reason>.+)")]
    private static partial Regex FailedPattern();

    [GeneratedRegex(@"AppID (?<app>\d+) update started : download \d+/(?<total>\d+)")]
    private static partial Regex TransferTotalPattern();

    [GeneratedRegex(@"Current download rate: (?<mbps>[\d.]+) Mbps")]
    private static partial Regex RatePattern();

    /// <summary>
    /// Runs the download to completion and returns the directory Steam reported writing.
    /// </summary>
    public async Task<string> RunAsync(
        uint appId,
        uint depotId,
        ulong manifestId,
        string label,
        CancellationToken cancellationToken)
    {
        // Start reading both logs from their current ends, so nothing from an earlier run is
        // mistaken for this one's result.
        var console = LogTail.FromEnd(steam.ConsoleLog);
        var content = LogTail.FromEnd(steam.ContentLog);
        var staging = steam.StagingDirectory(appId, depotId);

        reporter.Step($"Asking Steam for {label}");
        steam.DownloadDepot(appId, depotId, manifestId, authorization);

        var started = DateTime.UtcNow;
        var lastChange = DateTime.UtcNow;
        var acknowledged = false;
        long transferTotal = 0;
        double mbps = 0;
        var rateObserved = DateTime.MinValue;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            steam.RequireAccount(authorization);

            foreach (var line in console.ReadNewLines())
            {
                if (CompletePattern().Match(line) is { Success: true } complete &&
                    ulong.TryParse(complete.Groups["manifest"].Value, out var finished) &&
                    finished == manifestId)
                {
                    if (!SafePaths.Same(complete.Groups["dir"].Value, staging))
                        throw new SteamDownloadException("Steam reported an unexpected download folder; no files were moved.");
                    SafePaths.NoLinks(staging);
                    steam.RequireAccount(authorization);
                    reporter.Progress(new StepProgress($"Downloading {label}", 1, 1, "complete"));
                    reporter.Log($"Steam finished downloading {label}.", LogLevel.Good);
                    return staging;
                }

                if (FailedPattern().Match(line) is { Success: true } failed)
                    throw new SteamDownloadException(Explain(failed.Groups["reason"].Value.Trim()));

                if (StartedPattern().Match(line) is { Success: true } begun &&
                    uint.TryParse(begun.Groups["depot"].Value, out var begunDepot) && begunDepot == depotId)
                {
                    acknowledged = true;
                    lastChange = DateTime.UtcNow;
                    reporter.Log(
                        $"Steam is downloading {label}: {begun.Groups["files"].Value} files, "
                        + $"{begun.Groups["mb"].Value} MB.", LogLevel.Info);
                }
            }

            foreach (var line in content.ReadNewLines())
            {
                if (TransferTotalPattern().Match(line) is { Success: true } stage &&
                    uint.TryParse(stage.Groups["app"].Value, out var reportedApp) && reportedApp == appId &&
                    long.TryParse(stage.Groups["total"].Value, out var total))
                {
                    transferTotal = total;
                    acknowledged = true;
                }

                if (RatePattern().Match(line) is { Success: true } rate)
                {
                    double.TryParse(rate.Groups["mbps"].Value, CultureInfo.InvariantCulture, out mbps);
                    rateObserved = DateTime.UtcNow;
                    if (mbps > 0) lastChange = DateTime.UtcNow;
                }
            }

            // Steam preallocates full-sized files before their chunks arrive. File length
            // cannot measure transferred bytes. The available log reports total size and
            // aggregate Steam activity, but no reliable per-depot completed-byte counter.
            reporter.Progress(new StepProgress(
                $"Downloading {label}",
                0,
                0,
                DescribeActivity(transferTotal, DateTime.UtcNow - rateObserved < TimeSpan.FromSeconds(90) ? mbps : 0)));

            if (!acknowledged && DateTime.UtcNow - started > AcknowledgeTimeout)
                throw new SteamDownloadException(
                    "Steam did not react to the download request. Make sure Steam is running and signed in.");

            if (acknowledged && DateTime.UtcNow - lastChange > StallTimeout)
                throw new SteamDownloadException(
                    $"The download stopped making progress for {StallTimeout.TotalMinutes:0} minutes. "
                    + "Check Steam's own downloads page, then try again.");

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static string DescribeActivity(long total, double mbps)
    {
        var size = total > 0 ? $"{Human.Bytes(total)} requested - " : "";
        var activity = mbps > 0 ? $" - Steam activity: {mbps:0} Mbps" : "";
        return size + "waiting for Steam to finish" + activity;
    }

    /// <summary>Turns Steam's terse console wording into something a player can act on.</summary>
    private static string Explain(string reason) => reason switch
    {
        var r when r.Contains("Missing decryption key", StringComparison.OrdinalIgnoreCase) =>
            $"Steam would not release these files to this account ({reason}). "
            + "The signed-in Steam account has to own KurtzPel.",
        var r when r.Contains("Manifest not available", StringComparison.OrdinalIgnoreCase) =>
            $"Steam no longer has this version available for download ({reason}).",
        var r when r.Contains("disk", StringComparison.OrdinalIgnoreCase) =>
            $"Steam ran out of room ({reason}).",
        _ => $"Steam could not download this version: {reason}",
    };

}
