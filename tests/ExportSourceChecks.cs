using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KpcLauncher;
using KpcLauncher.Core;

internal static class ExportSourceChecks
{
    public static async Task Run(Action<bool, string> check, string root, string? previewDirectory)
    {
        var steam = Path.Combine(root, "Steam library");
        var steamGame = Path.Combine(steam, "steamapps", "common", "KurtzPel", CharacterExportSources.GameRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(steamGame)!); File.WriteAllText(steamGame, "fixture, never executed");
        File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_844870.acf"), "\"AppState\" { \"appid\" \"844870\" \"installdir\" \"KurtzPel\" }");
        var epicRoot = Path.Combine(root, "Epic Games", "KurtzPel");
        var epicGame = Path.Combine(epicRoot, CharacterExportSources.GameRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(epicGame)!); File.WriteAllText(epicGame, "fixture, never executed");
        var manifests = Path.Combine(root, "Epic manifests"); Directory.CreateDirectory(manifests);
        var epicManifest = new Dictionary<string, object?>
        {
            ["DisplayName"] = "KurtzPel", ["AppName"] = "test-kurtzpel-app", ["MainGameAppName"] = "test-kurtzpel-app",
            ["CatalogNamespace"] = "test-namespace", ["CatalogItemId"] = "test-item", ["InstallLocation"] = epicRoot,
            ["bIsIncompleteInstall"] = false, ["LaunchCommand"] = "never-forward-this", ["OwnershipToken"] = "never-read-this",
        };
        string Manifest(string? key = null, object? value = null)
        {
            var data = new Dictionary<string, object?>(epicManifest);
            if (key is not null) data[key] = value;
            return JsonSerializer.Serialize(data);
        }
        var item = Path.Combine(manifests, "kurtzpel.item"); File.WriteAllText(item, Manifest());
        File.WriteAllText(Path.Combine(manifests, "duplicate.item"), Manifest());
        File.WriteAllText(Path.Combine(manifests, "broken.item"), "{invalid");
        File.WriteAllText(Path.Combine(manifests, "array.item"), "[]");
        File.WriteAllText(Path.Combine(manifests, "other.item"), Manifest("DisplayName", "Another game"));
        File.WriteAllText(Path.Combine(manifests, "incomplete.item"), Manifest("bIsIncompleteInstall", true));
        File.WriteAllText(Path.Combine(manifests, "oversized.item"), new string(' ', 1024 * 1024 + 1));
        var steamExe = Path.Combine(steam, "steam.exe");
        var both = CharacterExportSources.Discover(steamExe, [manifests, manifests]);
        check(both.Count == 2 && both[0].Platform == "steam" && both[1].Platform == "epic",
            "discovery finds both retail copies, deduplicates Epic manifests and ignores unrelated, malformed and incomplete entries");
        check(both[1].Executable == epicGame && both[1].EpicAppId == "test-namespace:test-item:test-kurtzpel-app",
            "Epic discovery pins the shipping executable and complete catalog identity");
        check(CharacterExportSources.Discover(steamExe, []).Single().Platform == "steam", "Steam-only export is preserved");
        check(CharacterExportSources.Discover(null, [manifests]).Single().Platform == "epic", "Epic retail export does not require a Steam retail installation");
        check(CharacterExportSources.Discover(null, [Path.Combine(root, "absent")]).Count == 0, "missing manifests do not invent an Epic installation");
        foreach (var pair in new (string Key, object? Value)[] {
            ("AppName", "x?other=game"), ("AppName", "x\n"), ("AppName", 12), ("MainGameAppName", "another-app"),
            ("InstallLocation", "relative-folder"), ("InstallLocation", Path.Combine(root, "missing-game")),
            ("bIsIncompleteInstall", "false"), ("CatalogItemId", ""), ("CatalogNamespace", "ns:extra") })
            check(CharacterExportSources.ReadEpicManifest(Manifest(pair.Key, pair.Value)) is null, "Epic discovery rejects invalid " + pair.Key + " " + pair.Value);
        check(CharacterExportSources.EpicLaunchUri(both[1].EpicAppId!) ==
            "com.epicgames.launcher://apps/test-namespace%3Atest-item%3Atest-kurtzpel-app?action=launch&silent=true", "Epic launch uses the selected escaped identity");
        foreach (var invalid in new[] { "", "a:b", "a:b:c:d", "x?other=y", "x\"", "x y", "x\n" })
        {
            var rejected = false;
            try { CharacterExportSources.EpicLaunchUri(invalid); } catch (IOException) { rejected = true; }
            check(rejected, "Epic URI rejects malformed identity " + JsonSerializer.Serialize(invalid));
        }
        const ulong account = 76561198000000001;
        CharacterExporter.RequireRetailAccount(both[1], new SteamInstall(steam, steamExe,
            () => throw new Exception("Epic export must not inspect Steam's retail session")), account);
        check(true, "Epic capture leaves retail authentication to Epic while retaining community Steam identity");
        CharacterExporter.RequireRetailAccount(both[0], new SteamInstall(steam, steamExe, () => account), account);
        check(true, "Steam capture accepts the matching desktop account");
        var mismatch = false;
        try { CharacterExporter.RequireRetailAccount(both[0], new SteamInstall(steam, steamExe, () => account + 1), account); }
        catch (IOException) { mismatch = true; }
        check(mismatch, "Steam capture still refuses a different desktop account");
        var request = JsonSerializer.Serialize(new { exportSource = both[1] }, TesterClient.Json);
        using (var document = JsonDocument.Parse(request))
            check(CharacterExportSources.Resolve(document.RootElement.GetProperty("exportSource").Deserialize<CharacterExportSource>(TesterClient.Json)!, both) == both[1],
                "source survives the launcher-to-worker request and resolves to the same installation");
        foreach (var changed in new[] { both[1] with { Executable = steamGame }, both[1] with { EpicAppId = "another-app" }, both[1] with { Platform = "unknown" } })
        {
            var rejected = false;
            try { CharacterExportSources.Resolve(changed, both); } catch (IOException) { rejected = true; }
            check(rejected, "worker rejects a changed source " + changed.Platform + "/" + changed.EpicAppId);
        }
        var oldChooser = DesktopUi.ChooseExportSource;
        var prompts = 0;
        try
        {
            DesktopUi.ChooseExportSource = choices => { prompts++; return Task.FromResult<CharacterExportSource?>(choices[1]); };
            check(await DesktopUi.SelectExportSourceAsync([both[0]]) == both[0] && prompts == 0, "one Steam copy exports without a source prompt");
            check(await DesktopUi.SelectExportSourceAsync([both[1]]) == both[1] && prompts == 0, "one Epic copy exports without a source prompt");
            check(await DesktopUi.SelectExportSourceAsync(both) == both[1] && prompts == 1, "both copies prompt and preserve the Epic selection");
            DesktopUi.ChooseExportSource = _ => Task.FromResult<CharacterExportSource?>(both[0]);
            check(await DesktopUi.SelectExportSourceAsync(both) == both[0], "both copies also preserve the Steam selection");
            DesktopUi.ChooseExportSource = _ => Task.FromResult<CharacterExportSource?>(null);
            check(await DesktopUi.SelectExportSourceAsync(both) is null, "cancel returns no source and never falls back to Steam");
            var missing = false;
            try { await DesktopUi.SelectExportSourceAsync([]); } catch (TesterException) { missing = true; }
            check(missing, "no retail copy reports an actionable error");
        }
        finally { DesktopUi.ChooseExportSource = oldChooser; }
        // Read the launcher path from the registered handler but never execute registry-supplied commands.
        var epicLauncher = Path.Combine(root, "Epic launcher", "EpicGamesLauncher.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(epicLauncher)!); File.WriteAllText(epicLauncher, "fixture, never executed");
        foreach (var tail in new[] { "\"%1\"", "%1", "-uri=\"%1\"", "-uri=%1", "-uri \"%1\"", "-uri %1" })
        {
            var launch = CharacterExportSources.ParseEpicLauncherCommand("\"" + epicLauncher + "\" " + tail, both[1].EpicAppId!);
            check(launch.Executable == epicLauncher && launch.Arguments.Last().EndsWith(CharacterExportSources.EpicLaunchUri(both[1].EpicAppId!), StringComparison.Ordinal),
                "Epic handler safely expands supported argument form " + tail);
        }
        foreach (var command in new[] { "cmd.exe /c anything", "\"" + epicLauncher + "\" --unexpected \"%1\"", "\"" + epicLauncher + "\"", "\"" + epicLauncher + ".missing\" \"%1\"" })
        {
            var rejected = false;
            try { CharacterExportSources.ParseEpicLauncherCommand(command, both[1].EpicAppId!); } catch (IOException) { rejected = true; }
            check(rejected, "Epic launch refuses a missing or unexpected registered handler");
        }
        File.Delete(epicGame);
        var stale = false;
        try { CharacterExportSources.Resolve(both[1], CharacterExportSources.Discover(steamExe, [manifests])); } catch (IOException) { stale = true; }
        check(stale, "worker refuses an Epic installation removed after source selection");
        if (previewDirectory is not null) RenderDialog(previewDirectory);
    }

    private static void RenderDialog(string directory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dialog = WindowsDesktopUi.CreateExportSourceDialog([
                    new("steam", @"G:\Steam\steamapps\common\KurtzPel\TheChase\Binaries\Win64\TheChase-Win64-Shipping.exe"),
                    new("epic", @"D:\Epic Games\KurtzPel\TheChase\Binaries\Win64\TheChase-Win64-Shipping.exe", "fixture")], _ => { });
                var content = (FrameworkElement)dialog.Content;
                content.Measure(new Size(520, double.PositiveInfinity));
                var height = (int)Math.Ceiling(content.DesiredSize.Height);
                content.Arrange(new Rect(0, 0, 520, height));
                content.UpdateLayout();
                var image = new RenderTargetBitmap(520, height, 96, 96, PixelFormats.Pbgra32);
                var background = new DrawingVisual();
                using (var drawing = background.RenderOpen()) drawing.DrawRectangle(dialog.Background, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
                image.Render(background); image.Render(content);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                Directory.CreateDirectory(directory);
                using var output = File.Create(Path.Combine(directory, "export-source-dialog.png")); png.Save(output);
                dialog.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw failure;
    }
}
