using System.IO;
using System.Text;

namespace KpcLauncher.Core;

/// <summary>
/// Local file log started during application startup, before the main window is created.
/// </summary>
public static class CrashLog
{
    private static readonly object Gate = new();

    public static string Path { get; } =
        System.IO.Path.Combine(LauncherConfig.AppDataDir, "preservation.log");

    /// <summary>Truncates the previous run's log so the file is always the current session.</summary>
    public static void Start()
    {
        try
        {
            Directory.CreateDirectory(LauncherConfig.AppDataDir);
            File.WriteAllText(Path,
                $"KPC Launcher log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"exe:     {Environment.ProcessPath}{Environment.NewLine}" +
                $"runtime: {Environment.Version} on {Environment.OSVersion}{Environment.NewLine}" +
                $"culture: {System.Globalization.CultureInfo.CurrentCulture.Name}{Environment.NewLine}" +
                new string('-', 72) + Environment.NewLine);
        }
        catch (Exception)
        {
            // If even this fails there is nowhere left to report it.
        }
    }

    public static void Write(string text)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(Path, $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        }
        catch (Exception) { /* logging must never throw into the caller */ }
    }

    public static void WriteException(string context, Exception? ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"!! {context}");
        for (var e = ex; e is not null; e = e.InnerException)
        {
            sb.AppendLine($"   {e.GetType().FullName}: {e.Message}");
            if (!string.IsNullOrWhiteSpace(e.StackTrace)) sb.AppendLine(e.StackTrace);
        }
        Write(sb.ToString().TrimEnd());
    }
}
