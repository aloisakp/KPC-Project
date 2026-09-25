using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using System.Diagnostics;
using System.Reflection;

namespace KpcLauncher.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsLinux()) { Console.Error.WriteLine("Use the Windows installer on Windows."); return 1; }
        if (args.Length == 2 && args[0] == "--install-to")
        {
            using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip") ?? throw new IOException("Installer payload is missing.");
            var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
            var result = InstallEngine.Install(payload, args[1], version, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "applications"));
            Console.WriteLine(result.Executable);
            return 0;
        }
        SetupApp.Destination = args.Length == 2 && args[0] == "--destination" ? args[1] : null;
        SetupApp.SmokeTest = args.Contains("--smoke-test");
        return AppBuilder.Configure<SetupApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }
}
public sealed class SetupApp : Application
{
    internal static string? Destination;
    internal static bool SmokeTest;
    public override void Initialize() { RequestedThemeVariant = ThemeVariant.Dark; Styles.Add(new FluentTheme()); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new SetupWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
internal sealed class SetupWindow : Window
{
    private readonly TextBox path = new() { Text = SetupApp.Destination ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KPCLauncher", "app") };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button install = new() { Content = "Install", HorizontalAlignment = HorizontalAlignment.Right };
    private bool installing;
    private string? installedExecutable;
    public SetupWindow()
    {
        Title = "Install KPC Launcher for Linux"; Width = 640; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var browse = new Button { Content = "Choose folder…" };
        browse.Click += async (_, _) =>
        {
            try
            {
                var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose where to install KPC Launcher", AllowMultiple = false });
                if (result.FirstOrDefault()?.TryGetLocalPath() is { } chosen) path.Text = Path.Combine(chosen, "KPCLauncher");
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        install.Click += async (_, _) => await Install();
        Content = new StackPanel { Margin = new Thickness(28), Spacing = 18, Children = {
            new TextBlock { Text = "KPC Launcher", FontSize = 28, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = "Linux · Choose where to install the native launcher.\nAn entry will be added to your applications menu. Game storage is selected separately in the launcher.", TextWrapping = TextWrapping.Wrap },
            path, browse, status, install } };
        Closing += (_, e) => { if (installing) e.Cancel = true; };
        Opened += async (_, _) =>
        {
            if (!SetupApp.SmokeTest) return;
            await Task.Delay(1500);
            if (Environment.GetEnvironmentVariable("KPC_SMOKE_SCREENSHOT") is { Length: > 0 } file)
            {
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height));
                bitmap.Render(this); bitmap.Save(file);
            }
            Close();
        };
    }
    private async Task Install()
    {
        try
        {
            if (installedExecutable is not null)
            {
                Process.Start(new ProcessStartInfo(installedExecutable) { UseShellExecute = false })?.Dispose();
                return;
            }
            var destination = InstallEngine.Validate(path.Text ?? "");
            install.IsEnabled = false; path.IsEnabled = false; installing = true; status.Text = "Installing…";
            var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
            var result = await Task.Run(() =>
            {
                using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip") ?? throw new IOException("Installer payload is missing.");
                return InstallEngine.Install(payload, destination, version, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "applications"));
            });
            status.Text = "Installed. Open KPC Launcher from your applications menu.";
            installedExecutable = result.Executable;
            install.Content = "Open launcher"; install.IsEnabled = true;
        }
        catch (Exception ex) { status.Text = ex.Message; install.IsEnabled = true; path.IsEnabled = true; }
        finally { installing = false; }
    }
}
