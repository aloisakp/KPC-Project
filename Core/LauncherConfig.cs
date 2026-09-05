using System.IO;
using System.Text.Json;

namespace KpcLauncher.Core;

public sealed record ArchiveSpec(string Label, ulong ManifestId);

public sealed class LauncherConfig
{
    public const uint AppId = 844870;
    public const uint DepotId = 844871;
    public const string Branch = "public";

    public static IReadOnlyList<ArchiveSpec> RequiredArchives { get; } =
    [
        new("Archive A", 4819182874103212568UL),
        new("Archive B", 6221929141711975568UL),
    ];

    public int Schema { get; set; } = 1;
    public string StorageRoot { get; set; } = "";

    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KPCLauncher");

    private static string ConfigPath => Path.Combine(AppDataDir, "preservation-settings.json");

    public string ArchiveDirectory(ArchiveSpec archive) =>
        Path.Combine(StorageRoot, archive.ManifestId.ToString());

    public static LauncherConfig Load()
    {
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
