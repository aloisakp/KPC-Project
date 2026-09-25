namespace KpcLauncher.Core;

/// <summary>Desktop operations supplied by the Windows or Linux interface.</summary>
public static class DesktopUi
{
    public static Action<Action> Post { get; set; } = action => action();
    public static Func<bool> CheckAccess { get; set; } = () => true;
    public static Func<string, string, Task<string?>> PickFolder { get; set; } = (_, _) => Task.FromResult<string?>(null);
    public static Func<string, string, bool, Task<bool>> Dialog { get; set; } = (_, _, _) => Task.FromResult(false);
    public static Task<bool> Confirm(string title, string message) => Dialog(title, message, true);
    public static Task Inform(string title, string message) => Dialog(title, message, false);
}
