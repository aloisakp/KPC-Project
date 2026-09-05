using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace KpcLauncher.Core;

/// <summary>Browser identity verification only. This never obtains a Steam download session.</summary>
public static class SteamOpenId
{
    internal const string Endpoint = "https://steamcommunity.com/openid/login";
    internal const string Namespace = "http://specs.openid.net/auth/2.0";
    internal const string IdentityPrefix = "https://steamcommunity.com/openid/id/";
    internal const ulong IndividualBase = 76561197960265728UL;

    public static async Task<ulong> AuthenticateAsync(IReporter reporter, CancellationToken ct)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        reporter.Step("Authorize through Steam in your browser");
        reporter.Log("Opening steamcommunity.com. Approve the account you use in the Steam client.");
        return await AuthenticateAsync(url =>
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }, http, ct).ConfigureAwait(false);
    }

    internal static async Task<ulong> AuthenticateAsync(Action<string> openBrowser, HttpClient http, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(4);
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var realm = $"http://127.0.0.1:{port}/";
            // The unpredictable callback is covered by Steam's signed return_to field.
            var returnTo = realm + "callback/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var began = DateTimeOffset.UtcNow;
            var request = new Dictionary<string, string>
            {
                ["openid.ns"] = Namespace, ["openid.mode"] = "checkid_setup",
                ["openid.realm"] = realm, ["openid.return_to"] = returnTo,
                ["openid.identity"] = Namespace + "/identifier_select",
                ["openid.claimed_id"] = Namespace + "/identifier_select",
            };
            openBrowser(Endpoint + "?" + Encode(request));
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                Dictionary<string, string>? fields;
                try
                {
                    var headers = await ReadHeadersAsync(client.GetStream(), requestTimeout.Token).ConfigureAwait(false);
                    fields = ParseCallback(headers, returnTo);
                }
                catch (Exception ex) when (ex is IOException or FormatException ||
                    ex is OperationCanceledException && !timeout.IsCancellationRequested)
                {
                    continue;
                }
                if (fields is null) { await RespondAsync(client, false, timeout.Token); continue; }
                if (fields.GetValueOrDefault("openid.mode") == "cancel")
                {
                    await RespondAsync(client, false, timeout.Token);
                    throw new SteamDownloadException("Steam authorization was cancelled. Select Authorize Steam to try again.");
                }
                var steamId = await VerifyAsync(fields, returnTo, began, http, timeout.Token).ConfigureAwait(false);
                await RespondAsync(client, steamId.HasValue, timeout.Token).ConfigureAwait(false);
                if (steamId.HasValue) return steamId.Value;
                // Unsolicited or invalid requests must not complete the pending authorization.
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SteamDownloadException("Steam authorization timed out. Select Authorize Steam to try again.");
        }
        finally { listener.Stop(); }
    }

    internal static string Encode(IEnumerable<KeyValuePair<string, string>> fields) =>
        string.Join("&", fields.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));

    internal static Dictionary<string, string>? ParseCallback(string headers, string returnTo)
    {
        var lines = headers.Split("\r\n", StringSplitOptions.None);
        var request = lines[0].Split(' ');
        var callback = new Uri(returnTo);
        if (request.Length != 3 || request[0] != "GET" || request[2] != "HTTP/1.1") return null;
        var hosts = lines.Skip(1).Where(l => l.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (hosts.Length != 1 || hosts[0][5..].Trim() != callback.Authority) return null;
        var target = request[1];
        var question = target.IndexOf('?');
        if (question < 0 || target[..question] != callback.AbsolutePath || target.Contains('#')) return null;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in target[(question + 1)..].Split('&'))
        {
            var equals = pair.IndexOf('=');
            if (equals <= 0) return null;
            var key = Uri.UnescapeDataString(pair[..equals].Replace('+', ' '));
            var value = Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            if (!key.StartsWith("openid.", StringComparison.Ordinal) || !fields.TryAdd(key, value)) return null;
        }
        return fields;
    }

    internal static async Task<ulong?> VerifyAsync(Dictionary<string, string> fields, string returnTo,
        DateTimeOffset began, HttpClient http, CancellationToken ct)
    {
        if (fields.GetValueOrDefault("openid.ns") != Namespace ||
            fields.GetValueOrDefault("openid.mode") != "id_res" ||
            fields.GetValueOrDefault("openid.op_endpoint") != Endpoint ||
            fields.GetValueOrDefault("openid.return_to") != returnTo) return null;
        var claimed = fields.GetValueOrDefault("openid.claimed_id");
        if (claimed is null || claimed != fields.GetValueOrDefault("openid.identity") ||
            !claimed.StartsWith(IdentityPrefix, StringComparison.Ordinal)) return null;
        var id = claimed[IdentityPrefix.Length..];
        if (!ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId) ||
            !IsIndividualId(steamId) || id != steamId.ToString(CultureInfo.InvariantCulture)) return null;
        var signed = (fields.GetValueOrDefault("openid.signed") ?? "").Split(',').ToHashSet(StringComparer.Ordinal);
        if (new[] { "op_endpoint", "claimed_id", "identity", "return_to", "response_nonce", "assoc_handle" }
            .Any(name => !signed.Contains(name) || !fields.ContainsKey("openid." + name)) ||
            string.IsNullOrWhiteSpace(fields.GetValueOrDefault("openid.sig"))) return null;
        var nonce = fields["openid.response_nonce"];
        if (nonce.Length <= 20 || !DateTimeOffset.TryParseExact(nonce[..20], "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var issued) ||
            issued < began.AddMinutes(-2) || issued > DateTimeOffset.UtcNow.AddMinutes(2)) return null;
        var verification = new Dictionary<string, string>(fields) { ["openid.mode"] = "check_authentication" };
        using var body = new FormUrlEncodedContent(verification);
        // Always contact Valve over validated TLS; never follow an assertion-supplied URL.
        using var response = await http.PostAsync(Endpoint, body, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        var reply = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in reply.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = line.TrimEnd('\r');
            var colon = clean.IndexOf(':');
            if (colon <= 0 || !result.TryAdd(clean[..colon], clean[(colon + 1)..])) return null;
        }
        return result.GetValueOrDefault("ns") == Namespace && result.GetValueOrDefault("is_valid") == "true"
            ? steamId : null;
    }

    public static bool IsIndividualId(ulong id) => id > IndividualBase && id <= IndividualBase + uint.MaxValue;

    private static async Task<string> ReadHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[16384];
        var used = 0;
        while (used < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(used), ct).ConfigureAwait(false);
            if (read == 0) throw new IOException("Incomplete browser callback.");
            used += read;
            var text = Encoding.ASCII.GetString(buffer, 0, used);
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0) return text[..end];
        }
        throw new IOException("Browser callback is too large.");
    }

    private static async Task RespondAsync(TcpClient client, bool success, CancellationToken ct)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var body = Encoding.UTF8.GetBytes(BuildResultPage(success, nonce));
        var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\nReferrer-Policy: no-referrer\r\n" +
            $"Content-Security-Policy: default-src 'none'; style-src 'nonce-{nonce}'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'\r\n" +
            "X-Content-Type-Options: nosniff\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n");
        try
        {
            await client.GetStream().WriteAsync(header, ct).ConfigureAwait(false);
            await client.GetStream().WriteAsync(body, ct).ConfigureAwait(false);
        }
        catch (IOException) { /* Closing the browser must not discard a verified identity. */ }
    }

    internal static string BuildResultPage(bool success, string styleNonce)
    {
        var title = success ? "Steam account authorized" : "Authorization not completed";
        var status = success ? "ACCOUNT CONNECTED" : "NO CHANGES MADE";
        var message = success
            ? "Your Steam account is now connected to KPC Launcher. You're ready for the next step."
            : "This request could not complete your Steam authorization. You can try again from KPC Launcher.";
        var next = success ? "Return to the launcher" : "Try again in the launcher";
        var detail = success
            ? "Keep Steam signed in to the same account, then select Install to preserve your game files."
            : "Select Authorize Steam to open a fresh sign-in page. Your Steam account has not been changed.";
        var icon = success ? "<path d=\"m8 16 5 5L25 9\"/>" : "<path d=\"M16 8v10m0 5v1\"/>";
        return $$$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <meta name="theme-color" content="#0c0e13">
              <title>{{{title}}} · KPC Launcher</title>
              <style nonce="{{{styleNonce}}}">
                *{box-sizing:border-box}html{color-scheme:dark}
                body{margin:0;min-height:100vh;min-height:100svh;display:grid;place-items:center;
                  padding:32px 20px;background:#0c0e13;color:#e8ebf2;font-family:Segoe UI,system-ui,sans-serif;
                  background-image:radial-gradient(ellipse at 50% 0%,#25335480,transparent 60%)}
                main{width:min(100%,560px)}.brand{display:flex;align-items:center;gap:11px;margin:0 0 28px 4px;
                  font-size:12px;letter-spacing:2px;color:#9ca6ba}.brand strong{color:#e8ebf2;font-size:17px;letter-spacing:1px}
                .mark{width:4px;height:19px;border-radius:2px;background:#7894ff}
                article{padding:38px;border:1px solid #2a3346;border-radius:18px;background:linear-gradient(145deg,#192132,#11151f);
                  box-shadow:0 24px 90px #0005;position:relative;overflow:hidden}
                article:before{content:"";position:absolute;top:0;left:32px;right:32px;height:1px;
                  background:linear-gradient(90deg,transparent,#7894ff80,transparent)}
                .icon{display:grid;place-items:center;width:64px;height:64px;border-radius:20px;margin-bottom:26px;
                  color:#6ee0a1;background:#22483966;border:1px solid #6ee0a133}
                .pending .icon{color:#efc778;background:#51402266;border-color:#efc77833}
                svg{width:34px;height:34px;fill:none;stroke:currentColor;stroke-width:2.5;stroke-linecap:round;stroke-linejoin:round}
                .status{font-size:10px;font-weight:650;letter-spacing:1.7px;color:#6ee0a1;margin:0 0 12px}
                .pending .status{color:#efc778}h1{font-size:clamp(25px,5vw,32px);font-weight:600;letter-spacing:-.8px;
                  line-height:1.2;margin:0 0 16px}p{font-size:14px;line-height:1.75;color:#a8b2c6;margin:0}
                .next{margin-top:30px;padding:20px;border:1px solid #303b54;border-radius:10px;background:#0d121dcc}
                .next h2{display:flex;align-items:center;gap:10px;font-size:14px;font-weight:600;margin:0 0 8px;color:#e2e8f8}
                .arrow{color:#91a7ff;font-size:19px}.next p{font-size:13px;line-height:1.65}
                footer{text-align:center;font-size:12px;color:#8590a7;margin-top:24px;line-height:1.7}
                footer span{display:block;color:#67738c;font-size:11px;margin-top:5px}
                @media(max-width:420px){article{padding:28px 24px}.next{padding:16px}.brand{margin-bottom:20px}}
              </style>
            </head>
            <body>
              <main class="{{{(success ? "success" : "pending")}}}">
                <div class="brand"><span class="mark" aria-hidden="true"></span><strong>KPC</strong> LAUNCHER</div>
                <article aria-labelledby="result">
                  <div class="icon" aria-hidden="true"><svg viewBox="0 0 32 32">{{{icon}}}</svg></div>
                  <p class="status">{{{status}}}</p>
                  <h1 id="result">{{{title}}}</h1>
                  <p>{{{message}}}</p>
                  <section class="next" aria-labelledby="next-step">
                    <h2 id="next-step"><span class="arrow" aria-hidden="true">↗</span>{{{next}}}</h2>
                    <p>{{{detail}}}</p>
                  </section>
                </article>
                <footer>You can safely close this tab.<span>Steam handles your sign-in. KPC Launcher never receives your password.</span></footer>
              </main>
            </body>
            </html>
            """;
    }
}
