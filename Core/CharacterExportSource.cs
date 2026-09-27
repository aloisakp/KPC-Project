using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
#if !LINUX
using Microsoft.Win32;
#endif

namespace KpcLauncher.Core;

/// <summary>A retail export source, not a community account or download provider.</summary>
public sealed record CharacterExportSource(string Platform, string Executable, string? EpicAppId = null)
{
    public string DisplayName => Platform == "steam" ? "Steam" : "Epic Games";
}

internal static class CharacterExportSources
{
    internal const string GameProcess = "TheChase-Win64-Shipping";
    internal static readonly string GameRelativePath = Path.Combine("TheChase", "Binaries", "Win64", GameProcess + ".exe");

    internal static IReadOnlyList<CharacterExportSource> Discover(SteamInstall? steam)
    {
#if LINUX
        return [];
#else
        return Discover(steam is { IsNativeLinux: false } ? steam.Executable : null, EpicManifestDirectories());
#endif
    }

    internal static IReadOnlyList<CharacterExportSource> Discover(string? steamExe, IEnumerable<string> manifestDirectories)
    {
        var found = new List<CharacterExportSource>();
        if (steamExe is not null)
        {
            try { found.AddRange(FindSteam(steamExe).Select(exe => new CharacterExportSource("steam", exe))); }
            catch (Exception ex) when (InvalidInstallation(ex)) { }
        }
        foreach (var directory in manifestDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(directory)) continue;
                foreach (var file in Directory.EnumerateFiles(directory, "*.item"))
                {
                    try
                    {
                        if (new FileInfo(file).Length > 1024 * 1024) continue;
                        var source = ReadEpicManifest(File.ReadAllText(file));
                        if (source is not null && !found.Contains(source)) found.Add(source);
                    }
                    catch (Exception ex) when (InvalidInstallation(ex)) { }
                }
            }
            catch (Exception ex) when (InvalidInstallation(ex)) { }
        }
        return found;
    }

    private static bool InvalidInstallation(Exception ex) => ex is IOException or UnauthorizedAccessException
        or JsonException or ArgumentException or NotSupportedException;

    internal static CharacterExportSource? ReadEpicManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (!string.Equals(Text("DisplayName"), "KurtzPel", StringComparison.OrdinalIgnoreCase) ||
            !root.TryGetProperty("bIsIncompleteInstall", out var incomplete) || incomplete.ValueKind != JsonValueKind.False)
            return null;
        var app = Text("AppName");
        var main = Text("MainGameAppName");
        if (!ValidId(app) || (!string.IsNullOrEmpty(main) && main != app)) return null;
        var location = Text("InstallLocation");
        if (string.IsNullOrWhiteSpace(location) || !Path.IsPathFullyQualified(location)) return null;
        var executable = Path.GetFullPath(Path.Combine(location, GameRelativePath));
        if (!File.Exists(executable)) return null;
        var catalogNamespace = Text("CatalogNamespace");
        var catalogItem = Text("CatalogItemId");
        // Use Epic's full application identity where available; never forward LaunchCommand or tokens.
        var id = app!;
        if (!string.IsNullOrEmpty(catalogNamespace) || !string.IsNullOrEmpty(catalogItem))
        {
            if (!ValidId(catalogNamespace) || !ValidId(catalogItem)) return null;
            id = catalogNamespace + ":" + catalogItem + ":" + app;
        }
        return new("epic", executable, id);
    }

    private static bool ValidId(string? id) => id is { Length: > 0 and <= 200 } &&
        Regex.IsMatch(id, "\\A[A-Za-z0-9_-]+\\z");

    internal static string EpicLaunchUri(string appId)
    {
        var parts = appId.Split(':');
        if (parts.Length is not (1 or 3) || parts.Any(part => !ValidId(part)))
            throw new IOException("Epic's game identifier is invalid. Verify KurtzPel in Epic Games and try again.");
        return "com.epicgames.launcher://apps/" + Uri.EscapeDataString(appId) + "?action=launch&silent=true";
    }

    internal static CharacterExportSource Resolve(CharacterExportSource requested, IReadOnlyList<CharacterExportSource> detected)
    {
        var matches = detected.Where(source => source.Platform == requested.Platform &&
            string.Equals(source.Executable, requested.Executable, StringComparison.OrdinalIgnoreCase) &&
            source.EpicAppId == requested.EpicAppId).ToArray();
        return matches.Length == 1 ? matches[0] : throw new IOException(
            "The selected KurtzPel installation changed or is no longer available. Start Export character again.");
    }

    internal static IReadOnlyList<string> FindSteam(string steamExe)
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
            var exe = Path.Combine(library, "steamapps", "common", folder, GameRelativePath);
            if (File.Exists(exe)) found.Add(Path.GetFullPath(exe));
        }
        return found;
    }

#if !LINUX
    private static IEnumerable<string> EpicManifestDirectories()
    {
        var paths = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests") };
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = machine.OpenSubKey(@"SOFTWARE\Epic Games\EpicGamesLauncher");
                if (key?.GetValue("AppDataPath") is string path && Path.IsPathFullyQualified(path))
                    paths.Add(Path.Combine(path, "Manifests"));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        return paths;
    }

    internal static (string Executable, string[] Arguments) FindEpicLauncher(string appId)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(@"com.epicgames.launcher\shell\open\command");
        return ParseEpicLauncherCommand(key?.GetValue(null) as string, appId);
    }

    internal static (string Executable, string[] Arguments) ParseEpicLauncherCommand(string? command, string appId)
    {
        // Read the executable only. Do not run a registry command through a shell or inherit its arguments.
        var match = Regex.Match(command ?? "", "\\A\\s*\"(?<exe>[^\"]+)\"(?:\\s|$)");
        if (!match.Success) match = Regex.Match(command ?? "", "\\A(?<exe>[^\\s\"]+)(?:\\s|$)");
        var executable = match.Groups["exe"].Value;
        if (!Path.IsPathFullyQualified(executable) ||
            !string.Equals(Path.GetFileName(executable), "EpicGamesLauncher.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(executable))
            throw new IOException("Epic Games Launcher could not be found. Open or repair Epic Games Launcher, then try Export character again.");
        var uri = EpicLaunchUri(appId);
        var tail = command![match.Length..].Trim();
        string[] arguments = tail switch
        {
            "\"%1\"" or "%1" => [uri],
            "-uri=\"%1\"" or "-uri=%1" => ["-uri=" + uri],
            "-uri \"%1\"" or "-uri %1" => ["-uri", uri],
            _ => throw new IOException("Epic's registered launch command is unsupported. Open or repair Epic Games Launcher, then try Export character again."),
        };
        return (Path.GetFullPath(executable), arguments);
    }
#endif
}
