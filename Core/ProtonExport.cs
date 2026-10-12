using System.Collections.Concurrent;
using System.IO;

namespace KpcLauncher.Core;

/// <summary>
/// Linux character export. The native launcher owns Steam; the Windows worker starts the retail game
/// inside its own Proton session, so the listener can attach to it as it does on Windows.
/// </summary>
internal static class ProtonExport
{
    // Worker request on stdout, launcher answer on the worker's stdin.
    internal const string AccountRequest = "KPC_ACCOUNT_CHECK";
    internal const string AccountMatches = "KPC_ACCOUNT ok";
    internal const string AccountMismatch = "KPC_ACCOUNT mismatch";

#if GAME_WORKER
    /// <summary>Proton's Steam helper. With SteamGameId set it registers the native Steam client for the game it starts.</summary>
    internal const string SteamHelper = @"C:\windows\system32\steam.exe";

    /// <summary>
    /// The launcher passes the Windows path of the shipping executable it found. Confirm that this
    /// path is the copy its Steam library manifest names before starting anything.
    /// </summary>
    internal static CharacterExportSource Resolve(CharacterExportSource requested)
    {
        if (requested.Platform != "steam" || requested.EpicAppId is not null)
            throw new IOException("Character export on Linux uses the Steam copy of KurtzPel.");
        var executable = requested.Executable;
        var suffix = Path.DirectorySeparatorChar + CharacterExportSources.GameRelativePath;
        if (!Path.IsPathFullyQualified(executable) || !executable.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected KurtzPel installation is invalid. Start Export character again.");
        var common = Path.GetDirectoryName(executable[..^suffix.Length]);
        var steamapps = Path.GetDirectoryName(common);
        var library = Path.GetDirectoryName(steamapps);
        if (library is null || !string.Equals(Path.GetFileName(common), "common", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(steamapps), "steamapps", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(CharacterExportSources.FindInLibrary(library), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected KurtzPel installation changed or is no longer available. Start Export character again.");
        return new CharacterExportSource("steam", Path.GetFullPath(executable));
    }

    /// <summary>What Steam starts for KurtzPel: the bootstrap in the install folder, which starts the shipping executable.</summary>
    internal static string LaunchTarget(string shippingExecutable)
    {
        var install = shippingExecutable[..^(CharacterExportSources.GameRelativePath.Length + 1)];
        var bootstrap = Path.Combine(install, "TheChase.exe");
        return File.Exists(bootstrap) ? bootstrap : shippingExecutable;
    }
#endif
}

#if GAME_WORKER
/// <summary>
/// The Linux launcher's end of this worker's private stdin and stdout. Proton cannot see the native
/// Steam client, so the launcher answers account checks; closing stdin cancels the export.
/// </summary>
internal sealed class LauncherLink
{
    private readonly TextWriter output;
    private readonly BlockingCollection<string> answers = new(1);
    private readonly CancellationTokenSource closed = new();

    internal LauncherLink(TextReader input, TextWriter output)
    {
        this.output = output;
        new Thread(() =>
        {
            try
            {
                // One answer per request; anything else is a protocol error and ends the link.
                while (input.ReadLine() is { } line)
                    if (!answers.TryAdd(line)) break;
            }
            catch (IOException) { }
            finally
            {
                // Cancel before completing, so a check that finds no answer sees the cancellation.
                try { closed.Cancel(); }
                catch (AggregateException) { /* The export ends anyway; this thread must not end the worker. */ }
                answers.CompleteAdding();
            }
        }) { IsBackground = true, Name = "Launcher link" }.Start();
    }

    internal CancellationToken Closed => closed.Token;

    internal bool AccountMatches()
    {
        output.WriteLine(ProtonExport.AccountRequest);
        output.Flush();
        return answers.TryTake(out var answer, TimeSpan.FromSeconds(30)) && answer == ProtonExport.AccountMatches;
    }
}
#endif
