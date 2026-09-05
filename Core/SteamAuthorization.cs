using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace KpcLauncher.Core;

/// <summary>A remembered public identity, never a Steam credential or download token.</summary>
public sealed record SteamAuthorization(ulong SteamId, DateTimeOffset VerifiedAt)
{
    private static string StorePath => Path.Combine(LauncherConfig.AppDataDir, "steam-identity.dat");

    public bool IsCurrent => SteamOpenId.IsIndividualId(SteamId) &&
        VerifiedAt <= DateTimeOffset.UtcNow.AddMinutes(2) && VerifiedAt > DateTimeOffset.UtcNow.AddDays(-30);

    public static SteamAuthorization? Load()
    {
        try
        {
            if (!File.Exists(StorePath) || new FileInfo(StorePath).Length > 16384) return null;
            var data = ProtectedData.Unprotect(File.ReadAllBytes(StorePath), null, DataProtectionScope.CurrentUser);
            var saved = JsonSerializer.Deserialize<SteamAuthorization>(data);
            return saved?.IsCurrent == true ? saved : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        { return null; }
    }

    public void Save()
    {
        if (!IsCurrent) throw new InvalidOperationException("A current browser authorization is required.");
        Directory.CreateDirectory(LauncherConfig.AppDataDir);
        var data = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(this), null, DataProtectionScope.CurrentUser);
        var temporary = StorePath + ".tmp";
        File.WriteAllBytes(temporary, data);
        File.Move(temporary, StorePath, overwrite: true);
    }

    public static void Forget() => File.Delete(StorePath);
}
