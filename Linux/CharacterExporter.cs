using System.Text.Json;
namespace KpcLauncher.Core;
internal static class CharacterExporter
{
    public static Task RunAsync(JsonElement request, IReadOnlyDictionary<string, byte[]> assets, CancellationToken ct) =>
        throw new TesterException("Character capture across a separate Proton environment is not supported. You can import existing exports when starting a community character.");
}
