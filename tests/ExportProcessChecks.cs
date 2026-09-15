using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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
    }
}
