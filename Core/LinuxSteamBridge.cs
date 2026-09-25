using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KpcLauncher.Core;

/// <summary>The native helper owns Linux process inspection and command execution.
/// Its short-lived loopback capability is inherited by Wine, never persisted.</summary>
internal sealed class LinuxSteamBridge
{
    internal const string AddressVariable = "KPC_LINUX_BRIDGE";
    internal const string TokenVariable = "KPC_LINUX_BRIDGE_TOKEN";
    internal static bool IsConfigured => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AddressVariable));
    private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Uri _address;
    private readonly string _token;
    public string Root { get; private set; } = "";

    internal LinuxSteamBridge(string address, string token)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            uri.Host != "127.0.0.1" || uri.Port <= 0 || uri.AbsolutePath != "/" ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 ||
            token.Length != 64 || token.Any(c => !Uri.IsHexDigit(c)))
            throw new SteamDownloadException("Invalid Linux helper connection. Restart using linux-start.py.");
        _address = uri;
        _token = token;
    }

    internal static LinuxSteamBridge Connect(string? root)
    {
        var bridge = new LinuxSteamBridge(Environment.GetEnvironmentVariable(AddressVariable) ?? "",
            Environment.GetEnvironmentVariable(TokenVariable) ?? "");
        using var selected = bridge.Call("select", new { root });
        bridge.Root = selected.RootElement.GetProperty("root").GetString() ?? "";
        if (!Path.IsPathFullyQualified(bridge.Root) || !Directory.Exists(bridge.Root))
            throw new SteamDownloadException("Wine cannot access the native Steam data folder. Check Wine drive mappings.");
        return bridge;
    }

    internal sealed record State(int? Pid, DateTime? Started, ulong? SteamId);

    internal State ReadState()
    {
        using var document = Call("status", new { });
        var state = document.RootElement;
        if (state.GetProperty("schema").GetInt32() != 1 ||
            !string.Equals(state.GetProperty("root").GetString(), Root, StringComparison.Ordinal))
            throw new SteamDownloadException("The Linux helper's Steam folder changed. Select Steam again.");
        var pid = state.GetProperty("pid").ValueKind == JsonValueKind.Number ? state.GetProperty("pid").GetInt32() : (int?)null;
        var started = state.GetProperty("started").ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)(state.GetProperty("started").GetDouble() * 1000)).LocalDateTime
            : (DateTime?)null;
        ulong? id = ulong.TryParse(state.GetProperty("steamId").GetString(), NumberStyles.None,
            CultureInfo.InvariantCulture, out var parsed) && SteamOpenId.IsIndividualId(parsed) ? parsed : null;
        return new State(pid, started, pid > 0 && started.HasValue ? id : null);
    }

    internal bool MatchesStaging(string reported)
    {
        using var response = Call("path-match", new { path = reported });
        return response.RootElement.GetProperty("matches").GetBoolean();
    }

    internal void Download(uint appId, uint depotId, ulong manifestId, ulong steamId)
    {
        using var response = Call("download", new { appId, depotId,
            manifestId = manifestId.ToString(CultureInfo.InvariantCulture), steamId = steamId.ToString(CultureInfo.InvariantCulture) });
        if (!response.RootElement.GetProperty("ok").GetBoolean())
            throw new SteamDownloadException("The Linux helper did not accept the Steam download request.");
    }

    internal void RequireIdle(uint appId, uint depotId)
    {
        using var response = Call("idle", new { appId, depotId });
        if (!response.RootElement.GetProperty("ok").GetBoolean())
            throw new SteamDownloadException("Native Steam is not ready for another download.");
    }

    private JsonDocument Call(string operation, object value) => CallAsync(operation, value).GetAwaiter().GetResult();

    private async Task<JsonDocument> CallAsync(string operation, object value)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_address, operation));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            request.Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            // Bound the body even when a local service omits Content-Length.
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var bytes = new byte[1024];
            int count;
            while ((count = await stream.ReadAsync(bytes, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + count > 16384) throw new IOException("Oversized Linux helper response.");
                buffer.Write(bytes, 0, count);
            }
            var document = JsonDocument.Parse(buffer.ToArray());
            if (response.IsSuccessStatusCode) return document;
            using (document)
                throw new SteamDownloadException(document.RootElement.TryGetProperty("error", out var error)
                    ? error.GetString() ?? "Linux Steam helper error." : "Linux Steam helper error.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException)
        {
            throw new SteamDownloadException("The Linux Steam helper is unavailable. Keep linux-start.py running and restart the launcher through it.");
        }
    }
}
