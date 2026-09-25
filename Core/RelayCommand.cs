using System.Windows.Input;

namespace KpcLauncher.Core;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    private static event EventHandler? RequerySuggested;
    public static void Requery() => RequerySuggested?.Invoke(null, EventArgs.Empty);
    public event EventHandler? CanExecuteChanged
    {
        add => RequerySuggested += value;
        remove => RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();
}


