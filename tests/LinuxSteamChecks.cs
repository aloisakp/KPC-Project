using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using KpcLauncher;
using KpcLauncher.Core;

internal static class LinuxSteamChecks
{
    internal static async Task Run(Action<bool, string> check, string root)
    {
        var oldAddress = Environment.GetEnvironmentVariable(LinuxSteamBridge.AddressVariable);
        var oldToken = Environment.GetEnvironmentVariable(LinuxSteamBridge.TokenVariable);
        var originalConfig = LauncherConfig.Load();
        var steamRoot = Path.Combine(root, "Native Steam");
        Directory.CreateDirectory(steamRoot);
        Directory.CreateDirectory(Path.Combine(steamRoot, "logs"));
        const ulong account = 76561198000000001;
        string? currentAccount = account.ToString();
        var reportedRoot = steamRoot;
        var downloads = 0;
        var pathChecks = 0;
        var token = new string('a', 64);
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        var address = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(address);
        listener.Start();
        using var stop = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().WaitAsync(stop.Token); }
                catch (OperationCanceledException) { break; }
                using var body = await JsonDocument.ParseAsync(context.Request.InputStream);
                object response;
                if (context.Request.Headers["Authorization"] != "Bearer " + token)
                    throw new Exception("Missing Linux bridge capability");
                switch (context.Request.Url!.AbsolutePath)
                {
                    case "/status":
                    case "/select":
                        response = new { schema = 1, root = reportedRoot, unixRoot = "/home/test/Steam",
                            pid = 123, started = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeSeconds(), steamId = currentAccount };
                        break;
                    case "/path-match":
                        pathChecks++;
                        response = new { matches = body.RootElement.GetProperty("path").GetString() == "/home/test/Steam/steamapps/content/app_844870/depot_844871" };
                        break;
                    case "/download":
                        if (body.RootElement.GetProperty("steamId").GetString() != account.ToString())
                            throw new Exception("Download lost the authorized account");
                        downloads++;
                        response = new { ok = true };
                        break;
                    case "/idle":
                        response = new { ok = true };
                        break;
                    default: throw new Exception("Unexpected bridge route");
                }
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response));
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        });
        try
        {
            foreach (var invalid in new[] { "http://example.com/", "http://localhost/", "https://127.0.0.1/", address + "other", address + "?token=bad" })
            {
                var rejected = false;
                try { _ = new LinuxSteamBridge(invalid, token); } catch (SteamDownloadException) { rejected = true; }
                check(rejected, "native bridge refuses non-loopback or unexpected URLs");
            }
            Environment.SetEnvironmentVariable(LinuxSteamBridge.AddressVariable, address);
            Environment.SetEnvironmentVariable(LinuxSteamBridge.TokenVariable, token);
            var updater = new LauncherUpdater();
            check(await updater.CheckAsync() is null && !updater.IsInstalledBuild,
                "native helper mode cannot restart into an installer update without its helper");
            var steam = SteamInstall.Find()!;
            check(steam is { IsNativeLinux: true } && steam.Root == steamRoot && steam.ActiveSteamId == account,
                "native bridge accepts Steam data without steam.exe and reads connected identity");
            check(steam!.IsClientRunning, "native Steam running state comes from Linux helper");
            var authorization = new SteamAuthorization(account, DateTimeOffset.UtcNow);
            steam.DownloadDepot(LauncherConfig.AppId, LauncherConfig.DepotId, LauncherConfig.RequiredArchives[0].ManifestId, authorization);
            check(downloads == 1, "native depot request goes through helper with authorized account");
            currentAccount = (account + 1).ToString();
            var blocked = false;
            try { steam.DownloadDepot(LauncherConfig.AppId, LauncherConfig.DepotId, LauncherConfig.RequiredArchives[0].ManifestId, authorization); }
            catch (SteamDownloadException) { blocked = true; }
            check(blocked && downloads == 1, "native Steam account switch sends no further command");
            currentAccount = null;
            check(steam.ActiveSteamId is null, "native Steam logout invalidates account");
            currentAccount = account.ToString();
            check(steam.MatchesStaging("/home/test/Steam/steamapps/content/app_844870/depot_844871", LauncherConfig.AppId, LauncherConfig.DepotId)
                && !steam.MatchesStaging("/wrong", LauncherConfig.AppId, LauncherConfig.DepotId) && pathChecks == 2,
                "Linux completion paths are validated by native helper instead of Windows path normalization");
            using (var vm = new MainViewModel())
            {
                check(!vm.IsSteamMissing && vm.Config.NativeSteam, "native launcher restores selected data root");
                check(!vm.ExportCharacterCommand.CanExecute(null) && vm.ExportHelp.Contains("not yet supported"),
                    "native export limitation is explicit before launching an unsupported Proton process");
            }
            reportedRoot = root;
            check(steam.ActiveSteamId is null, "changed helper root fails closed for an existing Steam client");
        }
        finally
        {
            stop.Cancel();
            await server;
            listener.Stop();
            Environment.SetEnvironmentVariable(LinuxSteamBridge.AddressVariable, oldAddress);
            Environment.SetEnvironmentVariable(LinuxSteamBridge.TokenVariable, oldToken);
            originalConfig.Save();
        }
    }
}
