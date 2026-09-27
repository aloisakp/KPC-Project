namespace KpcLauncher.Core;

/// <summary>Desktop operations supplied by the Windows or Linux interface.</summary>
public static class DesktopUi
{
    public static Action<Action> Post { get; set; } = action => action();
    public static Func<bool> CheckAccess { get; set; } = () => true;
    public static Func<string, string, Task<string?>> PickFolder { get; set; } = (_, _) => Task.FromResult<string?>(null);
    public static Func<string, string, bool, Task<bool>> Dialog { get; set; } = (_, _, _) => Task.FromResult(false);
    public static Func<IReadOnlyList<CharacterExportSource>, Task<CharacterExportSource?>> ChooseExportSource { get; set; } = _ => Task.FromResult<CharacterExportSource?>(null);
    internal static async Task<CharacterExportSource?> SelectExportSourceAsync(IReadOnlyList<CharacterExportSource> sources)
    {
        if (sources.Count == 0) throw new TesterException("No installed retail copy of KurtzPel was found. Install it through Steam or Epic Games, then try Export character again.");
        if (sources.Count == 1) return sources[0];
        var chosen = await ChooseExportSource(sources);
        return chosen is null ? null : CharacterExportSources.Resolve(chosen, sources);
    }
    public static Task<bool> Confirm(string title, string message) => Dialog(title, message, true);
    public static Task Inform(string title, string message) => Dialog(title, message, false);
}
