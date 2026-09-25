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
public sealed partial class SteamInstall
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
    public bool IsNativeLinux => false;
    public bool IsClientRunning => IsRunning;
    public static SteamInstall? FindConfigured(LauncherConfig config) => Find(config.SteamRoot);
    internal bool MatchesStaging(string reported, uint appId, uint depotId) =>
        SafePaths.Same(reported, StagingDirectory(appId, depotId));

    public string ConsoleLog => Path.Combine(Root, "logs", "console_log.txt");
    public string ContentLog => Path.Combine(Root, "logs", "content_log.txt");

    /// <summary>
    /// Where <c>download_depot</c> stages a depot. Every manifest of a depot lands in this
    /// same directory, so each archive has to be moved out before the next one starts.
    /// </summary>
    public string StagingDirectory(uint appId, uint depotId) =>
        Path.Combine(Root, "steamapps", "content", $"app_{appId}", $"depot_{depotId}");

    public static SteamInstall? Find(string? selectedRoot = null)
    {
        // A saved choice is authoritative. If it moved, ask for a new folder rather
        // than silently using another Steam installation and its account/logs.
        if (!string.IsNullOrWhiteSpace(selectedRoot)) return FromFolder(selectedRoot);
        var root = ReadPath(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath")
                   ?? ReadPath(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
                   ?? ReadPath(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
        if (root is null) return null;

        var executable = ReadPath(Registry.CurrentUser, @"Software\Valve\Steam", "SteamExe")
                         ?? Path.Combine(root, "steam.exe");

        return File.Exists(executable) ? new SteamInstall(root, executable) : null;
    }

    public static SteamInstall? FromFolder(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        try
        {
            root = root.Replace('/', Path.DirectorySeparatorChar);
            if (!Path.IsPathFullyQualified(root)) return null;
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var executable = Path.Combine(root, "steam.exe");
            return File.Exists(executable) ? new SteamInstall(root, executable) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
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

    /// <summary>
    /// Brings Steam to a state where it can accept a download. If a sign-in is needed it is
    /// Steam's own window that asks for it - that prompt belongs to Valve, and no credential
    /// passes through this launcher on its way there.
    /// </summary>
    public async Task EnsureReadyAsync(IReporter reporter, CancellationToken cancellationToken)
    {
        if (!IsClientRunning)
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
