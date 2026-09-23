using System.IO;
using System.Text.Json;

namespace KpcLauncher.Core;

/// <summary>Explicit local sandbox ownership; no fallback to installed-launcher state.</summary>
public static class RelayDevelopment
{
    public const string RootVariable = "KP_RELAY_SANDBOX_ROOT";
    public const string MarkerName = ".relay-sandbox-owner.json";
    public const string Baseline = "23bbacb12f906a6d57ee0abcb80e02df901ed8b4";
    public static bool SplitServer => Environment.GetEnvironmentVariable("KP_RELAY_SPLIT_MODE") switch
    {
        null or "" or "0" => false,
        "1" => true,
        _ => throw new InvalidOperationException("KP_RELAY_SPLIT_MODE must be 0 or 1.")
    };
    public static string StateRoot => RequireRoot(Environment.GetEnvironmentVariable(RootVariable));

    public static string RequireRoot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw new InvalidOperationException("Start this development build with Start-RelayDevelopment.ps1; an explicit sandbox root is required.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        if (root.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A drive root cannot be a relay sandbox.");
        for (var folder = new DirectoryInfo(root); folder is not null; folder = folder.Parent)
            if (folder.Exists && (folder.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The relay sandbox must not contain directory redirects.");
        var marker = Path.Combine(root, MarkerName);
        if (!File.Exists(marker) || (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The selected directory is not an owned relay sandbox.");
        using var document = JsonDocument.Parse(File.ReadAllText(marker));
        var owner = document.RootElement;
        if (owner.GetProperty("schema").GetString() != "kp-relay-sandbox/v1" ||
            owner.GetProperty("baseline").GetString() != Baseline ||
            !string.Equals(owner.GetProperty("root").GetString(), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Relay sandbox ownership does not match this directory and baseline.");
        return root;
    }
}
