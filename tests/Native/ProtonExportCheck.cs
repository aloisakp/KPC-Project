using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KpcLauncher.Core;

/// <summary>
/// Character export through a real Proton session: the launcher's runtime and worker host, the game
/// worker, Proton's Steam helper, a stand-in game and a stand-in listener that reads the game's memory.
/// tests/linux-export-check.sh prepares it. No Steam account, retail game, server or private package is used.
/// </summary>
internal static class ProtonExportCheck
{
    private const ulong Account = SteamOpenId.IndividualBase + 123;

    public static async Task Run(string work, string steamRoot, string worker, string listener, string keyFile)
    {
        var count = 0;
        void Check(bool passed, string message) { if (!passed) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); count++; }
        var connectionLog = Path.Combine(steamRoot, "logs", "connection_log.txt");
        void SignIn(uint id) => File.AppendAllText(connectionLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Logged On, 4, 7] [U:1:{id}] test\n");
        var runtimeRoot = Path.Combine(work, "runtime");
        async Task EndSession()
        {
            var wineserver = Directory.GetDirectories(Path.Combine(steamRoot, "steamapps", "common"), "Proton*")
                .Select(p => Path.Combine(p, "files", "bin", "wineserver")).First(File.Exists);
            using var kill = Process.Start(new ProcessStartInfo(wineserver, "-k") { Environment = { ["WINEPREFIX"] = Path.Combine(runtimeRoot, "pfx") } })!;
            await kill.WaitForExitAsync();
        }
        // A stand-in for the native Steam client process and its signed-in account.
        using var steamProcess = Process.Start(new ProcessStartInfo(Path.Combine(steamRoot, "ubuntu12_32", "steam")) { ArgumentList = { "3600" } })!;
        try
        {
            SignIn(123);
            var steam = SteamInstall.FromFolder(steamRoot) ?? throw new Exception("The Steam fixture is invalid.");
            Check(steam.ActiveSteamId == Account, "stand-in Steam client is signed in to the authorized account");
            var runtime = new GameRuntime(steam, runtimeRoot, worker);
            Check(runtime.IsProton, "the game runtime uses Proton from the Steam library");
            runtime.RequireExport();
            Check(true, "export preflight accepts Proton, the Steam client library and no running game");
            var sources = CharacterExportSources.Discover(steam);
            Check(sources.Count == 1 && sources[0].Executable == Path.Combine(steamRoot, "steamapps", "common", "KurtzPel", CharacterExportSources.GameRelativePath),
                "the retail copy is discovered in the native Steam library");

            // Stand-in export packages, signed with the key the worker under test trusts.
            var tools = Path.Combine(work, "tools");
            Directory.CreateDirectory(Path.Combine(tools, "client", "node_modules"));
            File.Copy(listener, Path.Combine(tools, "client", "node.exe"), true);
            var instructions = Zip(("assets/memory-node.cjs", "// stand-in loader"), ("assets/tools/character-export/run.js", "// stand-in runner"));
            using var key = RSA.Create();
            key.ImportFromPem(File.ReadAllText(keyFile));
            var release = new TesterRelease(1, "linux-export-check", "export", "export", "0.8.0", [
                new("instructions", "instructions.zip", Hash(instructions), instructions.Length, null),
                new("tools", "tools.zip", Hash(File.ReadAllBytes(listener)), new FileInfo(listener).Length,
                    [new("client/node.exe", Hash(File.ReadAllBytes(listener)), new FileInfo(listener).Length)])], 0, 1);
            var payload = JsonSerializer.SerializeToUtf8Bytes(release, TesterClient.Json);
            var envelope = new SignedRelease(Convert.ToBase64String(payload),
                Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
            var verified = TesterPackageHost.VerifyRelease(envelope, key.ExportSubjectPublicKeyInfoPem());
            var storage = Path.Combine(work, "storage");
            var exports = Path.Combine(storage, "Character Exports");
            Task<string?> Export(Reporter reporter, CancellationToken ct) => TesterPackageHost.HostAsync(runtime, envelope, verified,
                instructions.ToArray(), null, "export-character", storage, tools, reporter, ct, Account, sources[0]);

            var report = Path.Combine(work, "game-report.txt");
            Environment.SetEnvironmentVariable("KPC_EXPORT_CHECK_REPORT", await runtime.MapAsync(report, default));
            var first = new Reporter();
            Check(await Export(first, default) == "Aloisa Test", "export captures the character the listener read from the game's memory");
            var saved = Path.Combine(exports, "Aloisa Test.kpc-character.json");
            Check(File.Exists(saved) && File.ReadAllText(saved).Contains("\"name\":\"Aloisa Test\""), "the export is saved under the character's name");
            Check(!Directory.EnumerateFiles(exports, ".capture-*").Any(), "no temporary capture remains");
            Check(first.Steps.Contains("Starting KurtzPel through Proton") && first.Steps.Contains("Listening to the game") &&
                first.Steps.Contains("Closing the captured Steam game"), "worker and listener progress reaches the launcher");
            var game = File.ReadAllLines(report).Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
            Check(game["SteamAppId"] == "844870" && game["SteamGameId"] == "844870" && game["SteamOverlayGameId"] == "844870",
                "the game starts with KurtzPel's Steam app identity");
            Check(game["steamIpc"] == "1" && game["steamPid"] != "0", "Proton's Steam helper registers the Steam client for the game");
            Check(game["cwd"].EndsWith(@"\steamapps\common\KurtzPel", StringComparison.OrdinalIgnoreCase), "the game starts in its install folder, as Steam starts it");
            Check(game["KPC_WORKER_TOKEN"].Length == 0, "the game does not inherit the worker channel capability");
            Check(await Until(() => !GameRuntime.RetailGameRunning()), "the captured game is closed");

            // The account changes while the listener captures: nothing is saved and the game stays open, as on Windows.
            Environment.SetEnvironmentVariable("KPC_EXPORT_CHECK_DELAY_MS", "8000");
            var second = new Reporter();
            var switched = Export(second, default);
            Check(await Until(() => second.Steps.Contains("Listening to the game")), "the listener attaches to a second export");
            Check(GameRuntime.RetailGameRunning(), "a game started in the runtime is seen as running KurtzPel");
            SignIn(456);
            Check(await Fails<TesterException>(switched) && second.Errors.Any(e => e.Contains("Keep Steam signed in")),
                "a Steam account switch during capture fails the export before it is saved");
            Check(Directory.GetFiles(exports, "*.kpc-character.json").Length == 1 && !Directory.EnumerateFiles(exports, ".capture-*").Any(),
                "no capture from another account is saved");
            Check(GameRuntime.RetailGameRunning(), "the game stays open after a failed capture");
            Check(Throws(runtime.RequireExport, "Close KurtzPel"), "a running KurtzPel blocks another export");
            await EndSession();
            Check(await Until(() => !GameRuntime.RetailGameRunning()), "test session ended");

            // Cancel while the listener waits for the lobby: the worker stops its listener and the game stays open.
            SignIn(123);
            Environment.SetEnvironmentVariable("KPC_EXPORT_CHECK_DELAY_MS", "120000");
            using var cancel = new CancellationTokenSource();
            var third = new Reporter();
            var cancelled = Export(third, cancel.Token);
            Check(await Until(() => third.Steps.Contains("Listening to the game")), "the listener attaches to a third export");
            var stopwatch = Stopwatch.StartNew();
            cancel.Cancel();
            Check(await Fails<OperationCanceledException>(cancelled) && stopwatch.Elapsed < TimeSpan.FromSeconds(20), "cancel ends the export promptly");
            Check(await Until(() => !ProcessRunning(@"\client\node.exe")), "cancel stops the listener inside the Proton session");
            Check(GameRuntime.RetailGameRunning(), "cancel leaves the game open");
            Check(Directory.GetFiles(exports, "*.kpc-character.json").Length == 1, "a cancelled export saves nothing");
            Console.WriteLine($"All {count} Proton export checks passed.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KPC_EXPORT_CHECK_DELAY_MS", null);
            await EndSession();
            steamProcess.Kill();
        }
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var (name, text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        return stream.ToArray();
    }

    private static async Task<bool> Until(Func<bool> condition)
    {
        for (var deadline = DateTime.UtcNow.AddMinutes(3); DateTime.UtcNow < deadline; await Task.Delay(250))
            if (condition()) return true;
        return false;
    }

    private static async Task<bool> Fails<T>(Task task) where T : Exception
    {
        try { await task; return false; }
        catch (T) { return true; }
    }

    private static bool Throws(Action action, string message)
    {
        try { action(); return false; }
        catch (TesterException ex) { return ex.Message.Contains(message); }
    }

    private static bool ProcessRunning(string commandPart) => Directory.EnumerateDirectories("/proc").Any(folder =>
    {
        try { return File.ReadAllText(Path.Combine(folder, "cmdline")).Contains(commandPart, StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    });

    private sealed class Reporter : IReporter
    {
        private readonly ConcurrentQueue<string> steps = new(), errors = new();
        public IReadOnlyCollection<string> Steps => steps;
        public IReadOnlyCollection<string> Errors => errors;
        public void Log(string text, LogLevel level = LogLevel.Info) { if (level == LogLevel.Error) errors.Enqueue(text); Console.WriteLine("  log: " + text); }
        public void Step(string name) { steps.Enqueue(name); Console.WriteLine("  step: " + name); }
        public void Progress(StepProgress progress) { }
    }
}
