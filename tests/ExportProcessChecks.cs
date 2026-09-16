using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using KpcLauncher.Core;

internal static class ExportProcessChecks
{
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
    private struct ProcessInfo
    {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string application, StringBuilder command,
        IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags,
        IntPtr environment, string? directory, ref StartupInfo startup, out ProcessInfo info);
    [DllImport("kernel32.dll")]
    private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static async Task Run(Action<bool, string> check)
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var startedAt = DateTime.UtcNow;
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        // CREATE_SUSPENDED | CREATE_NO_WINDOW: hold our harmless child before its
        // loader initializes, reproducing the actual Steam startup failure reliably.
        if (!CreateProcessW(executable, new StringBuilder($"\"{executable}\" /d /c exit"),
            IntPtr.Zero, IntPtr.Zero, false, 0x08000004, IntPtr.Zero, null, ref startup, out var info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            using var child = Process.GetProcessById((int)info.ProcessId);
            var oldError = 0;
            try { _ = child.MainModule?.FileName; }
            catch (Win32Exception error) { oldError = error.NativeErrorCode; }
            check(oldError == 299, "real startup fixture reproduces the former partial-copy error");
            check(CharacterExporter.MatchesLaunchedGame(child, executable, startedAt),
                "export recognizes its game before the module list initializes");
            check(!CharacterExporter.MatchesLaunchedGame(child, executable + ".other", startedAt),
                "export rejects a different executable path");
            check(!CharacterExporter.MatchesLaunchedGame(child, executable, DateTime.UtcNow.AddMinutes(1)),
                "export rejects a process from before the Steam launch");
            var rejected = false;
            try { await CharacterExporter.CloseCapturedGameAsync(child, executable + ".other", startedAt); }
            catch (IOException) { rejected = true; }
            check(rejected && !child.HasExited, "capture completion leaves a mismatched process running");
            await CharacterExporter.CloseCapturedGameAsync(child, executable, startedAt);
            check(child.HasExited, "capture completion closes only its verified child");
            check(!CharacterExporter.MatchesLaunchedGame(child, executable, startedAt),
                "export ignores a candidate that has already exited");
            await CharacterExporter.CloseCapturedGameAsync(child, executable, startedAt);
        }
        finally
        {
            // These handles belong only to the child created above, even on test failure.
            TerminateProcess(info.Process, 0);
            CloseHandle(info.Thread);
            CloseHandle(info.Process);
        }

        // A Steam client started by the export can outlive the worker. It must not hold the worker's
        // output pipe open, or the launcher waits for Steam to exit before reporting the export.
        var ping = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "PING.EXE");
        string[] longLived = ["-n", "4", "127.0.0.1"];
        check(!await PipeClosesWhileChildRuns(() =>
        {
            var start = new ProcessStartInfo(ping) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in longLived) start.ArgumentList.Add(argument);
            using var child = Process.Start(start)!;
            return child.Id;
        }), "handle fixture reproduces a child holding the worker's pipe");
        check(await PipeClosesWhileChildRuns(() => CharacterExporter.StartWithoutShell(ping, longLived)),
            "Steam launch does not pass the worker's pipes to Steam");
        foreach (var unsafeArgument in new[] { "two words", "\"quoted\"", "" })
        {
            var refused = false;
            try { CharacterExporter.StartWithoutShell(ping, unsafeArgument); }
            catch (ArgumentException) { refused = true; }
            check(refused, "Steam launch refuses argument " + JsonSerializer.Serialize(unsafeArgument));
        }
        var missing = false;
        try { CharacterExporter.StartWithoutShell(ping + ".missing", "-n", "1", "127.0.0.1"); }
        catch (Win32Exception) { missing = true; }
        check(missing, "Steam launch reports a missing executable");
    }

    private static async Task<bool> PipeClosesWhileChildRuns(Func<int> start)
    {
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var id = start();
        pipe.DisposeLocalCopyOfClientHandle();
        // Anonymous pipes cannot cancel a pending read; stop the child so the read always finishes.
        var read = Task.Run(() => pipe.Read(new byte[1], 0, 1));
        var closed = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(2))) == read;
        try
        {
            using var child = Process.GetProcessById(id);
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
        }
        catch (ArgumentException) { } // already exited
        return closed && await read == 0;
    }
}
