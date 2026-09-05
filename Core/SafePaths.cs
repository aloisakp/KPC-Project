using System.IO;

namespace KpcLauncher.Core;

internal static class SafePaths
{
    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public static bool Same(string a, string b) => Normalize(a).Equals(Normalize(b), StringComparison.OrdinalIgnoreCase);
    public static bool Within(string path, string root) => Same(path, root) ||
        Normalize(path).StartsWith(Normalize(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void NoLinks(string path)
    {
        for (var item = new DirectoryInfo(Path.GetFullPath(path)); item is not null; item = item.Parent)
        {
            if ((item.Exists || File.Exists(item.FullName)) &&
                (File.GetAttributes(item.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new SteamDownloadException("Choose a regular folder: links and junctions are not supported for downloads.");
        }
    }

    public static IEnumerable<FileInfo> Files(string root)
    {
        NoLinks(root);
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new SteamDownloadException("A download contains a link or junction; no files were removed.");
            if (entry is DirectoryInfo directory)
                foreach (var file in Files(directory.FullName)) yield return file;
            else if (entry is FileInfo file) yield return file;
        }
    }
}
