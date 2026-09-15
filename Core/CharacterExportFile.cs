using System.IO;
using System.Text.Json;

namespace KpcLauncher.Core;

internal static class CharacterExportFile
{
    internal const int MaximumBytes=16384;
    internal static string? FindLatest(string storageRoot)
    {
        var folder=Path.Combine(storageRoot,"Character Exports");
        if(!Directory.Exists(folder))return null;
        SafePaths.NoLinks(folder);
        return new DirectoryInfo(folder).EnumerateFiles("*.kpc-character.json",SearchOption.TopDirectoryOnly)
            .OrderByDescending(file=>file.LastWriteTimeUtc).ThenBy(file=>file.Name,StringComparer.Ordinal)
            .FirstOrDefault()?.FullName;
    }
    internal static async Task<JsonDocument> ReadAsync(string file,CancellationToken ct)
    {
        SafePaths.NoLinks(file);
        await using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length is <1 or >MaximumBytes)throw new TesterException("The exported character file has an invalid size.");
        var bytes=new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes,ct).ConfigureAwait(false);
        var document=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=16});
        if(document.RootElement.ValueKind!=JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("format",out var format)||format.GetString()!="kpc-character"||
            !document.RootElement.TryGetProperty("version",out var version)||!version.TryGetInt32(out var n)||n!=1)
        {document.Dispose();throw new TesterException("This character export format is not supported.");}
        return document;
    }
    internal static void ValidateStatus(CharacterCreationStatus status)
    {
        if(status.SchemaVersion!="kp-character-creation/v1"||status.State is not ("empty" or "ready" or "import-pending")||
            (status.State=="import-pending"? !long.TryParse(status.DraftUid,out var uid)||uid<=0 : status.DraftUid is not null))
            throw new TesterException("The server returned an invalid character creation state.");
    }
}
