using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KpcLauncher.Core;

public sealed record ArchiveSpec(string Label, ulong ManifestId);

public sealed class LauncherConfig
{
    public const uint AppId = 844870;
    public const uint DepotId = 844871;

    public static IReadOnlyList<ArchiveSpec> RequiredArchives { get; } =
    [
        new("Archive A", 4819182874103212568UL),
        new("Archive B", 6221929141711975568UL),
    ];

    public string StorageRoot { get; set; } = "";

    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KPCLauncher");

    private static string ConfigPath => Path.Combine(AppDataDir, "preservation-settings.json");

    public string ArchiveDirectory(ArchiveSpec archive) =>
        Path.Combine(StorageRoot, archive.ManifestId.ToString());

    public static LauncherConfig Load()
    {
        RetireLegacyEndpoints(Path.Combine(AppDataDir, "config.json"));
        LauncherConfig config;
        try
        {
            var source = File.Exists(ConfigPath) ? ConfigPath : Path.Combine(AppDataDir, "config.json");
            config = File.Exists(source)
                ? JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(source)) ?? new LauncherConfig()
                : new LauncherConfig();
        }
        catch
        {
            config = new LauncherConfig();
        }

        if (string.IsNullOrWhiteSpace(config.StorageRoot))
            config.StorageRoot = DefaultStorageRoot();

        return config;
    }

    internal static void RetireLegacyEndpoints(string path)
    {
        if (!File.Exists(path)) return;
        SafePaths.NoLinks(path);
        if (new FileInfo(path).Length > 1024 * 1024)
            throw new IOException("Legacy launcher settings are too large to migrate safely.");
        if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject settings) return;
        var removed = false;
        foreach (var key in settings.Select(p => p.Key).ToArray())
            if (key.Equals("ServerHost", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("AccountServerBaseUrl", StringComparison.OrdinalIgnoreCase))
                removed |= settings.Remove(key);
        if (!removed) return;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>
    /// Prefers the drive Steam is on. Steam always stages a depot download inside its own
    /// library, so a storage root on the same volume turns filing an archive into a rename
    /// instead of a 30 GB copy.
    /// </summary>
    private static string DefaultStorageRoot()
    {
        const string FolderName = "KPC Preservation";

        if (SteamInstall.Find()?.Root is { Length: > 0 } steamRoot &&
            Path.GetPathRoot(steamRoot) is { Length: > 0 } drive)
        {
            return Path.Combine(drive, FolderName);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), FolderName);
    }

    public void Save()
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }
}
