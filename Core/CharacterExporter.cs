using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KpcLauncher.Core;

internal static class CharacterExporter
{
    private const string GameProcess = "TheChase-Win64-Shipping";

    internal static string FindRetailExecutable(string steamExe)
    {
        var steamRoot = Path.GetDirectoryName(Path.GetFullPath(steamExe))!;
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamRoot };
        var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFile))
        {
            if (new FileInfo(libraryFile).Length > 1024 * 1024) throw new IOException("Steam's library list is too large.");
            foreach (Match match in Regex.Matches(File.ReadAllText(libraryFile), "\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(Path.GetFullPath(match.Groups[1].Value.Replace("\\\\", "\\")));
        }
        var found = new List<string>();
        foreach (var library in libraries)
        {
            var manifest = Path.Combine(library, "steamapps", "appmanifest_844870.acf");
            if (!File.Exists(manifest)) continue;
            if (new FileInfo(manifest).Length > 1024 * 1024) throw new IOException("Steam's game manifest is too large.");
            var text = File.ReadAllText(manifest);
            if (!Regex.IsMatch(text, "\"appid\"\\s+\"844870\"")) continue;
            var install = Regex.Match(text, "\"installdir\"\\s+\"([^\"]+)\"");
            if (!install.Success) continue;
            var folder = install.Groups[1].Value;
            if (folder is "." or ".." || folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new IOException("Invalid Steam install folder.");
            var exe = Path.Combine(library, "steamapps", "common", folder, "TheChase", "Binaries", "Win64", GameProcess + ".exe");
            if (File.Exists(exe)) found.Add(Path.GetFullPath(exe));
        }
        return found.Count == 1 ? found[0] : throw new IOException("Install KurtzPel in one Steam library before exporting a character.");
    }

    public static async Task RunAsync(JsonElement request, IReadOnlyDictionary<string,byte[]> assets, CancellationToken cancellationToken)
    {
        if(request.GetProperty("operation").GetString()!="export-character") throw new IOException("Export packages cannot start tester operations.");
        var steamId=ulong.Parse(request.GetProperty("exportSteamId").GetString()!);
        var steam=SteamInstall.Find() ?? throw new IOException("Steam is not installed.");
        void RequireSteamAccount(ulong expected) { if(steam.ActiveSteamId!=expected) throw new IOException("Keep Steam signed in to the account authorized in the launcher."); }
        RequireSteamAccount(steamId);
        var toolsRoot=request.GetProperty("toolsRoot").GetString()!;
        var storage=request.GetProperty("storageRoot").GetString()!;
        SafePaths.NoLinks(storage);
        Directory.CreateDirectory(storage);
        using var ownership=new FileStream(Path.Combine(storage,".kpc-tester-operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var reporter=new ExportReporter();
        var retailExe = FindRetailExecutable(steam.Executable);
        var existing = Process.GetProcessesByName(GameProcess);
        try { if (existing.Length != 0) throw new IOException("Close KurtzPel before starting a character export."); }
        finally { foreach (var process in existing) process.Dispose(); }

        var outputFolder = Path.Combine(storage, "Character Exports");
        SafePaths.NoLinks(outputFolder);
        var output = Path.Combine(outputFolder, ".capture-" + Guid.NewGuid().ToString("N") + ".tmp");
        reporter.Step("Starting KurtzPel through your Steam library");
        var startedAt = DateTime.UtcNow;
        Process.Start(new ProcessStartInfo("steam://rungameid/844870") { UseShellExecute = true })?.Dispose();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        Process? game = null;
        try
        {
            while (game is null)
            {
                timeout.Token.ThrowIfCancellationRequested(); RequireSteamAccount(steamId);
                foreach (var process in Process.GetProcessesByName(GameProcess))
                {
                    bool keep = false;
                    try
                    {
                        if (string.Equals(process.MainModule?.FileName, retailExe, StringComparison.OrdinalIgnoreCase) &&
                            process.StartTime.ToUniversalTime() >= startedAt.AddSeconds(-1))
                        {
                            if (game is not null) throw new IOException("Multiple Steam game processes started.");
                            game = process; keep = true;
                        }
                    }
                    finally { if (!keep) process.Dispose(); }
                }
                if (game is null) await Task.Delay(200, timeout.Token);
            }
            RequireSteamAccount(steamId);
            reporter.Step("Waiting for the Steam game window");
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested(); game.Refresh();
                if (game.HasExited) throw new IOException("Steam's game launch ended before its window opened. Try Export character again.");
                if (game.MainWindowHandle != IntPtr.Zero) break;
                await Task.Delay(200, timeout.Token);
            }
            var runner = Path.Combine(toolsRoot, "export", "tools", "character-export", "run.js");
            var start = new ProcessStartInfo(Path.Combine(toolsRoot, "client", "node.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.ArgumentList.Add(runner); start.ArgumentList.Add(game.Id.ToString()); start.ArgumentList.Add(output);
            using var listener = StartListener(start,toolsRoot,assets) ?? throw new IOException("Could not start the character listener.");
            string? capturedName = null;
            using var cancel = cancellationToken.Register(() => { try { listener.Kill(true); } catch (InvalidOperationException) { } });
            async Task Pump(StreamReader reader)
            {
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    if (line.StartsWith("KPC_STATUS ", StringComparison.Ordinal)) reporter.Step(line[11..]);
                    else if (line.StartsWith("KPC_ERROR ", StringComparison.Ordinal)) reporter.Log(line[10..], LogLevel.Error);
                    else if (line.StartsWith("KPC_CAPTURED ", StringComparison.Ordinal))
                    {
                        var name = JsonSerializer.Deserialize<string>(line[13..]);
                        if (capturedName is not null || string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
                            throw new IOException("The capture completion message is invalid.");
                        capturedName = name;
                    }
                }
            }
            await Task.WhenAll(Pump(listener.StandardOutput), Pump(listener.StandardError), listener.WaitForExitAsync(cancellationToken));
            if (listener.ExitCode != 0 || !File.Exists(output) || capturedName is null) throw new IOException("Character export did not complete. Your previous exports are intact.");
            RequireSteamAccount(steamId);
            ExportFileName.Complete(output, capturedName);
            reporter.Step("Closing the captured Steam game");
            await CloseCapturedGameAsync(game, retailExe, startedAt);
            Console.WriteLine("KPC_CAPTURED " + JsonSerializer.Serialize(capturedName));
        }
        finally
        {
            game?.Dispose();
            // An interrupted capture must never be offered as an import.
            if (File.Exists(output)) File.Delete(output);
        }
    }

    internal static async Task CloseCapturedGameAsync(Process game, string expectedExe, DateTime launchedAt)
    {
        if (game.HasExited) return;
        if (!string.Equals(game.MainModule?.FileName, expectedExe, StringComparison.OrdinalIgnoreCase) ||
            game.StartTime.ToUniversalTime() < launchedAt.AddSeconds(-1))
            throw new IOException("The captured game process identity changed.");
        game.CloseMainWindow();
        using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await game.WaitForExitAsync(grace.Token); }
        catch (OperationCanceledException) when (grace.IsCancellationRequested)
        {
            if (!game.HasExited) game.Kill(entireProcessTree: false);
            await game.WaitForExitAsync();
        }
    }
    private static Process? StartListener(ProcessStartInfo start,string toolsRoot,IReadOnlyDictionary<string,byte[]> assets)
    {
        var original=start.ArgumentList.ToArray();
        start.ArgumentList.Clear();start.ArgumentList.Add("-e");
        start.ArgumentList.Add(System.Text.Encoding.UTF8.GetString(assets["assets/memory-node.cjs"]));
        start.RedirectStandardInput=true;
        start.Environment["NODE_PATH"]=Path.Combine(toolsRoot,"client","node_modules");
        start.Environment.Remove("NODE_OPTIONS");
        var sources=assets.Where(p=>p.Key.StartsWith("assets/",StringComparison.Ordinal)).ToDictionary(
            p=>Path.Combine(toolsRoot,"export",p.Key[7..].Replace('/',Path.DirectorySeparatorChar)),p=>Convert.ToBase64String(p.Value));
        var payload=JsonSerializer.SerializeToUtf8Bytes(new {args=original,sources});
        var process=Process.Start(start);
        if(process is null)return null;
        try { process.StandardInput.BaseStream.Write(BitConverter.GetBytes(payload.Length));process.StandardInput.BaseStream.Write(payload);process.StandardInput.Close();return process; }
        catch {try{process.Kill(true);}catch{}process.Dispose();throw;}
        finally {System.Security.Cryptography.CryptographicOperations.ZeroMemory(payload);}
    }
    private sealed class ExportReporter
    {
        public void Step(string text)=>Console.WriteLine("KPC_STATUS "+text);
        public void Log(string text,LogLevel level)=>Console.Error.WriteLine("KPC_ERROR "+text);
    }

}
