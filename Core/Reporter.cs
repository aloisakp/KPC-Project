namespace KpcLauncher.Core;

public enum LogLevel { Info, Good, Warn, Error, Dim }

public sealed record LogLine(LogLevel Level, string Text);

/// <summary>
/// Progress for one long-running step. <see cref="Total"/> of 0 means indeterminate.
/// </summary>
public sealed record StepProgress(string Step, long Done, long Total, string Detail)
{
    public double Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : 0;
}

/// <summary>
/// The pipeline's only channel back to the UI. Implementations marshal to the dispatcher.
/// </summary>
public interface IReporter
{
    void Log(string text, LogLevel level = LogLevel.Info);
    void Progress(StepProgress progress);
    void Step(string name);
}

public static class Human
{
    public static string Bytes(long n)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = n;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{n} B" : $"{v:0.##} {units[u]}";
    }

    public static string Rate(double bytesPerSecond) =>
        bytesPerSecond <= 0 ? "--" : $"{Bytes((long)bytesPerSecond)}/s";

    public static string Eta(long remaining, double bytesPerSecond)
    {
        if (bytesPerSecond <= 0 || remaining <= 0) return "--";
        var t = TimeSpan.FromSeconds(remaining / bytesPerSecond);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes:00}m {t.Seconds:00}s";
    }
}


