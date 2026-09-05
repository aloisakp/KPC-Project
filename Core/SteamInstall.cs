using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace KpcLauncher.Core;

/// <summary>
/// The installed Steam client owns every download and its credentials. Only public account
/// identity and download status are read by the launcher.
/// </summary>
public sealed class SteamInstall
{
    private readonly Func<ulong?>? _readIdentity;
    private readonly Action<uint, uint, ulong>? _download;

    internal SteamInstall(string root, string executable, Func<ulong?>? readIdentity = null,
        Action<uint, uint, ulong>? download = null)
    {
        Root = root;
        Executable = executable;
        _readIdentity = readIdentity;
        _download = download;
    }

    public string Root { get; }
    public string Executable { get; }

    public string ConsoleLog => Path.Combine(Root, "logs", "console_log.txt");
    public string ContentLog => Path.Combine(Root, "logs", "content_log.txt");

    /// <summary>
    /// Where <c>download_depot</c> stages a depot. Every manifest of a depot lands in this
    /// same directory, so each archive has to be moved out before the next one starts.
    /// </summary>
    public string StagingDirectory(uint appId, uint depotId) =>
        Path.Combine(Root, "steamapps", "content", $"app_{appId}", $"depot_{depotId}");

    public static SteamInstall? Find()
    {
        var root = ReadPath(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath")
                   ?? ReadPath(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
                   ?? ReadPath(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
        if (root is null) return null;

        var executable = ReadPath(Registry.CurrentUser, @"Software\Valve\Steam", "SteamExe")
                         ?? Path.Combine(root, "steam.exe");

        return File.Exists(executable) ? new SteamInstall(root, executable) : null;
    }

    /// <summary>Steam records its own location with forward slashes ("g:/steam").</summary>
    private static string? ReadPath(RegistryKey hive, string subKey, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey);
            if (key?.GetValue(name) as string is not { Length: > 0 } raw) return null;
            return Path.GetFullPath(raw.Replace('/', Path.DirectorySeparatorChar));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool IsRunning
    {
        get
        {
            var processes = Process.GetProcessesByName("steam");
            try { return processes.Length > 0; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }

    /// <summary>
    /// Steam writes the signed-in account id here and zeroes it on sign-out, which separates
    /// a running client from a usable one without asking Valve anything.
    /// </summary>
    public ulong? ActiveSteamId
    {
        get
        {
            if (_readIdentity is not null) return _readIdentity();
            try
            {
                var processes = Process.GetProcessesByName("steam");
                try
                {
                    var process = processes.FirstOrDefault(p => string.Equals(p.MainModule?.FileName,
                        Executable, StringComparison.OrdinalIgnoreCase));
                    if (process is null) return null;
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
                    var registryId = key?.GetValue("ActiveUser") is int user ? unchecked((uint)user) : 0;
                    var logPath = Path.Combine(Root, "logs", "connection_log.txt");
                    // New Steam clients may omit ActiveUser. Use only connection-state lines
                    // from this process lifetime, and reject disagreement or disconnected state.
                    if (File.Exists(logPath))
                    {
                        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        if (stream.Length > 512 * 1024) stream.Seek(-512 * 1024, SeekOrigin.End);
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        return ParseConnectedIdentity(reader.ReadToEnd(), process.StartTime, registryId);
                    }
                    return registryId == 0 ? null : SteamOpenId.IndividualBase + registryId;
                }
                finally { foreach (var process in processes) process.Dispose(); }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

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

    /// <summary>
    /// A cancelled launcher can leave Steam downloading. Check Steam's current-process
    /// console history before touching shared staging or issuing another request.
    /// </summary>
    public void RequireDepotIdle(uint appId, uint depotId)
    {
        // Injected test clients have no OS process; the parser is tested separately.
        if (_download is not null) return;
        var processes = Process.GetProcessesByName("steam");
        try
        {
            var process = processes.FirstOrDefault(p => string.Equals(p.MainModule?.FileName,
                Executable, StringComparison.OrdinalIgnoreCase));
            if (process is null) throw new SteamDownloadException("Steam is not running. Restart Steam and try again.");
            if (!File.Exists(ConsoleLog)) return; // First request before Steam has created this log.
            using var stream = new FileStream(ConsoleLog, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            // Read the full log: skipping a request while retaining its completion would
            // incorrectly declare a busy depot idle. Steam rotates its own logs.
            using var reader = new StreamReader(stream, Encoding.UTF8);
            if (HasPendingDepotDownload(reader, process.StartTime, appId, depotId, StagingDirectory(appId, depotId)))
                throw new SteamDownloadException("Steam is still processing an earlier depot request. " +
                    "Let it finish, or close and restart Steam, then retry Install. The staging files were left untouched.");
        }
        finally { foreach (var process in processes) process.Dispose(); }
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

    /// <summary>
    /// Brings Steam to a state where it can accept a download. If a sign-in is needed it is
    /// Steam's own window that asks for it - that prompt belongs to Valve, and no credential
    /// passes through this launcher on its way there.
    /// </summary>
    public async Task EnsureReadyAsync(IReporter reporter, CancellationToken cancellationToken)
    {
        if (!IsRunning)
        {
            reporter.Step("Starting Steam");
            reporter.Log("Steam is not running; starting it.", LogLevel.Dim);
            Launch("-silent");
        }

        if (ActiveSteamId.HasValue) return;

        reporter.Step("Waiting for Steam");
        reporter.Log("Waiting for Steam to finish signing in. If it asks you to sign in, do that "
                     + "in Steam's own window - the launcher never sees those details.", LogLevel.Info);

        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (!ActiveSteamId.HasValue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline)
                throw new SteamDownloadException("Steam did not finish signing in. Sign in to Steam and retry Install.");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        reporter.Log("Steam is signed in.", LogLevel.Good);
    }

    private void Launch(string argument)
    {
        var info = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(argument);
        Process.Start(info)?.Dispose();
    }

    /// <summary>
    /// Hands a console command to Steam on its own command line. Steam runs it whether or not
    /// the client was already up and echoes it to console_log.txt as "ExecCommandLine", which
    /// is what makes the whole transfer drivable without a console window or a pasted command.
    /// </summary>
    public void DownloadDepot(uint appId, uint depotId, ulong manifestId, SteamAuthorization authorization)
    {
        RequireAccount(authorization);
        RequireDepotIdle(appId, depotId);
        if (_download is not null) { _download(appId, depotId, manifestId); return; }
        var info = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("+download_depot");
        foreach (var argument in new ulong[] { appId, depotId, manifestId })
            info.ArgumentList.Add(Convert.ToString(argument, CultureInfo.InvariantCulture) ?? "");
        Process.Start(info)?.Dispose();
    }
}
