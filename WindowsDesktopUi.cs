using System.Windows;
using KpcLauncher.Core;

namespace KpcLauncher;

internal static class WindowsDesktopUi
{
    public static void Initialize()
    {
        DesktopUi.CheckAccess = () => Application.Current.Dispatcher.CheckAccess();
        DesktopUi.Post = action =>
        {
            if (DesktopUi.CheckAccess()) action();
            else Application.Current.Dispatcher.BeginInvoke(action);
        };
        DesktopUi.PickFolder = async (title, initial) => await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title, InitialDirectory = initial };
            return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
        });
        DesktopUi.Dialog = async (title, message, confirm) => await Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(Application.Current.MainWindow, message, title,
                confirm ? MessageBoxButton.YesNo : MessageBoxButton.OK,
                confirm ? MessageBoxImage.Question : MessageBoxImage.Information,
                confirm ? MessageBoxResult.No : MessageBoxResult.OK) == MessageBoxResult.Yes);
    }
}
