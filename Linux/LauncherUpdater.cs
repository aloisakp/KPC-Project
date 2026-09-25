using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace KpcLauncher.Core;

public sealed record LauncherUpdate(string Version, object Payload);
public sealed class LauncherUpdater
{
    public const string RepositoryUrl = "https://github.com/aloisakp/KPC-Project";
    private sealed record Asset(Uri Url, string Hash, long Bytes);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    internal static string? InstallRoot => Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.Parent?.FullName;
    public bool IsInstalledBuild => InstallRoot is { } root && File.Exists(Path.Combine(root, ".kpc-install.json"));
    public string CurrentVersion => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.7.1";
    public async Task<LauncherUpdate?> CheckAsync()
    {
        if (!IsInstalledBuild) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/aloisakp/KPC-Project/releases/latest");
        request.Headers.UserAgent.ParseAdd("KPCLauncher/" + CurrentVersion);
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length > 2 * 1024 * 1024) throw new IOException("Update metadata is too large.");
        using var json = JsonDocument.Parse(bytes);
        var release = json.RootElement;
        var tag = release.GetProperty("tag_name").GetString()!;
        if (!Version.TryParse(tag.TrimStart('v'), out var version) || version <= Version.Parse(CurrentVersion)) return null;
        var item = release.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == "KPCLauncher-linux-Setup.run");
        var url = new Uri(item.GetProperty("browser_download_url").GetString()!);
        var expected = RepositoryUrl + "/releases/download/" + tag + "/KPCLauncher-linux-Setup.run";
        var digest = item.GetProperty("digest").GetString() ?? "";
        var length = item.GetProperty("size").GetInt64();
        if (url.AbsoluteUri != expected || !System.Text.RegularExpressions.Regex.IsMatch(digest, "^sha256:[a-f0-9]{64}$") || length is < 1 or > 600_000_000)
            throw new IOException("Invalid Linux update metadata.");
        return new LauncherUpdate(version.ToString(), new Asset(url, digest[7..], length));
    }
    public async Task DownloadAndApplyAsync(LauncherUpdate update, Action<int> progress, CancellationToken ct)
    {
        var asset = (Asset)update.Payload;
        var folder = Path.Combine(LauncherConfig.AppDataDir, "updates");
        SafePaths.NoLinks(folder); Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "setup-" + Guid.NewGuid().ToString("N") + ".run");
        try
        {
            using var response = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await source.ReadAsync(buffer, ct)) != 0)
                {
                    total += count;
                    if (total > asset.Bytes) throw new IOException("Oversized update.");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct); progress((int)(100 * total / asset.Bytes));
                }
                if (total != asset.Bytes) throw new IOException("Incomplete update.");
            }
            await using (var verify = File.OpenRead(file))
                if (Convert.ToHexString(await SHA256.HashDataAsync(verify, ct)).ToLowerInvariant() != asset.Hash) throw new IOException("Update checksum failed.");
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var start = new ProcessStartInfo(file) { UseShellExecute = false };
            start.ArgumentList.Add("--destination"); start.ArgumentList.Add(InstallRoot!);
            Process.Start(start)?.Dispose();
        }
        catch { if (File.Exists(file)) File.Delete(file); throw; }
    }
}
