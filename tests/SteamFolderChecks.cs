using KpcLauncher;
using KpcLauncher.Core;

internal static class SteamFolderChecks
{
    // Called only after Program establishes its isolated launcher settings root.
    public static void Run(Action<bool, string> check, string root)
    {
        var folder = Path.Combine(root, "Steam with spaces é");
        Directory.CreateDirectory(folder);
        check(SteamInstall.FromFolder(folder) is null, "Steam folder requires steam.exe");
        File.WriteAllText(Path.Combine(folder, "steam.exe"), "fixture - never executed");
        var install = SteamInstall.FromFolder(folder.Replace('\\', '/') + "/");
        check(install?.Root == folder && install.Executable == Path.Combine(folder, "steam.exe"),
            "manual Steam folder normalizes Wine-style separators and keeps executable under selected root");
        foreach (var invalid in new string?[] { null, "", "relative", "\0", Path.Combine(folder, "steamapps"), Path.Combine(folder, "steam.exe") })
            check(SteamInstall.FromFolder(invalid) is null, "invalid Steam folder is rejected");

        var config = LauncherConfig.Load();
        var storage = config.StorageRoot;
        config.SteamRoot = Path.Combine(root, "missing-steam");
        config.Save();
        using (var vm = new MainViewModel())
        {
            check(vm.IsSteamMissing && !vm.DownloadCommand.CanExecute(null), "missing saved Steam folder disables downloads");
            var changed = new HashSet<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            vm.SetSteamFolder(folder);
            check(!vm.IsSteamMissing && vm.SteamFolder == folder && vm.DownloadCommand.CanExecute(null),
                "manual selection immediately enables downloads without restart");
            check(changed.Contains(nameof(vm.SteamFolder)) && changed.Contains(nameof(vm.IsSteamMissing)),
                "manual selection refreshes folder and missing-Steam bindings");
            check(LauncherConfig.Load().SteamRoot == folder && LauncherConfig.Load().StorageRoot == storage,
                "Steam selection persists without changing download storage");
            var rejected = false;
            try { vm.SetSteamFolder(root); } catch (IOException) { rejected = true; }
            check(rejected && vm.SteamFolder == folder && LauncherConfig.Load().SteamRoot == folder,
                "invalid selection preserves previous active and saved Steam folder");
        }
        using (var restarted = new MainViewModel())
        {
            check(restarted.SteamFolder == folder && !restarted.IsSteamMissing, "launcher restores manual Steam choice on restart");
            File.Delete(Path.Combine(folder, "steam.exe"));
            check(SteamInstall.Find(folder) is null, "missing selected Steam never silently falls back to another install");
            restarted.SetSteamFolder(null);
            check(LauncherConfig.Load().SteamRoot == "", "automatic detection clears saved override");
        }
    }
}
