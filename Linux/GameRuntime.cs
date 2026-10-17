using System.Diagnostics;
using System.Text.RegularExpressions;

namespace KpcLauncher.Core;

internal sealed class GameRuntime
{
    // Wine writes a Windows process's command line over its argv. Proton games share the host's process IDs.
    private static readonly Regex GameCommand = new(@"(^|[\\/])TheChase-Win64-Shipping\.exe(""|\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly string executable;
    private readonly bool proton;
    private readonly string steamRoot;
    private readonly string libraryPaths;
    private readonly string root;
    private readonly string worker;
    private bool prepared;
    public GameRuntime() : this(SteamInstall.FindConfigured(LauncherConfig.Load()) ?? throw new TesterException("Select Steam's folder first.")) { }
    internal GameRuntime(SteamInstall steam, string? root = null, string? worker = null)
    {
        Steam = steam;
        steamRoot = steam.Root;
        var libraries = new HashSet<string>(StringComparer.Ordinal) { steamRoot };
        var file = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(file) && new FileInfo(file).Length < 1024 * 1024)
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "\"path\"\\s+\"([^\"]+)\""))
                if (Path.IsPathFullyQualified(match.Groups[1].Value)) libraries.Add(match.Groups[1].Value);
        libraryPaths = string.Join(':', libraries);
        executable = libraries.Select(l => Path.Combine(l, "steamapps", "common")).Where(Directory.Exists)
            .SelectMany(p => Directory.EnumerateDirectories(p, "Proton*")).OrderByDescending(p => Path.GetFileName(p), StringComparer.Ordinal)
            .Select(p => Path.Combine(p, "proton")).FirstOrDefault(File.Exists) ?? "";
        proton = executable.Length > 0;
        if (!proton) executable = SteamInstall.FindCommand("wine") ?? SteamInstall.FindCommand("wine64") ?? "";
        if (executable.Length == 0) throw new TesterException("Install Proton through Steam: Library → Tools → Proton Experimental. Then retry. The Linux launcher itself does not need Wine.");
        this.root = root ?? Path.Combine(LauncherConfig.AppDataDir, "game-runtime");
        this.worker = worker ?? Path.Combine(AppContext.BaseDirectory, "game-worker", "KpcGameWorker.exe");
    }
    public SteamInstall Steam { get; }
    public bool IsProton => proton;
    /// <summary>The Proton build or Wine command this runtime uses, for the log.</summary>
    public string Description
    {
        get
        {
            if (!proton) return executable;
            var folder = Path.GetDirectoryName(executable)!;
            try
            {
                // Proton records its build in its "version" file.
                var version = File.ReadLines(Path.Combine(folder, "version")).FirstOrDefault()?.Trim();
                if (version is { Length: > 0 and <= 100 } && !version.Any(char.IsControl)) return folder + " (" + version + ")";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return folder;
        }
    }
    // Proton's run verb starts a program through its Steam helper, which leaves the program no usable
    // stdout or stderr. getcompatpath prints a translated path; runinprefix keeps the worker's stdio.
    private ProcessStartInfo Start(string? verb, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        SafePaths.NoLinks(root); Directory.CreateDirectory(root);
        if (proton)
        {
            start.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = steamRoot;
            start.Environment["STEAM_COMPAT_DATA_PATH"] = root;
            // Proton's Steam helper lists these libraries for a game it starts, as Steam does.
            start.Environment["STEAM_COMPAT_LIBRARY_PATHS"] = libraryPaths;
            start.ArgumentList.Add(verb!);
        }
        else start.Environment["WINEPREFIX"] = Path.Combine(root, "wine");
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return start;
    }
    public async Task<string> MapAsync(string path, CancellationToken ct)
    {
        var full = Path.GetFullPath(path);
        // getcompatpath also creates or updates Proton's prefix, which takes longer the first time.
        using var process = Process.Start(proton ? Start("getcompatpath", full) : Start(null, "winepath.exe", "-w", full)) ?? throw new TesterException("Could not initialize the game runtime.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var cancel = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var result = (await output).Trim(); await error;
        if (process.ExitCode != 0 || result.Length < 3 || result[1] != ':' || result.Any(char.IsControl))
            throw new TesterException("The game runtime could not access the selected folder. Choose a local writable folder and retry.");
        prepared = true;
        return result;
    }
    public ProcessStartInfo Worker()
    {
        if (!File.Exists(worker)) throw new TesterException("Game tools are missing. Reinstall the Linux launcher.");
        // runinprefix skips Proton's prefix setup, which MapAsync has run for this runtime.
        if (proton && !prepared) throw new TesterException("The game runtime was not prepared.");
        return Start("runinprefix", worker, "--tester-worker");
    }
    /// <summary>
    /// Export starts the retail game in this runtime through Proton's Steam helper, which connects it to
    /// the native Steam client. Plain Wine has no such bridge, and a copy started elsewhere is out of reach.
    /// </summary>
    public void RequireExport()
    {
        if (!proton) throw new TesterException("Character export on Linux needs Proton. In Steam, install Proton Experimental from Library → Tools, then try again.");
        if (Steam.IsFlatpak) throw new TesterException("Character export does not support Flatpak Steam yet. Existing exports can still be imported.");
        if (!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam", "sdk64", "steamclient.so")))
            throw new TesterException("Proton cannot reach Steam because ~/.steam/sdk64/steamclient.so is missing. Restart Steam, then try again.");
        if (RetailGameRunning()) throw new TesterException("Close KurtzPel before starting a character export.");
    }
    internal static bool RetailGameRunning(string proc = "/proc")
    {
        foreach (var folder in Directory.EnumerateDirectories(proc))
        {
            if (!int.TryParse(Path.GetFileName(folder), out _)) continue;
            try
            {
                var uid = File.ReadLines(Path.Combine(folder, "status")).FirstOrDefault(l => l.StartsWith("Uid:", StringComparison.Ordinal))?
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (uid is not { Length: > 1 } || uid[1] != SteamInstall.CurrentUserId) continue;
                if (IsRetailGameCommand(File.ReadAllText(Path.Combine(folder, "cmdline")))) return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return false;
    }
    internal static bool IsRetailGameCommand(string commandLine) => GameCommand.IsMatch(commandLine.Split('\0')[0]);
}
