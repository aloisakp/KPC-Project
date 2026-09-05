using System.Windows;
using System.Windows.Threading;
using KpcLauncher.Core;

namespace KpcLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        CrashLog.Start();
        CrashLog.Write("startup");

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
    }

    private static void ShowFatal(string context, Exception? exception)
    {
        CrashLog.WriteException(context, exception);
        MessageBox.Show(
            $"KPC Launcher encountered an unexpected error.\n\n{exception?.Message}\n\nLog: {CrashLog.Path}",
            "KPC Launcher",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatal("unhandled UI exception", e.Exception);
        e.Handled = true;
        Shutdown(1);
    }

    private static void OnDomainException(object? sender, UnhandledExceptionEventArgs e) =>
        ShowFatal("unhandled domain exception", e.ExceptionObject as Exception);

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLog.WriteException("unobserved task exception", e.Exception);
        e.SetObserved();
    }
}
