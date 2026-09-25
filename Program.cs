using System.Windows;
using Velopack;

namespace KpcLauncher;

public static class Program
{
    internal static bool SmokeTest;
    [STAThread]
    public static void Main(string[] args)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Core.TesterPackageHost.PipeVariable)))
        {
            Environment.ExitCode = Core.TesterPackageHost.RunWorker(args);
            return;
        }
        VelopackApp.Build().Run();
        SmokeTest = args.Contains("--smoke-test");

        var application = new App();
        application.InitializeComponent();
        application.Run();
    }
}
