using System.IO;
using System.Security.Cryptography;

namespace KpcLauncher.Core;

internal static class SecretStore
{
    public static byte[]? Read(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 16384) return null;
        return ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
    }
    public static void Write(string path, byte[] plain)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Delete(string path) => File.Delete(path);
}
