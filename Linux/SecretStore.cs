using System.Diagnostics;

namespace KpcLauncher.Core;

/// <summary>Use the desktop keyring when available; otherwise keep login in memory only.</summary>
internal static class SecretStore
{
    private static readonly Dictionary<string, byte[]> Memory = new();
    private static string? Run(string operation, string path, byte[]? input = null)
    {
        var executable = SteamInstall.FindCommand("secret-tool");
        if (executable is null) return null;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(operation);
        if (operation == "store") start.ArgumentList.Add("--label=KPC Launcher sign-in");
        foreach (var arg in new[] { "application", "KPCLauncher", "record", path }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (input is not null) process.StandardInput.Write(Convert.ToBase64String(input));
        process.StandardInput.Close();
        if (!process.WaitForExit(10000)) { process.Kill(); return null; }
        return process.ExitCode == 0 ? output.GetAwaiter().GetResult().Trim() : null;
    }
    public static byte[]? Read(string path)
    {
        lock (Memory)
        {
            if (Memory.TryGetValue(path, out var value)) return value.ToArray();
            if (File.Exists(path + ".forgotten")) return null;
            try
            {
                var result = Run("lookup", path);
                return string.IsNullOrEmpty(result) || result.Length > 24000 ? null : Convert.FromBase64String(result);
            }
            catch (Exception ex) when (ex is IOException or FormatException or System.ComponentModel.Win32Exception) { return null; }
        }
    }
    public static void Write(string path, byte[] plain)
    {
        lock (Memory) Memory[path] = plain.ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SafePaths.NoLinks(path + ".forgotten");
        File.WriteAllText(path + ".forgotten", "Do not restore an older keyring record.");
        try
        {
            if (Run("store", path, plain) is null) CrashLog.Write("Desktop keyring unavailable. Sign-in is remembered for this launcher session only.");
            else File.Delete(path + ".forgotten");
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { CrashLog.Write("Desktop keyring unavailable. Sign-in is remembered for this launcher session only."); }
    }
    public static void Delete(string path)
    {
        lock (Memory) { if (Memory.Remove(path, out var value)) System.Security.Cryptography.CryptographicOperations.ZeroMemory(value); }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SafePaths.NoLinks(path + ".forgotten");
        File.WriteAllText(path + ".forgotten", "This sign-in was disconnected.");
        try { Run("clear", path); } catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { }
    }
}
