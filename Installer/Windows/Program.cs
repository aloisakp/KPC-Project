using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace KpcLauncher.Setup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args) => new Application().Run(new SetupWindow(args.Contains("--smoke-test")));
}
internal sealed class SetupWindow : Window
{
    private readonly TextBox path = new() { Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KPCLauncher"), Margin = new Thickness(0, 12, 0, 12), Padding = new Thickness(8) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 16) };
    private readonly Button install = new() { Content = "Install", Padding = new Thickness(24, 8, 24, 8), HorizontalAlignment = HorizontalAlignment.Right };
    private bool installing;
    public SetupWindow(bool smokeTest = false)
    {
        Title = "Install KPC Launcher for Windows"; Width = 620; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var browse = new Button { Content = "Choose folder…", Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Left };
        browse.Click += (_, _) => { var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the launcher installation folder" }; if (picker.ShowDialog(this) == true) path.Text = Path.Combine(picker.FolderName, "KPCLauncher"); };
        install.Click += async (_, _) => await Install();
        Content = new StackPanel { Margin = new Thickness(28), Children = {
            new TextBlock { Text = "KPC Launcher", FontSize = 28, FontWeight = FontWeights.SemiBold },
            new TextBlock { Text = "Windows · Choose where to install the launcher.\nGame storage is selected separately inside the launcher.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 8) },
            path, browse, status, install } };
        Closing += (_, e) => { if (installing) e.Cancel = true; };
        ContentRendered += async (_, _) =>
        {
            if (!smokeTest) return;
            await Task.Delay(1000);
            if (Environment.GetEnvironmentVariable("KPC_SMOKE_SCREENSHOT") is { Length: > 0 } file)
            {
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(this);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var output = File.Create(file); encoder.Save(output);
            }
            Close();
        };
    }
    private static string Validate(string value)
    {
        if (!Path.IsPathFullyQualified(value)) throw new IOException("Choose an absolute installation path.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        if (root == Path.GetPathRoot(root)) throw new IOException("Choose a launcher subfolder, not the drive root.");
        for (var item = new DirectoryInfo(root); item is not null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Choose a regular folder without links or junctions.");
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any() &&
            !(File.Exists(Path.Combine(root, "Update.exe")) && File.Exists(Path.Combine(root, "current", "KpcLauncher.exe"))))
            throw new IOException("Choose an empty folder or an existing KPC Launcher installation. Other files will not be replaced.");
        return root;
    }
    private async Task Install()
    {
        string? temporary = null;
        try
        {
            var destination = Validate(path.Text);
            installing = true; install.IsEnabled = false; path.IsEnabled = false; status.Text = "Installing…";
            temporary = Path.Combine(Path.GetTempPath(), "kpc-setup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
            var setup = Path.Combine(temporary, "Setup.exe");
            await using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("setup.exe") ?? throw new IOException("Installer payload is missing."))
            await using (var target = new FileStream(setup, FileMode.CreateNew)) await source.CopyToAsync(target);
            var start = new ProcessStartInfo(setup) { UseShellExecute = false };
            start.ArgumentList.Add("--installto"); start.ArgumentList.Add(destination);
            using var process = Process.Start(start) ?? throw new IOException("Could not start installation.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new IOException("Installation did not complete. Check folder permissions and retry.");
            status.Text = "Installation complete. You can open KPC Launcher from the Start menu.";
            install.Content = "Installed";
        }
        catch (Exception ex) { status.Text = ex.Message; install.IsEnabled = true; path.IsEnabled = true; }
        finally
        {
            installing = false;
            if (temporary is not null)
                try { File.Delete(Path.Combine(temporary, "Setup.exe")); Directory.Delete(temporary); } catch (IOException) { }
        }
    }
}
