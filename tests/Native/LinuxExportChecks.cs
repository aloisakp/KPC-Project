using System.Diagnostics;
using KpcLauncher.Core;

/// <summary>Native parts of Linux character export. tests/linux-export-check.sh runs it through Proton.</summary>
internal static class LinuxExportChecks
{
    public static async Task Run(Action<bool, string> check, string root, SteamInstall steam)
    {
        var data = steam.Root;
        var library = Path.Combine(root, "Second library é");
        Directory.CreateDirectory(Path.Combine(library, "steamapps"));
        File.WriteAllText(Path.Combine(data, "steamapps", "libraryfolders.vdf"),
            $"\"libraryfolders\"\n{{\n\t\"0\"\n\t{{\n\t\t\"path\"\t\t\"{data}\"\n\t}}\n\t\"1\"\n\t{{\n\t\t\"path\"\t\t\"{library}\"\n\t}}\n}}\n");
        check(CharacterExportSources.Discover(steam).Count == 0, "no retail copy is offered before KurtzPel is installed");
        File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_844870.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"844870\"\n\t\"installdir\"\t\t\"KurtzPel\"\n}\n");
        check(CharacterExportSources.Discover(steam).Count == 0, "an install record without the game executable is not offered");
        var game = Path.Combine(library, "steamapps", "common", "KurtzPel", CharacterExportSources.GameRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(game)!);
        File.WriteAllText(game, "fixture, never executed");
        var found = CharacterExportSources.Discover(steam);
        check(found.Count == 1 && found[0] == new CharacterExportSource("steam", game), "native Steam export finds the copy in another library");

