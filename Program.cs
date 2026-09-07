using System.Windows;
using Velopack;

namespace KpcLauncher;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Core.TesterPackageHost.PipeVariable)))
        {
            Environment.ExitCode = Core.TesterPackageHost.RunWorker(args);
            return;
        }
        VelopackApp.Build().Run();

        var application = new App();
        application.InitializeComponent();
        application.Run();
    }
}
