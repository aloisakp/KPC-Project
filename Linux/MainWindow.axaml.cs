using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KpcLauncher.Core;

namespace KpcLauncher.Linux;

public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DesktopUi.CheckAccess = () => Dispatcher.UIThread.CheckAccess();
        DesktopUi.Post = action => { if (Dispatcher.UIThread.CheckAccess()) action(); else Dispatcher.UIThread.Post(action); };
        DesktopUi.PickFolder = (title, initial) => OnUi(async () =>
        {
            var start = Directory.Exists(initial) ? await StorageProvider.TryGetFolderFromPathAsync(initial) : null;
            var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = start });
            return result.FirstOrDefault()?.TryGetLocalPath();
        });
        DesktopUi.Dialog = (title, message, confirm) => OnUi(() => DialogAsync(title, message, confirm));
        model = new MainViewModel(); DataContext = model;
        Opened += async (_, _) =>
        {
            if (App.SmokeTest)
            {
                await Task.Delay(1500);
                if (Environment.GetEnvironmentVariable("KPC_SMOKE_SCREENSHOT") is { Length: > 0 } file)
                {
                    using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height));
                    bitmap.Render(this); bitmap.Save(file);
                }
                Close();
            }
            else await model.InitializeAsync();
        };
        Closed += (_, _) => { model.TrySaveConfig(); model.Dispose(); };
    }
    private static async Task<T> OnUi<T>(Func<Task<T>> action) => await Dispatcher.UIThread.InvokeAsync(action);
    private Task<bool> DialogAsync(string title, string message, bool confirm)
    {
        var dialog = new Window { Title = title, Width = 520, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 12 };
        if (confirm)
        {
            var no = new Button { Content = "Cancel", IsCancel = true, IsDefault = true }; no.Click += (_, _) => dialog.Close(false); buttons.Children.Add(no);
        }
        var yes = new Button { Content = confirm ? "Continue" : "OK", IsDefault = !confirm }; yes.Click += (_, _) => dialog.Close(true); buttons.Children.Add(yes);
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 24, Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, buttons } };
        return dialog.ShowDialog<bool>(this);
    }
}
