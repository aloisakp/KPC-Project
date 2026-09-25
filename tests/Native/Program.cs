using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using KpcLauncher.Core;
using KpcLauncher.Setup;

if (!OperatingSystem.IsLinux()) throw new Exception("Run the native tests on Linux.");
var root = Path.Combine(Path.GetTempPath(), "kpc-native-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var count = 0;
void Check(bool passed, string message) { if (!passed) throw new Exception(message); Console.WriteLine("PASS: " + message); count++; }
void Reject(Action action, string message) { try { action(); } catch (Exception ex) when (ex is IOException or SteamDownloadException or System.Text.Json.JsonException) { Check(true, message); return; } throw new Exception("Accepted: " + message); }
MemoryStream Payload(string? invalid = null)
{
    var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        foreach (var name in invalid is null ? new[] { "KpcLauncher.Linux", "game-worker/KpcGameWorker.exe" } : new[] { invalid })
        { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("test payload, never executed"); }
    stream.Position = 0; return stream;
}
try
{
    var data = Path.Combine(root, "Steam with spaces é");
    Directory.CreateDirectory(Path.Combine(data, "steamapps")); Directory.CreateDirectory(Path.Combine(data, "ubuntu12_32")); Directory.CreateDirectory(Path.Combine(data, "logs"));
    Check(SteamInstall.FromFolder(data) is null, "native Steam requires an ELF binary, not steam.exe");
    var binary = Path.Combine(data, "ubuntu12_32", "steam"); File.Copy("/bin/sleep", binary);
    File.SetUnixFileMode(binary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    var steam = SteamInstall.FromFolder(data)!;
    Check(steam is not null && steam.IsNativeLinux, "native Steam data root detected without Wine");
    var alias = Path.Combine(root, "steam-alias"); Directory.CreateSymbolicLink(alias, data);
    Check(SteamInstall.FromFolder(alias)?.Root == data, "standard Steam symlink aliases resolve before validation");
    Check(SteamInstall.FromFolder("/usr/bin") is null, "executable directory is not confused with Steam data");
    using (var process = Process.Start(new ProcessStartInfo(binary) { ArgumentList = { "60" } })!)
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var log = Path.Combine(data, "logs", "connection_log.txt");
            File.WriteAllText(log, $"[{stamp}] [Logged On, 4, 7] [U:1:123] logged in\n");
            Check(steam!.LiveProcess()?.Pid == process.Id, "Steam identity is tied to a live process from the selected data root");
            Check(steam.ActiveSteamId == SteamOpenId.IndividualBase + 123, "live connected account accepted");
            File.AppendAllText(log, "ConnectionDisconnected( test\n"); Check(steam.ActiveSteamId is null, "disconnect rejects cached account");
            File.WriteAllText(log, "[2000-01-01 00:00:00] [Logged On, 4, 7] [U:1:123] stale\n");
            Check(steam.ActiveSteamId is null, "previous-process identity rejected");
            File.WriteAllText(steam.ConsoleLog, $"[{stamp}] ExecCommandLine: +download_depot 844870 844871 4819182874103212568\n");
            Reject(() => steam.RequireDepotIdle(844870, 844871), "an unfinished depot request blocks staging changes");
            File.AppendAllText(steam.ConsoleLog, $"[{stamp}] Depot download complete : \"{steam.StagingDirectory(844870,844871)}\" (manifest 4819182874103212568)\n");
            steam.RequireDepotIdle(844870,844871); Check(true, "exact staging completion clears the pending request");
        }
        finally { if (!process.HasExited) process.Kill(); process.WaitForExit(); }
    }
    Check(steam!.ActiveSteamId is null && !steam.IsClientRunning, "exited Steam invalidates identity");
    Check(!SafePaths.Same(Path.Combine(root, "A"), Path.Combine(root, "a")), "Linux paths are case sensitive");
    Check(!steam.MatchesStaging(steam.StagingDirectory(844870,844871).ToUpperInvariant(),844870,844871), "case mismatch cannot authorize a staging directory");
    var content = Path.Combine(data, "steamapps", "content"); Directory.CreateSymbolicLink(content, root);
    Check(SteamInstall.FromFolder(data) is null, "redirected Steam staging rejected"); Directory.Delete(content);

    var destination = Path.Combine(root, "Custom launcher é"); var applications = Path.Combine(root, "applications");
    using var firstPayload = Payload(); var first = InstallEngine.Install(firstPayload, destination, "0.7.0", applications);
    Check(File.Exists(first.Executable) && File.Exists(first.DesktopFile), "custom location installs a native app and menu shortcut");
    Check((File.GetUnixFileMode(first.Executable) & UnixFileMode.UserExecute) != 0, "installed launcher is executable");
    using var nextPayload = Payload(); var next = InstallEngine.Install(nextPayload, destination, "0.7.1", applications);
    Check(first.Executable != next.Executable && File.Exists(first.Executable), "updates preserve the previous version");
    Check(File.ReadAllText(next.DesktopFile).Contains(InstallEngine.DesktopQuote(next.Executable)), "update shortcut points at the installed version");
    Check(InstallEngine.DesktopQuote("/a/$b/%c/\"d").Contains("\\$b/%%c/\\\"d"), "desktop paths escape expansion and field codes");
    var other = Path.Combine(root, "unrelated"); Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, "keep"), "preserve");
    Reject(() => InstallEngine.Validate(other), "installer refuses nonempty unrelated folders");
    Reject(() => InstallEngine.Validate("/"), "installer refuses filesystem root");
    Reject(() => InstallEngine.Validate("relative"), "installer refuses relative paths");
    var link = Path.Combine(root, "install-link"); Directory.CreateSymbolicLink(link, destination);
    Reject(() => InstallEngine.Validate(link), "installer refuses symlink destinations");
    foreach (var unsafePath in new[] { "../escape", "/escape", "a/../../escape", "a\\escape", "a//b" })
    {
        using var bad = Payload(unsafePath);
        Reject(() => InstallEngine.Install(bad, destination, "0.7.2", applications), "installer rejects payload path " + unsafePath);
    }
    Check(File.Exists(next.Executable) && File.ReadAllText(Path.Combine(other, "keep")) == "preserve", "failed installs preserve working version and unrelated files");

    using var channel = new WorkerChannel();
    var start = new ProcessStartInfo(); channel.Configure(start);
    using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var serve = channel.ServeAsync(new byte[] { 1, 2 }, new byte[] { 3, 4 }, lifetime.Token);
    var port = int.Parse(start.Environment[WorkerChannel.PortVariable]!);
    using (var wrong = new TcpClient())
    {
        await wrong.ConnectAsync(IPAddress.Loopback, port); await wrong.GetStream().WriteAsync(new byte[32]);
        Check(await wrong.GetStream().ReadAsync(new byte[1]) == 0, "worker channel releases no private payload without its random token");
    }
    using (var valid = new TcpClient())
    {
        await valid.ConnectAsync(IPAddress.Loopback, port);
        await valid.GetStream().WriteAsync(Convert.FromHexString(start.Environment[WorkerChannel.TokenVariable]!));
        var bytes = new byte[12]; await valid.GetStream().ReadExactlyAsync(bytes);
        Check(BitConverter.ToInt32(bytes,0)==2 && bytes[4]==1 && BitConverter.ToInt32(bytes,6)==2 && bytes[10]==3, "authorized worker receives length-framed memory payload");
    }
    lifetime.Cancel(); await serve;
    Console.WriteLine($"All {count} native Linux checks passed.");
}
finally { Directory.Delete(root, true); }