        // A stand-in proton script records how the runtime invokes Proton.
        var calls = Path.Combine(root, "proton-calls.txt");
        var proton = Path.Combine(library, "steamapps", "common", "Proton 9.0", "proton");
        Directory.CreateDirectory(Path.GetDirectoryName(proton)!);
        File.WriteAllText(proton, $$"""
            #!/bin/sh
            printf '%s|%s|%s\n' "$*" "$STEAM_COMPAT_DATA_PATH" "$STEAM_COMPAT_LIBRARY_PATHS" >> '{{calls}}'
            [ "$1" = getcompatpath ] && printf 'Z:%s\r\n' "$(printf '%s' "$2" | tr / '\\')"
            exit 0
            """);
        File.SetUnixFileMode(proton, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var worker = Path.Combine(root, "worker", "KpcGameWorker.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(worker)!);
        File.WriteAllText(worker, "fixture, never executed");
        var runtimeRoot = Path.Combine(root, "runtime");
        var runtime = new GameRuntime(steam, runtimeRoot, worker);
        check(runtime.IsProton, "Proton in any Steam library is used for the game runtime");
        check(Throws(() => runtime.Worker(), "not prepared"), "the worker never starts in a Proton prefix that was not prepared");
        var storage = Path.Combine(root, "Storage é");
        check(await runtime.MapAsync(storage, default) == "Z:" + storage.Replace('/', '\\'), "Proton translates paths with getcompatpath, whose output reaches the launcher");
        check(File.ReadAllLines(calls).Single() == $"getcompatpath {storage}|{runtimeRoot}|{data}:{library}",
            "path translation does not go through Proton's Steam helper and passes the prefix and Steam libraries");
        var start = runtime.Worker();
        check(start.FileName == proton && start.ArgumentList.SequenceEqual(["runinprefix", worker, "--tester-worker"]) &&
            start.RedirectStandardOutput && start.RedirectStandardError, "the worker runs directly in the prefix, keeping its stdout and stderr");

        // Export preflight.
        var previousHome = Environment.GetEnvironmentVariable("HOME");
        var home = Path.Combine(root, "home");
        Directory.CreateDirectory(home);
        try
        {
            Environment.SetEnvironmentVariable("HOME", home);
            check(Throws(runtime.RequireExport, "steamclient.so is missing"), "export explains a missing Steam client library instead of hanging Proton's helper");
            Directory.CreateDirectory(Path.Combine(home, ".steam", "sdk64"));
            File.WriteAllText(Path.Combine(home, ".steam", "sdk64", "steamclient.so"), "fixture");
            runtime.RequireExport();
            check(true, "export preflight accepts Proton with the Steam client library");
            foreach (var command in new[] { "Z:\\Games\\KurtzPel\\TheChase\\Binaries\\Win64\\TheChase-Win64-Shipping.exe TheChase", "Z:\\x\\THECHASE-WIN64-SHIPPING.EXE\0TheChase",
                "\"C:\\KurtzPel\\TheChase\\Binaries\\Win64\\TheChase-Win64-Shipping.exe\"", "/home/u/KurtzPel/TheChase-Win64-Shipping.exe" })
                check(GameRuntime.IsRetailGameCommand(command), "a running KurtzPel is recognized from its Wine command line");
            foreach (var command in new[] { "less\0/tmp/TheChase-Win64-Shipping.exe", "Z:\\x\\TheChase-Win64-Shipping.exe.log", "Z:\\x\\NotTheChase-Win64-Shipping.exe" })
                check(!GameRuntime.IsRetailGameCommand(command), "other commands mentioning the game are not mistaken for it");
            using (var running = Process.Start(new ProcessStartInfo("/bin/bash")
                { ArgumentList = { "-c", "exec -a 'Z:\\Games\\TheChase-Win64-Shipping.exe' sleep 30" } })!)
            {
                try
                {
                    check(await Until(() => GameRuntime.RetailGameRunning()), "a running game process is found in /proc");
                    check(Throws(runtime.RequireExport, "Close KurtzPel"), "a running KurtzPel blocks export");
                }
                finally { running.Kill(); running.WaitForExit(); }
            }
            check(!GameRuntime.RetailGameRunning(), "a closed game no longer blocks export");
        }
        finally { Environment.SetEnvironmentVariable("HOME", previousHome); }

        // Without Proton the runtime falls back to Wine, which cannot reach native Steam for export.
        var plain = Path.Combine(root, "Steam without Proton");
        Directory.CreateDirectory(Path.Combine(plain, "steamapps"));
        Directory.CreateDirectory(Path.Combine(plain, "ubuntu12_32"));
        File.Copy("/bin/sleep", Path.Combine(plain, "ubuntu12_32", "steam"));
        var bin = Path.Combine(root, "wine-bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "wine"), """
            #!/bin/sh
            [ "$1" = winepath.exe ] && printf 'Z:%s\n' "$(printf '%s' "$3" | tr / '\\')"
            exit 0
            """);
        File.SetUnixFileMode(Path.Combine(bin, "wine"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var previousPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", bin + ":" + previousPath);
            var wine = new GameRuntime(SteamInstall.FromFolder(plain)!, Path.Combine(root, "wine-runtime"), worker);
            check(!wine.IsProton && await wine.MapAsync(storage, default) == "Z:" + storage.Replace('/', '\\'), "plain Wine still translates paths with winepath");
            var wineStart = wine.Worker();
            check(wineStart.FileName == Path.Combine(bin, "wine") && wineStart.ArgumentList.SequenceEqual([worker, "--tester-worker"]) &&
                wineStart.Environment["WINEPREFIX"] == Path.Combine(root, "wine-runtime", "wine"), "plain Wine starts the worker in its own prefix");
            check(Throws(wine.RequireExport, "needs Proton"), "export explains that Linux capture needs Proton");
        }
        finally { Environment.SetEnvironmentVariable("PATH", previousPath); }
    }

    private static bool Throws(Action action, string message)
    {
        try { action(); return false; }
        catch (TesterException ex) { return ex.Message.Contains(message); }
    }

    private static async Task<bool> Until(Func<bool> condition)
    {
        for (var deadline = DateTime.UtcNow.AddSeconds(10); DateTime.UtcNow < deadline; await Task.Delay(100))
            if (condition()) return true;
        return false;
    }
}
