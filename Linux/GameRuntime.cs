using System.Diagnostics;
using System.Text.RegularExpressions;

namespace KpcLauncher.Core;

internal sealed class GameRuntime
{
    private readonly string executable;
    private readonly bool proton;
    private readonly string steamRoot;
    public GameRuntime()
    {
        var steam = SteamInstall.FindConfigured(LauncherConfig.Load()) ?? throw new TesterException("Select Steam's folder first.");
        steamRoot = steam.Root;
        var libraries = new HashSet<string>(StringComparer.Ordinal) { steamRoot };
        var file = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(file) && new FileInfo(file).Length < 1024 * 1024)
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "\"path\"\\s+\"([^\"]+)\""))
                if (Path.IsPathFullyQualified(match.Groups[1].Value)) libraries.Add(match.Groups[1].Value);
        executable = libraries.Select(l => Path.Combine(l, "steamapps", "common")).Where(Directory.Exists)
            .SelectMany(p => Directory.EnumerateDirectories(p, "Proton*")).OrderByDescending(p => Path.GetFileName(p), StringComparer.Ordinal)
            .Select(p => Path.Combine(p, "proton")).FirstOrDefault(File.Exists) ?? "";
        proton = executable.Length > 0;
        if (!proton) executable = SteamInstall.FindCommand("wine") ?? SteamInstall.FindCommand("wine64") ?? "";
        if (executable.Length == 0) throw new TesterException("Install Proton through Steam: Library → Tools → Proton Experimental. Then retry. The Linux launcher itself does not need Wine.");
    }
    private ProcessStartInfo Start(params string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        var root = Path.Combine(LauncherConfig.AppDataDir, "game-runtime");
        SafePaths.NoLinks(root); Directory.CreateDirectory(root);
        if (proton)
        {
            start.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = steamRoot;
            start.Environment["STEAM_COMPAT_DATA_PATH"] = root;
            start.ArgumentList.Add("run");
        }
        else start.Environment["WINEPREFIX"] = Path.Combine(root, "wine");
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return start;
    }
    public async Task<string> MapAsync(string path, CancellationToken ct)
    {
        using var process = Process.Start(Start("winepath.exe", "-w", Path.GetFullPath(path))) ?? throw new TesterException("Could not initialize the game runtime.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using var cancel = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var result = (await output).Trim(); await error;
        if (process.ExitCode != 0 || result.Length < 3 || result[1] != ':' || result.Any(char.IsControl))
            throw new TesterException("The game runtime could not access the selected folder. Choose a local writable folder and retry.");
        return result;
    }
    public ProcessStartInfo Worker()
    {
        var worker = Path.Combine(AppContext.BaseDirectory, "game-worker", "KpcGameWorker.exe");
        if (!File.Exists(worker)) throw new TesterException("Game tools are missing. Reinstall the Linux launcher.");
        return Start(worker, "--tester-worker");
    }
}
