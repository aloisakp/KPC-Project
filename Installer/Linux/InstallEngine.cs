using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KpcLauncher.Setup;

public static class InstallEngine
{
    public const string Marker = ".kpc-install.json";
    public sealed record Installation(string Schema, string Root, string Version, string Executable, string DesktopFile);
    public static void NoLinks(string path)
    {
        for (var entry = new DirectoryInfo(Path.GetFullPath(path)); entry is not null; entry = entry.Parent)
        {
            if (entry.LinkTarget is not null || entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Choose a regular folder without symbolic links.");
        }
    }
    public static string Validate(string selected)
    {
        if (!Path.IsPathFullyQualified(selected) || selected.Any(char.IsControl)) throw new IOException("Choose an absolute folder path.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected)); NoLinks(root);
        if (root == Path.GetPathRoot(root)) throw new IOException("Choose a launcher subfolder.");
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            var marker = Path.Combine(root, Marker); NoLinks(marker);
            if (!File.Exists(marker) || new FileInfo(marker).Length > 16384) throw new IOException("Choose an empty folder or an existing KPC Launcher installation.");
            var installed = JsonSerializer.Deserialize<Installation>(File.ReadAllText(marker));
            if (installed?.Schema != "kpc-linux-install/v1" || installed.Root != root)
                throw new IOException("This folder is not a recognized KPC Launcher installation.");
        }
        return root;
    }
    public static string DesktopQuote(string path) => "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%") + "\"";
    public static Installation Install(Stream payload, string selected, string version, string applicationsDirectory)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, "^\\d+\\.\\d+\\.\\d+$")) throw new IOException("Invalid installer version.");
        var root = Validate(selected);
        NoLinks(applicationsDirectory);
        Directory.CreateDirectory(root);
        NoLinks(Path.Combine(root, ".install.lock"));
        using var ownership = new FileStream(Path.Combine(root, ".install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (!File.Exists(Path.Combine(root, Marker)))
            AtomicWrite(Path.Combine(root, Marker), JsonSerializer.Serialize(new Installation("kpc-linux-install/v1", root, "", "", "")));
        var versions = Path.Combine(root, "versions"); NoLinks(versions); Directory.CreateDirectory(versions);
        var directory = Path.Combine(versions, version + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var finished = false;
        try
        {
            using var zip = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count is < 1 or > 5000 || zip.Entries.Sum(e => e.Length) > 1_500_000_000) throw new IOException("Invalid installer payload.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (name.Contains('\\') || name.Contains(':') || name.Split('/').Any(p => p is "" or "." or "..") || !seen.Add(name) ||
                    ((entry.ExternalAttributes >> 16) & 0xf000) is not (0 or 0x8000)) throw new IOException("Unsafe installer payload path.");
                var file = Path.GetFullPath(Path.Combine(directory, name));
                if (!file.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new IOException("Installer payload escaped its folder.");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                entry.ExtractToFile(file);
                var executable = ((entry.ExternalAttributes >> 16) & 0x49) != 0;
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | (executable ? UnixFileMode.UserExecute : 0));
            }
            var app = Path.Combine(directory, "KpcLauncher.Linux");
            if (!File.Exists(app) || !File.Exists(Path.Combine(directory, "game-worker", "KpcGameWorker.exe"))) throw new IOException("The installer is missing required launcher files.");
            File.SetUnixFileMode(app, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..16].ToLowerInvariant();
            var desktop = Path.Combine(applicationsDirectory, "kpc-launcher-" + id + ".desktop"); NoLinks(desktop);
            if (File.Exists(desktop) && !File.ReadAllText(desktop).StartsWith("# KPC Launcher installer\n", StringComparison.Ordinal))
                throw new IOException("An unrelated application shortcut already uses this name.");
            Directory.CreateDirectory(applicationsDirectory);
            var desktopText = "# KPC Launcher installer\n[Desktop Entry]\nType=Application\nName=KPC Launcher\nComment=Steam preservation and community testing\nExec=" + DesktopQuote(app) + "\nTerminal=false\nCategories=Game;\n";
            var result = new Installation("kpc-linux-install/v1", root, version, app, desktop);
            AtomicWrite(Path.Combine(root, Marker), JsonSerializer.Serialize(result));
            finished = true;
            AtomicWrite(desktop, desktopText);
            return result;
        }
        finally
        {
            if (!finished) Directory.Delete(directory, recursive: true); // This invocation's new GUID directory only.
        }
    }
    private static void AtomicWrite(string target, string contents)
    {
        NoLinks(target);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, contents); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
