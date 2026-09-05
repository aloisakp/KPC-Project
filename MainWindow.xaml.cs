using System.Windows;

namespace KpcLauncher;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.LogAppended += (_, _) => LogScroller.ScrollToEnd();
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_initialized) return;
        _initialized = true;
        await _viewModel.InitializeAsync();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        var maximised = WindowState == WindowState.Maximized;
        RootBorder.Margin = maximised ? new Thickness(8) : new Thickness(0);
        RootBorder.BorderThickness = maximised ? new Thickness(0) : new Thickness(1);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.TrySaveConfig();
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
