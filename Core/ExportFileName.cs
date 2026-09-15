using System.IO;
using System.Text.RegularExpressions;

namespace KpcLauncher.Core;

internal static class ExportFileName
{
    internal static string Complete(string temporary, string characterName)
    {
        var name = new string(characterName.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray());
        if (name.Length > 80) name = name[..(char.IsHighSurrogate(name[79]) ? 79 : 80)];
        name = name.Trim().TrimEnd('.');
        if (name.Length == 0) name = "Character";
        if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])($|\.)", RegexOptions.IgnoreCase)) name = "_" + name;
        var folder = Path.GetDirectoryName(Path.GetFullPath(temporary))!;
        for (var suffix = 1; suffix <= 10000; suffix++)
        {
            var destination = Path.Combine(folder, name + (suffix == 1 ? "" : $" ({suffix})") + ".kpc-character.json");
            try { File.Move(temporary, destination, overwrite: false); return destination; }
            catch (IOException) when (File.Exists(destination)) { }
        }
        throw new IOException("Too many exports for this character. Remove an older export and try again.");
    }
}
