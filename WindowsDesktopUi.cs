using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        DesktopUi.ChooseExportSource = async sources => await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CharacterExportSource? selected = null;
            var dialog = CreateExportSourceDialog(sources, source => selected = source);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
            return selected;
        });
    }

    internal static Window CreateExportSourceDialog(IReadOnlyList<CharacterExportSource> sources, Action<CharacterExportSource> select)
    {
        var dialog = new Window
        {
            Title = "Export character", Width = 540, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Background = new SolidColorBrush(Color.FromRgb(18, 22, 31)),
            Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = 14,
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Which copy would you like to export from?", FontSize = 20,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "We found these KurtzPel installations. Choose the one with your character, then enter the lobby.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 16), Foreground = Brushes.LightGray });
        foreach (var source in sources)
        {
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = source.DisplayName, FontSize = 17, FontWeight = FontWeights.SemiBold });
            label.Children.Add(new TextBlock { Text = System.IO.Path.GetDirectoryName(source.Executable), FontSize = 11,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            var button = new Button { Content = label, Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10),
                HorizontalContentAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromRgb(232, 237, 245)),
                Foreground = Brushes.Black };
            button.Click += (_, _) => { select(source); dialog.Close(); };
            panel.Children.Add(button);
        }
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88, Padding = new Thickness(14, 7, 14, 7),
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        cancel.Click += (_, _) => dialog.Close();
        panel.Children.Add(cancel);
        dialog.Content = panel;
        return dialog;
    }
}
