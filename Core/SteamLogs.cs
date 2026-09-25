using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
namespace KpcLauncher.Core;
public sealed partial class SteamInstall
{
    internal static ulong? ParseConnectedIdentity(string log, DateTime processStarted, uint registryId)
    {
        var matches = Regex.Matches(log,
            @"(?m)^\[(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\] \[(?<state>Logged On|Logged Off|Logging On|Connecting|Connected),[^\]\r\n]*\] \[U:1:(?<id>\d+)\]");
        if (matches.Count == 0) return null;
        var last = matches[^1];
        if (last.Groups["state"].Value != "Logged On" ||
            !DateTime.TryParseExact(last.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time) || time < processStarted.AddSeconds(-2) ||
            !uint.TryParse(last.Groups["id"].Value, out var accountId) || accountId == 0 ||
            registryId != 0 && registryId != accountId) return null;
        var tail = log[(last.Index + last.Length)..];
        if (tail.Contains("ConnectionDisconnected(", StringComparison.Ordinal)) return null;
        return SteamOpenId.IndividualBase + accountId;
    }

    public void RequireAccount(SteamAuthorization authorization) =>
        RequireAccount(authorization, ActiveSteamId);

    internal static void RequireAccount(SteamAuthorization authorization, ulong? activeSteamId)
    {
        if (!authorization.IsCurrent)
            throw new SteamDownloadException("Authorize your Steam account in the browser before downloading.");
        if (activeSteamId is null)
            throw new SteamDownloadException("Steam's signed-in account could not be verified. Keep Steam online, then retry.");
        if (activeSteamId != authorization.SteamId)
            throw new SteamDownloadException("Steam is using a different account from the one you authorized. " +
                "Switch accounts in Steam or use Authorize Steam in the launcher. No further download will be requested.");
    }

    internal static bool HasPendingDepotDownload(TextReader reader, DateTime processStarted,
        uint appId, uint depotId, string staging)
    {
        var pending = 0;
        var awaitingStart = false;
        var requestPattern = new Regex(@"\+download_depot\s+" + appId + @"\s+" + depotId + @"\s+\d+(?=\s|""|$)",
            RegexOptions.CultureInvariant);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length < 22 || line[0] != '[' || line[20] != ']' ||
                !DateTime.TryParseExact(line.AsSpan(1, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var at) || at < processStarted.AddSeconds(-2)) continue;
            var message = line[22..];
            if (message.StartsWith("ExecCommandLine:", StringComparison.Ordinal) && requestPattern.IsMatch(message))
            {
                pending++;
                awaitingStart = true;
            }
            else if (message.StartsWith($"Downloading depot {depotId} (", StringComparison.Ordinal))
            {
                if (!awaitingStart) pending++; // Also recognize requests entered in Steam's console.
                awaitingStart = false;
            }
            else if (Regex.Match(message, "^Depot download complete : \"(?<dir>.*)\" \\(manifest [0-9]+\\)") is { Success: true } complete &&
                SafePaths.Same(complete.Groups["dir"].Value, staging))
            {
                pending = Math.Max(0, pending - 1);
                awaitingStart = false;
            }
            // Failures omit the depot ID. Do not use an unrelated failure to declare the
            // target idle; a restart clears unresolved requests from the old process.
        }
        return pending > 0;
    }

}
