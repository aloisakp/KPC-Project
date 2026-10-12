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
        DesktopUi.ChooseExportSource = sources => OnUi(() => ChooseExportSourceAsync(sources));
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
    private Task<CharacterExportSource?> ChooseExportSourceAsync(IReadOnlyList<CharacterExportSource> sources)
    {
        var dialog = new Window { Title = "Export character", Width = 560, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children = {
            new TextBlock { Text = "Which copy would you like to export from?", FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "KurtzPel is installed in more than one Steam library. Choose the one with your character, then enter the lobby.", TextWrapping = TextWrapping.Wrap } } };
        foreach (var source in sources)
        {
            var folder = source.Executable[..^(CharacterExportSources.GameRelativePath.Length + 1)];
            var choice = new Button { Content = new TextBlock { Text = source.DisplayName + "\n" + folder, TextWrapping = TextWrapping.Wrap }, HorizontalAlignment = HorizontalAlignment.Stretch };
            choice.Click += (_, _) => dialog.Close(source);
            panel.Children.Add(choice);
        }
        var cancel = new Button { Content = "Cancel", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => dialog.Close(null);
        panel.Children.Add(cancel);
        dialog.Content = panel;
        return dialog.ShowDialog<CharacterExportSource?>(this);
    }
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
