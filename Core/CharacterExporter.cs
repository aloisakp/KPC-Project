using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace KpcLauncher.Core;

internal static class CharacterExporter
{
    private const string GameProcess = "TheChase-Win64-Shipping";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags,
        StringBuilder path, ref uint size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public int X, Y, Width, Height, XChars, YChars, Fill, Flags;
        public short Show, ReservedSize;
        public IntPtr ReservedData, Stdin, Stdout, Stderr;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint flags, IntPtr environment, string? directory, ref StartupInfo startup, out ProcessInformation information);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// Starts a process without the shell and without inheriting this worker's handles; returns its id.
    /// A shell launch left shell threads in the worker that crashed its exit (0xC0000005 in
    /// windows.storage.dll) on some Windows 10 builds. Process.Start would instead pass the worker's
    /// output pipes to a Steam client started here, so the launcher would wait until Steam exits.
    /// </summary>
    internal static int StartWithoutShell(string executable, params string[] arguments)
    {
        if (arguments.Any(a => a.Length == 0 || a.Any(c => c == '"' || char.IsWhiteSpace(c))))
            throw new ArgumentException("Arguments must be single unquoted words.", nameof(arguments));
        var commandLine = new StringBuilder("\"" + executable + "\"");
        foreach (var argument in arguments) commandLine.Append(' ').Append(argument);
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        const uint CreateNoWindow = 0x08000000;
        if (!CreateProcessW(executable, commandLine, IntPtr.Zero, IntPtr.Zero, false, CreateNoWindow,
                IntPtr.Zero, null, ref startup, out var information))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        CloseHandle(information.Thread);
        CloseHandle(information.Process);
        return information.ProcessId;
    }

    internal static bool MatchesLaunchedGame(Process process, string expectedExe, DateTime launchedAt)
    {
        try
        {
            if (process.HasExited) return false;
            // MainModule reads the remote loader list, which is not initialized during
            // early Steam startup (Win32 error 299). Query the OS image identity instead.
            // Keep this Process's handle for subsequent waiting and capture completion.
            var path = new StringBuilder(32768);
            var size = (uint)path.Capacity;
            if (!QueryFullProcessImageNameW(process.SafeHandle, 0, path, ref size))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return string.Equals(path.ToString(), expectedExe, StringComparison.OrdinalIgnoreCase) &&
                process.StartTime.ToUniversalTime() >= launchedAt.AddSeconds(-1);
        }
        catch (InvalidOperationException) when (process.HasExited) { return false; }
        catch (Win32Exception) when (process.HasExited) { return false; }
    }

    internal static string FindRetailExecutable(string steamExe)
    {
        var found = CharacterExportSources.FindSteam(steamExe);
        return found.Count == 1 ? found[0] : throw new IOException("Install KurtzPel in one Steam library before exporting a character.");
    }

    internal static void RequireRetailAccount(CharacterExportSource source, SteamInstall? steam, ulong steamId)
    {
        // Epic authenticates its own retail session. Community identity stays Steam.
        if (source.Platform == "steam" && steam?.ActiveSteamId != steamId)
            throw new IOException("Keep Steam signed in to the account authorized in the launcher.");
        if (source.Platform is not ("steam" or "epic")) throw new IOException("Unsupported character export source.");
    }

    public static async Task RunAsync(JsonElement request, IReadOnlyDictionary<string,byte[]> assets, CancellationToken cancellationToken)
    {
        if(request.GetProperty("operation").GetString()!="export-character") throw new IOException("Export packages cannot start tester operations.");
        var steamId=ulong.Parse(request.GetProperty("exportSteamId").GetString()!);
        var steam=SteamInstall.FindConfigured(LauncherConfig.Load());
        if (steam is { IsNativeLinux: true }) throw new IOException("Character export from a separate Proton/Wine process is not supported by the native Steam helper yet.");
        if (!SteamOpenId.IsIndividualId(steamId)) throw new IOException("Steam authorization is required.");
        var requested = request.TryGetProperty("exportSource", out var sourceJson) && sourceJson.ValueKind != JsonValueKind.Null
            ? sourceJson.Deserialize<CharacterExportSource>(TesterClient.Json) : null;
        var source = requested is null
            ? new CharacterExportSource("steam", FindRetailExecutable(steam?.Executable ?? throw new IOException("Steam could not be found. Select its folder in Settings.")))
            : CharacterExportSources.Resolve(requested, CharacterExportSources.Discover(steam));
        void RequireSourceAccount() => RequireRetailAccount(source, steam, steamId);
        RequireSourceAccount();
        var toolsRoot=request.GetProperty("toolsRoot").GetString()!;
        var storage=request.GetProperty("storageRoot").GetString()!;
        SafePaths.NoLinks(storage);
        Directory.CreateDirectory(storage);
        using var ownership=new FileStream(Path.Combine(storage,".kpc-tester-operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var reporter=new ExportReporter();
        var retailExe = source.Executable;
        var existing = Process.GetProcessesByName(GameProcess);
        try { if (existing.Length != 0) throw new IOException("Close KurtzPel before starting a character export."); }
        finally { foreach (var process in existing) process.Dispose(); }

        var outputFolder = Path.Combine(storage, "Character Exports");
        SafePaths.NoLinks(outputFolder);
        var output = Path.Combine(outputFolder, ".capture-" + Guid.NewGuid().ToString("N") + ".tmp");
        var launch = source.Platform == "steam"
            ? (Executable: steam!.Executable, Arguments: new[] { "--", "steam://rungameid/844870" })
            : CharacterExportSources.FindEpicLauncher(source.EpicAppId!);
        cancellationToken.ThrowIfCancellationRequested();
        reporter.Step("Starting KurtzPel through " + source.DisplayName);
        var startedAt = DateTime.UtcNow;
        // The same command Windows runs for a steam:// link ("steam.exe" -- "%1").
        StartWithoutShell(launch.Executable, launch.Arguments);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        Process? game = null;
        try
        {
            while (game is null)
            {
                timeout.Token.ThrowIfCancellationRequested(); RequireSourceAccount();
                var candidates = Process.GetProcessesByName(GameProcess);
                try
                {
                    foreach (var process in candidates)
                    {
                        if (MatchesLaunchedGame(process, retailExe, startedAt))
                        {
                            if (game is not null) throw new IOException("Multiple matching game processes started.");
                            game = process;
                        }
                    }
                }
                finally { foreach (var process in candidates) if (process != game) process.Dispose(); }
                if (game is null) await Task.Delay(200, timeout.Token);
            }
            RequireSourceAccount();
            reporter.Step("Waiting for the " + source.DisplayName + " game window");
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested(); game.Refresh();
                if (game.HasExited) throw new IOException(source.DisplayName + " game launch ended before its window opened. Try Export character again.");
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
            RequireSourceAccount();
            ExportFileName.Complete(output, capturedName);
            // The export is committed once the file is in place; announce it before closing the game.
            Console.WriteLine("KPC_CAPTURED " + JsonSerializer.Serialize(capturedName));
            reporter.Step("Closing the captured " + source.DisplayName + " game");
            await CloseCapturedGameAsync(game, retailExe, startedAt);
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
        if (!MatchesLaunchedGame(game, expectedExe, launchedAt))
        {
            if (game.HasExited) return;
            throw new IOException("The captured game process identity changed.");
        }
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
