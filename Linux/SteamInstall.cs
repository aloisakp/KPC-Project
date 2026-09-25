using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace KpcLauncher.Core;

/// <summary>Native Steam integration. No Windows executable, registry or Wine path lookup.</summary>
public sealed partial class SteamInstall
{
    [DllImport("libc")] private static extern uint getuid();
    [DllImport("libc", SetLastError = true)] private static extern IntPtr realpath(string path, IntPtr buffer);
    [DllImport("libc")] private static extern void free(IntPtr memory);
    private readonly bool _flatpak;
    internal SteamInstall(string root, bool flatpak) { Root = root; _flatpak = flatpak; }
    public string Root { get; }
    public string Executable => FindCommand(_flatpak ? "flatpak" : "steam") ?? "";
    public bool IsNativeLinux => true;
    public string ConsoleLog => Path.Combine(Root, "logs", "console_log.txt");
    public string ContentLog => Path.Combine(Root, "logs", "content_log.txt");
    public string StagingDirectory(uint appId, uint depotId) => Path.Combine(Root, "steamapps", "content", $"app_{appId}", $"depot_{depotId}");
    internal bool MatchesStaging(string reported, uint appId, uint depotId) => Path.IsPathFullyQualified(reported) && SafePaths.Same(reported, StagingDirectory(appId, depotId));
    public static SteamInstall? FindConfigured(LauncherConfig config) => Find(config.SteamRoot);
    internal static string Canonical(string path)
    {
        var value = realpath(path, IntPtr.Zero);
        if (value == IntPtr.Zero) throw new IOException("The selected folder does not exist.");
        try { return Marshal.PtrToStringUTF8(value)!; }
        finally { free(value); }
    }
    internal static string? FindCommand(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':')
        .Where(Path.IsPathFullyQualified).Select(p => Path.Combine(p, name)).FirstOrDefault(p =>
            File.Exists(p) && (File.GetUnixFileMode(p) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0);
    public static SteamInstall? FromFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) return null;
        try
        {
            var root = Canonical(folder);
            if (!Directory.Exists(Path.Combine(root, "steamapps")) ||
                !new[] { "ubuntu12_32", "ubuntu12_64" }.Any(arch => IsElf(Path.Combine(root, arch, "steam")))) return null;
            SafePaths.NoLinks(Path.Combine(root, "steamapps", "content"));
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new SteamInstall(root, SafePaths.Within(root, Path.Combine(home, ".var", "app", "com.valvesoftware.Steam")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or SteamDownloadException) { return null; }
    }
    private static bool IsElf(string file)
    {
        if (!File.Exists(file)) return false;
        using var stream = File.OpenRead(file);
        Span<byte> magic = stackalloc byte[4];
        return stream.Read(magic) == 4 && magic.SequenceEqual(new byte[] { 127, 69, 76, 70 });
    }
    public static SteamInstall? Find(string? selectedRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(selectedRoot)) return FromFolder(selectedRoot);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[] { ".local/share/Steam", ".steam/steam", ".steam/root",
            ".var/app/com.valvesoftware.Steam/.local/share/Steam", ".var/app/com.valvesoftware.Steam/data/Steam" }
            .Select(p => FromFolder(Path.Combine(home, p))).OfType<SteamInstall>().DistinctBy(p => p.Root).ToArray();
        if (roots.Length == 1) return roots[0];
        var running = roots.Where(p => p.IsClientRunning).ToArray();
        return running.Length == 1 ? running[0] : null;
    }
    internal (int Pid, DateTime Started)? LiveProcess()
    {
        var found = new List<(int, DateTime)>();
        var binaries = new[] { "ubuntu12_32", "ubuntu12_64" }.Select(a => Path.Combine(Root, a, "steam")).Where(File.Exists).Select(Canonical).ToArray();
        foreach (var folder in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(folder), out var pid)) continue;
            try
            {
                var uid = File.ReadLines(Path.Combine(folder, "status")).First(l => l.StartsWith("Uid:", StringComparison.Ordinal)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (uid[1] != getuid().ToString(CultureInfo.InvariantCulture) || !binaries.Contains(Canonical(Path.Combine(folder, "exe")))) continue;
                if (File.ReadAllText(Path.Combine(folder, "cmdline")).Split('\0').Contains("-child-update-ui")) continue;
                using var process = Process.GetProcessById(pid);
                if (!process.HasExited) found.Add((pid, process.StartTime));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        if (found.Count > 1) throw new SteamDownloadException("Multiple Steam processes use this folder. Restart Steam and retry.");
        return found.Count == 1 ? found[0] : null;
    }
    public bool IsClientRunning { get { try { return LiveProcess() is not null; } catch (SteamDownloadException) { return false; } } }
    public static bool IsRunning => Find()?.IsClientRunning == true;
    public ulong? ActiveSteamId
    {
        get
        {
            try
            {
                if (LiveProcess() is not { } process) return null;
                using var stream = new FileStream(Path.Combine(Root, "logs", "connection_log.txt"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 512 * 1024) stream.Seek(-512 * 1024, SeekOrigin.End);
                using var reader = new StreamReader(stream);
                var account = ParseConnectedIdentity(reader.ReadToEnd(), process.Started, 0);
                return LiveProcess() == process ? account : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SteamDownloadException) { return null; }
        }
    }
    public void RequireDepotIdle(uint appId, uint depotId)
    {
        var process = LiveProcess() ?? throw new SteamDownloadException("Start Steam and sign in before installing.");
        if (!File.Exists(ConsoleLog)) return;
        using var reader = File.OpenText(ConsoleLog);
        if (HasPendingDepotDownload(reader, process.Started, appId, depotId, StagingDirectory(appId, depotId)))
            throw new SteamDownloadException("Steam has an unfinished download. Let it finish or restart Steam before retrying. Existing files were preserved.");
    }
    private void Launch(params string[] arguments)
    {
        if (Executable.Length == 0) throw new SteamDownloadException(_flatpak ? "Flatpak Steam is not installed. Install Steam through your software center." : "Steam is not installed. Install Steam through your software center.");
        var start = new ProcessStartInfo(Executable) { UseShellExecute = false };
        if (_flatpak) { start.ArgumentList.Add("run"); start.ArgumentList.Add("com.valvesoftware.Steam"); }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        Process.Start(start)?.Dispose();
    }
    public async Task EnsureReadyAsync(IReporter reporter, CancellationToken cancellationToken)
    {
        if (!IsClientRunning) { reporter.Step("Starting Steam"); Launch("-silent"); }
        reporter.Step("Waiting for Steam to sign in");
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (ActiveSteamId is null)
        {
            if (DateTime.UtcNow >= deadline) throw new SteamDownloadException("Sign in to Steam, then retry Install.");
            await Task.Delay(1000, cancellationToken);
        }
    }
    public void DownloadDepot(uint appId, uint depotId, ulong manifestId, SteamAuthorization authorization)
    {
        if (appId != LauncherConfig.AppId || depotId != LauncherConfig.DepotId || !LauncherConfig.RequiredArchives.Any(a => a.ManifestId == manifestId))
            throw new SteamDownloadException("Unsupported depot request.");
        RequireAccount(authorization); RequireDepotIdle(appId, depotId); RequireAccount(authorization);
        Launch("+download_depot", appId.ToString(CultureInfo.InvariantCulture), depotId.ToString(CultureInfo.InvariantCulture), manifestId.ToString(CultureInfo.InvariantCulture));
    }
}
