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
            var data = SecretStore.Read(StorePath);
            if (data is null) return null;
            var saved = JsonSerializer.Deserialize<SteamAuthorization>(data);
            return saved?.IsCurrent == true ? saved : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        { return null; }
    }

    public void Save()
    {
        if (!IsCurrent) throw new InvalidOperationException("A current browser authorization is required.");
        SecretStore.Write(StorePath, JsonSerializer.SerializeToUtf8Bytes(this));
    }

    public static void Forget() => SecretStore.Delete(StorePath);
}
