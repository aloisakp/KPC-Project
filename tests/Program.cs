using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using KpcLauncher.Core;

const ulong Account = 76561198000000001;
var passed = 0;
var reporter = new QuietReporter();
var root = Path.Combine(Path.GetTempPath(), "kpc-security-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var began = DateTimeOffset.UtcNow;
const string Callback = "http://127.0.0.1:12345/callback/0123456789abcdef";
Dictionary<string, string> Assertion(string callback = Callback) => new()
{
    ["openid.ns"] = SteamOpenId.Namespace, ["openid.mode"] = "id_res",
    ["openid.op_endpoint"] = SteamOpenId.Endpoint, ["openid.return_to"] = callback,
    ["openid.claimed_id"] = SteamOpenId.IdentityPrefix + Account,
    ["openid.identity"] = SteamOpenId.IdentityPrefix + Account,
    ["openid.response_nonce"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") + "unique",
    ["openid.assoc_handle"] = "test", ["openid.sig"] = "test-signature",
    ["openid.signed"] = "op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle",
};
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
async Task Reject(Func<Task> action, string name)
{
    try { await action(); } catch (SteamDownloadException) { Check(true, name); return; }
    throw new Exception("FAIL: " + name);
}
using var verifier = new FakeValve();
using var http = new HttpClient(verifier);
try
{
    Check(await SteamOpenId.VerifyAsync(Assertion(), Callback, began, http, default) == Account, "valid signed identity");
    Check(verifier.Calls == 1 && verifier.LastBody.Contains("openid.mode=check_authentication"), "Valve verification required");
    foreach (var (field, value) in new[]
    {
        ("openid.return_to", Callback + "wrong"), ("openid.op_endpoint", "http://127.0.0.1/steal"),
        ("openid.claimed_id", SteamOpenId.IdentityPrefix + "76561198000000002"),
        ("openid.ns", "wrong"), ("openid.mode", "setup_needed"), ("openid.sig", ""),
        ("openid.signed", "op_endpoint,claimed_id,identity,response_nonce,assoc_handle"),
        ("openid.response_nonce", "2000-01-01T00:00:00Zold"),
        ("openid.response_nonce", "2099-01-01T00:00:00Zfuture"),
    })
    {
        var fields = Assertion(); fields[field] = value;
        var before = verifier.Calls;
        Check(await SteamOpenId.VerifyAsync(fields, Callback, began, http, default) is null && verifier.Calls == before,
            "reject " + field + " before network");
    }
    var badIdentity = Assertion();
    badIdentity["openid.identity"] = badIdentity["openid.claimed_id"] = SteamOpenId.IdentityPrefix + "1";
    Check(await SteamOpenId.VerifyAsync(badIdentity, Callback, began, http, default) is null, "reject non-individual SteamID");
    verifier.Valid = false;
    Check(await SteamOpenId.VerifyAsync(Assertion(), Callback, began, http, default) is null, "reject invalid Valve signature");
    verifier.Valid = true; verifier.Duplicate = true;
    Check(await SteamOpenId.VerifyAsync(Assertion(), Callback, began, http, default) is null, "reject ambiguous Valve response");
    verifier.Duplicate = false;

    string Headers(string query, string host = "127.0.0.1:12345", string path = "/callback/0123456789abcdef", string method = "GET") =>
        $"{method} {path}?{query} HTTP/1.1\r\nHost: {host}\r\n";
    var encoded = SteamOpenId.Encode(Assertion());
    Check(SteamOpenId.ParseCallback(Headers(encoded), Callback)?.Count == 10, "valid callback parsing");
    Check(SteamOpenId.ParseCallback(Headers(encoded + "&openid.mode=id_res"), Callback) is null, "duplicate callback parameters");
    Check(SteamOpenId.ParseCallback(Headers(encoded, "evil.example"), Callback) is null, "DNS rebinding Host rejected");
    Check(SteamOpenId.ParseCallback(Headers(encoded, path: "/callback/wrong"), Callback) is null, "wrong callback state rejected");
    Check(SteamOpenId.ParseCallback(Headers(encoded, method: "POST"), Callback) is null, "POST callback rejected");

    var browserUrl = new TaskCompletionSource<string>();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var flow = SteamOpenId.AuthenticateAsync(url => browserUrl.SetResult(url), http, timeout.Token);
    var opened = new Uri(await browserUrl.Task);
    Check(opened.GetLeftPart(UriPartial.Path) == SteamOpenId.Endpoint, "browser opens only Valve");
    var returnTo = Uri.UnescapeDataString(opened.Query.TrimStart('?').Split('&').Single(p => p.StartsWith("openid.return_to="))[17..]);
    var callback = new Uri(returnTo);
    Check(callback.Host == "127.0.0.1" && callback.AbsolutePath.Split('/').Last().Length == 64, "random loopback callback");
    using var browser = new HttpClient(new HttpClientHandler { UseProxy = false });
    await browser.GetAsync(callback.GetLeftPart(UriPartial.Authority) + "/favicon.ico", timeout.Token);
    Check(!flow.IsCompleted, "stray browser request does not consume authorization");
    // Deliberately fragment the callback across TCP writes, including the request line.
    using (var client = new TcpClient())
    {
        await client.ConnectAsync(IPAddress.Loopback, callback.Port, timeout.Token);
        var request = Encoding.ASCII.GetBytes($"GET {callback.PathAndQuery}?{SteamOpenId.Encode(Assertion(returnTo))} HTTP/1.1\r\nHost: {callback.Authority}\r\n\r\n");
        await client.GetStream().WriteAsync(request.AsMemory(0, 7), timeout.Token);
        await Task.Delay(30, timeout.Token);
        await client.GetStream().WriteAsync(request.AsMemory(7), timeout.Token);
        using var reader = new StreamReader(client.GetStream());
        var response = await reader.ReadToEndAsync(timeout.Token);
        Check(response.Contains("Steam account authorized") && response.Contains("Cache-Control: no-store"), "verified browser completion");
    }
    Check(await flow == Account, "fragmented TCP authorization round trip");

    var authorization = new SteamAuthorization(Account, DateTimeOffset.UtcNow);
    SteamInstall.RequireAccount(authorization, Account);
    Check(true, "matching desktop account accepted");
    await Reject(() => { SteamInstall.RequireAccount(authorization, Account + 1); return Task.CompletedTask; }, "different desktop account blocked");
    await Reject(() => { SteamInstall.RequireAccount(authorization, null); return Task.CompletedTask; }, "missing desktop identity blocked");
    await Reject(() => { SteamInstall.RequireAccount(authorization with { VerifiedAt = began.AddDays(-31) }, Account); return Task.CompletedTask; }, "expired authorization blocked");
    const string Log = "[2026-09-05 10:00:00] [Logged On, 4, 7] [U:1:123] ready\n";
    var started = new DateTime(2026, 9, 5, 9, 0, 0);
    Check(SteamInstall.ParseConnectedIdentity(Log, started, 0) == SteamOpenId.IndividualBase + 123, "connected log supports absent registry");
    Check(SteamInstall.ParseConnectedIdentity(Log, started, 124) is null, "registry and log disagreement blocked");
    Check(SteamInstall.ParseConnectedIdentity(Log, started.AddHours(2), 123) is null, "previous process log rejected");
    Check(SteamInstall.ParseConnectedIdentity(Log + "[2026-09-05 10:01:00] [Logged Off, 4, 7] [U:1:123] bye\n", started, 123) is null, "logout invalidates account");
    var pendingFolder = Path.Combine(root, "steamapps", "content", "app_1", "depot_2");
    var pendingRequest = "[2026-09-05 10:00:00] ExecCommandLine: \"steam.exe +download_depot 1 2 3\"\n";
    var pendingStart = "[2026-09-05 10:00:01] Downloading depot 2 (2 files, 2 MB) ...\n";
    var pendingEnd = $"[2026-09-05 10:00:02] Depot download complete : \"{pendingFolder}\" (manifest 3)\n";
    bool Pending(string text, DateTime? since = null) => SteamInstall.HasPendingDepotDownload(
        new StringReader(text), since ?? started, 1, 2, pendingFolder);
    Check(Pending(pendingRequest), "queued Steam request blocks a competing download");
    Check(Pending(pendingRequest + pendingStart), "cancelled launcher cannot overwrite active staging");
    Check(!Pending(pendingRequest + pendingStart + pendingEnd), "completed Steam request releases the staging guard");
    Check(Pending(pendingRequest + pendingStart + pendingRequest + pendingStart + pendingEnd), "one completion cannot clear two requests");
    Check(!Pending(pendingRequest + pendingStart, started.AddHours(2)), "Steam restart clears previous-process pending requests");
    Check(Pending(pendingRequest + pendingStart + "[2026-09-05 10:00:02] Depot download failed : unrelated\n"),
        "unscoped Steam failure cannot clear the staging guard");

    Directory.CreateDirectory(Path.Combine(root, "logs"));
    ulong? currentAccount = Account + 1;
    var commands = 0;
    var steam = new SteamInstall(root, "unused", () => currentAccount, (_, _, _) => commands++);
    await Reject(() => new DepotDownload(steam, authorization, reporter).RunAsync(1, 2, 3, "test", default), "mismatch sends no Steam command");
    Check(commands == 0, "zero commands for mismatch");
    currentAccount = Account;
    steam = new SteamInstall(root, "unused", () => currentAccount, (_, _, _) => { commands++; currentAccount++; });
    await Reject(() => new DepotDownload(steam, authorization, reporter).RunAsync(1, 2, 3, "test", default), "account switch during transfer stops tracking");
    currentAccount = Account;
    steam = new SteamInstall(root, "unused", () => currentAccount, (_, _, _) =>
        File.AppendAllText(Path.Combine(root, "logs", "console_log.txt"), $"Depot download complete : \"{root}\" (manifest 3)\n"));
    await Reject(() => new DepotDownload(steam, authorization, reporter).RunAsync(1, 2, 3, "test", default), "unexpected completion directory rejected");
    var staging = steam.StagingDirectory(1, 2);
    Directory.CreateDirectory(staging);
    steam = new SteamInstall(root, "unused", () => currentAccount, (_, _, _) =>
        File.AppendAllText(Path.Combine(root, "logs", "console_log.txt"), $"Depot download complete : \"{staging}\" (manifest 3)\n"));
    Check(await new DepotDownload(steam, authorization, reporter).RunAsync(1, 2, 3, "test", default) == staging, "expected completion accepted");

    // Real regression: Steam allocates final-length files before it downloads their chunks.
    File.WriteAllBytes(Path.Combine(staging, "preallocated.bin"), new byte[4096]);
    using (var progressCancel = new CancellationTokenSource())
    {
        var progressReporter = new QuietReporter { OnProgress = _ => progressCancel.Cancel() };
        steam = new SteamInstall(root, "unused", () => currentAccount, (_, _, _) =>
        {
            File.AppendAllText(steam.ConsoleLog, "Downloading depot 2 (1 files, 4 MB) ...\n");
            File.AppendAllText(steam.ContentLog, "AppID 1 update started : download 0/4096, stage 0/4096\nDownloading 4 chunks for depot 2 (3)\n");
        });
        try { await new DepotDownload(steam, authorization, progressReporter).RunAsync(1, 2, 3, "test", progressCancel.Token); }
        catch (OperationCanceledException) { }
        Check(progressReporter.LastProgress is { Total: 4096, Done: 0 }, "preallocated files never show false 100 percent");
        Check(progressReporter.LastProgress!.Detail.Contains("Estimated"), "download estimate is clearly labeled");
    }

    var clock = new DateTime(2026, 9, 5, 10, 0, 0);
    SteamTransferProgress Transfer(long done = 0)
    {
        var progress = new SteamTransferProgress(1, 2, 3);
        progress.Observe($"AppID 1 update started : download {done}/1000000000, stage 0/2000000000", clock);
        progress.Observe("Downloading 2000 chunks for depot 2 (3)", clock);
        return progress;
    }
    var transfer = Transfer();
    transfer.Observe("Current download rate: 8.000 Mbps", clock.AddSeconds(10));
    Check(transfer.Report("test", clock.AddSeconds(10)).Done == 10_000_000, "first Steam rate estimates elapsed transfer bytes");
    Check(transfer.Report("test", clock.AddSeconds(20)).Done == 20_000_000, "bar advances between Steam log samples");
    transfer.Observe("Current download rate: 0.000 Mbps", clock.AddSeconds(30));
    Check(transfer.Report("test", clock.AddSeconds(40)).Done == 30_000_000, "zero rate stops advancing the bar");
    transfer = Transfer();
    transfer.Observe("Current download rate: 8.000 Mbps", clock.AddSeconds(10));
    var stale = transfer.Report("test", clock.AddSeconds(100));
    Check(stale.Done == 85_000_000 && transfer.Report("test", clock.AddSeconds(200)).Done == stale.Done,
        "stale speed cannot generate unlimited progress");
    Check(!stale.Detail.Contains("Mbps"), "stale speed is removed from the status line");
    transfer = Transfer(50_000_000);
    Check(transfer.Report("test", clock).Done == 50_000_000, "Steam resume byte count initializes the estimate");
    transfer.Observe("Current download rate: 100000.000 Mbps", clock.AddSeconds(1));
    var capped = transfer.Report("test", clock.AddSeconds(10));
    Check(capped.Fraction == .95 && capped.Detail.Contains("waiting for Steam completion"), "estimate cannot claim completion");
    transfer.Observe("AppID 99 update started : download 0/1000", clock.AddSeconds(11));
    Check(transfer.Report("test", clock.AddSeconds(12)).Total == 0, "overlapping app download disables aggregate-rate estimate");
    transfer = Transfer();
    transfer.Observe("Downloading 20 chunks for depot 2 (4)", clock);
    Check(transfer.Report("test", clock).Total == 0, "different manifest disables ambiguous estimate");
    transfer = Transfer();
    transfer.Observe("Current download rate: " + new string('9', 400) + " Mbps", clock);
    Check(transfer.Report("test", clock.AddSeconds(5)).Done == 0, "non-finite rate is ignored");
    transfer.Observe("[2099-01-01 10:00:00] Current download rate: 42.000 Mbps", clock);
    Check(transfer.Report("test", clock.AddSeconds(5)).Done == 0, "future-dated log sample is ignored");
    transfer.Observe("[2026-09-05 10:00:10] Current download rate: 8.000 Mbps", clock.AddSeconds(10.8));
    transfer.Report("test", clock.AddSeconds(20.8));
    transfer.Observe("[2026-09-05 10:00:20] Current download rate: 0.000 Mbps", clock.AddSeconds(21));
    var stopped = transfer.Report("test", clock.AddSeconds(21)).Done;
    Check(transfer.Report("test", clock.AddSeconds(30)).Done == stopped, "buffered rate samples still stop an extrapolated estimate");

    var archive = Path.Combine(root, "archive"); Directory.CreateDirectory(archive);
    var file = Path.Combine(archive, "data.bin"); File.WriteAllText(file, "original");
    var beforeHash = PreservationPipeline.Measure(archive, default);
    File.WriteAllText(file, "tampered");
    var afterHash = PreservationPipeline.Measure(archive, default);
    Check(beforeHash.Files == afterHash.Files && beforeHash.Bytes == afterHash.Bytes && beforeHash.Digest != afterHash.Digest,
        "verification detects equal-size content tampering");
    Check(SafePaths.Within(Path.Combine(root, "archive"), root) && !SafePaths.Within(root + "-outside", root), "path containment uses directory boundary");
    Check(SafePaths.Within(root, Path.GetPathRoot(root)!), "drive-root containment includes its children");
    var copySource = Path.Combine(root, "copy-source");
    Directory.CreateDirectory(copySource);
    var originalBytes = Enumerable.Range(0, 3 * 1024 * 1024).Select(i => (byte)(i % 251)).ToArray();
    File.WriteAllBytes(Path.Combine(copySource, "large.bin"), originalBytes);
    using (var copyCancel = new CancellationTokenSource())
    {
        var copyTarget = Path.Combine(root, "cancelled-copy");
        var copyReporter = new QuietReporter { OnProgress = _ => copyCancel.Cancel() };
        try { await PreservationPipeline.CopyTreeAsync(copySource, copyTarget, copyReporter, copyCancel.Token); }
        catch (OperationCanceledException) { }
        Check(File.ReadAllBytes(Path.Combine(copySource, "large.bin")).SequenceEqual(originalBytes) &&
            new FileInfo(Path.Combine(copyTarget, "large.bin")).Length < originalBytes.Length,
            "cross-drive copy cancels within a file and preserves its source");
    }
    var copiedTarget = Path.Combine(root, "complete-copy");
    await PreservationPipeline.CopyTreeAsync(copySource, copiedTarget, reporter, default);
    Check(File.ReadAllBytes(Path.Combine(copiedTarget, "large.bin")).SequenceEqual(originalBytes), "cross-drive copy preserves all bytes");
    var tailPath = Path.Combine(root, "unicode-log.txt");
    var tail = LogTail.FromEnd(tailPath);
    var unicodeLine = Encoding.UTF8.GetBytes("folder/日/complete\n");
    File.WriteAllBytes(tailPath, unicodeLine[..8]);
    Check(tail.ReadNewLines().Count == 0, "partial log line waits for its terminator");
    using (var append = new FileStream(tailPath, FileMode.Append)) append.Write(unicodeLine[8..]);
    Check(tail.ReadNewLines().Single() == "folder/日/complete", "UTF-8 paths survive fragmented log writes");
    File.WriteAllText(tailPath, "new\n");
    Check(tail.ReadNewLines().Single() == "new", "log truncation resets pending decoder state");
    var resultPage = SteamOpenId.BuildResultPage(true, "test-style-nonce");
    Check(resultPage.Contains("background:#0c0e13") && resultPage.Contains("Return to the launcher") &&
        resultPage.Contains("nonce=\"test-style-nonce\"") && !resultPage.Contains("<script"), "themed redirect needs no scripts or external resources");
    Check(SteamOpenId.BuildResultPage(false, "nonce").Contains("NO CHANGES MADE"), "cancellation page has a clear retry path");
    if (args.Length == 2 && args[0] == "--preview-dir")
    {
        Directory.CreateDirectory(args[1]);
        File.WriteAllText(Path.Combine(args[1], "success.html"), resultPage);
        File.WriteAllText(Path.Combine(args[1], "cancelled.html"), SteamOpenId.BuildResultPage(false, "nonce"));
    }
    Console.WriteLine($"All {passed} security checks passed.");
}
finally
{
    if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
        throw new Exception("Invalid test cleanup path.");
    Directory.Delete(root, recursive: true);
}

sealed class FakeValve : HttpMessageHandler
{
    public int Calls; public bool Valid = true; public bool Duplicate; public string LastBody = "";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.AbsoluteUri != SteamOpenId.Endpoint || request.Method != HttpMethod.Post)
            throw new Exception("Unexpected verification destination");
        Calls++; LastBody = await request.Content!.ReadAsStringAsync(ct);
        return new(HttpStatusCode.OK) { Content = new StringContent("ns:" + SteamOpenId.Namespace +
            "\nis_valid:" + (Valid ? "true" : "false") + "\n" + (Duplicate ? "is_valid:false\n" : "")) };
    }
}
sealed class QuietReporter : IReporter
{
    public Action<StepProgress>? OnProgress;
    public StepProgress? LastProgress;
    public void Log(string text, LogLevel level = LogLevel.Info) { }
    public void Step(string name) { }
    public void Progress(StepProgress progress) { LastProgress = progress; OnProgress?.Invoke(progress); }
}
