using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KpcLauncher.Core;

/// <summary>
/// Reads the decrypted manifest cached by the installed Steam client. This is a
/// local recovery check, not a replacement for Steam authorization or server signatures.
/// Wire format references: SteamRE/SteamKit Types/DepotManifest.cs and ContentManifest.cs.
/// No network requests, depot keys, credentials or decryption are involved.
/// </summary>
internal sealed class SteamDepotManifest
{
    internal sealed record Entry(string Name, long Size, byte[] Sha1);
    internal IReadOnlyList<Entry> Files { get; }
    internal long Bytes { get; }
    private SteamDepotManifest(List<Entry> files, long bytes) { Files = files; Bytes = bytes; }

    internal static SteamDepotManifest Read(string path, uint depot, ulong manifest)
    {
        SafePaths.NoLinks(path);
        using var stream = File.OpenRead(path);
        if (stream.Length > 32 * 1024 * 1024) throw new InvalidDataException("Steam manifest is too large.");
        using var reader = new BinaryReader(stream);
        byte[] Section(uint magic)
        {
            if (reader.ReadUInt32() != magic) throw new InvalidDataException("Unrecognized Steam manifest section.");
            var size = reader.ReadUInt32();
            if (size > stream.Length - stream.Position) throw new InvalidDataException("Truncated Steam manifest.");
            return reader.ReadBytes(checked((int)size));
        }
        var payload = Section(0x71F617D0);
        var metadata = Fields(Section(0x1F4812BE)).ToDictionary(f => f.Number);
        _ = Section(0x1B81B817);
        if (reader.ReadUInt32() != 0x32C415AB || stream.Position != stream.Length)
            throw new InvalidDataException("Invalid Steam manifest ending.");
        ulong Number(int key) => metadata.TryGetValue(key, out var field) && field.Wire == 0
            ? field.Value : throw new InvalidDataException("Missing Steam manifest metadata.");
        if (Number(1) != depot || Number(2) != manifest || Number(4) != 0)
            throw new InvalidDataException("Steam cache has a different or encrypted manifest.");
        var total = checked((long)Number(5));
        // Steam includes the little-endian payload length in the clear-payload CRC.
        var crcInput = new byte[payload.Length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(crcInput, payload.Length);
        payload.CopyTo(crcInput, 4);
        if (Crc32(crcInput) != Number(9)) throw new InvalidDataException("Steam manifest checksum mismatch.");
        var files = new List<Entry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in Fields(payload))
        {
            if (mapping.Number != 1) continue;
            if (mapping.Wire != 2 || names.Count >= 100000) throw new InvalidDataException("Invalid Steam file mapping.");
            var fields = Fields(mapping.Data).Where(f => f.Number != 6).ToDictionary(f => f.Number);
            var name = fields.TryGetValue(1, out var nameField) && nameField.Wire == 2
                ? new UTF8Encoding(false, true).GetString(nameField.Data.Span).Replace('\\', '/') : "";
            if (name.Length is 0 or > 4096 || name.Contains(':') || name.Contains('\0') ||
                name.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ')) ||
                name == ".kpdl-complete" || !names.Add(name))
                throw new InvalidDataException("Unsafe or duplicate Steam manifest path.");
            if (fields.TryGetValue(7, out var link) && (link.Wire != 2 || link.Data.Length != 0))
                throw new InvalidDataException("Linked manifest entries are not supported for recovery.");
            var flags = fields.TryGetValue(3, out var flag) && flag.Wire == 0 ? flag.Value : 0;
            if ((flags & 64) != 0) continue; // directory
            if (!fields.TryGetValue(2, out var size) || size.Wire != 0 ||
                !fields.TryGetValue(5, out var hash) || hash.Wire != 2 || hash.Data.Length != 20)
                throw new InvalidDataException("Incomplete Steam file mapping.");
            files.Add(new Entry(name, checked((long)size.Value), hash.Data.ToArray()));
        }
        if (files.Count == 0 || total <= 0 || files.Sum(f => f.Size) != total)
            throw new InvalidDataException("Steam manifest file totals do not match.");
        return new SteamDepotManifest(files, total);
    }

    internal bool Matches(string directory, IReporter reporter, string label, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) return false;
        var actual = SafePaths.Files(directory).Where(f => Path.GetRelativePath(directory, f.FullName) != ".kpdl-complete").ToArray();
        if (actual.Length != Files.Count || actual.Sum(f => f.Length) != Bytes) return false;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var byName = actual.ToDictionary(f => Path.GetRelativePath(directory, f.FullName).Replace(Path.DirectorySeparatorChar, '/'), comparer);
        if (Files.Any(f => !byName.TryGetValue(f.Name, out var present) || present.Length != f.Size)) return false;
        reporter.Log($"Checking saved files against Steam's manifest: {directory}", LogLevel.Dim);
        var buffer = new byte[1024 * 1024];
        long done = 0;
        var lastReport = DateTime.MinValue;
        foreach (var file in Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = byName[file.Name].FullName;
            SafePaths.NoLinks(path);
            using var input = File.OpenRead(path);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
                done += read;
                if (DateTime.UtcNow - lastReport >= TimeSpan.FromMilliseconds(200) || done == Bytes)
                {
                    reporter.Progress(new StepProgress($"Recovering {label}", done, Bytes, $"Checking saved files: {Human.Bytes(done)} of {Human.Bytes(Bytes)}"));
                    lastReport = DateTime.UtcNow;
                }
            }
            if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), file.Sha1)) return false;
        }
        return true;
    }

    private readonly record struct Field(int Number, int Wire, ulong Value, ReadOnlyMemory<byte> Data);
    private static IEnumerable<Field> Fields(ReadOnlyMemory<byte> bytes)
    {
        var position = 0;
        ulong Varint()
        {
            ulong value = 0;
            for (var shift = 0; shift < 70; shift += 7)
            {
                if (position == bytes.Length) throw new InvalidDataException("Truncated protobuf integer.");
                var part = bytes.Span[position++];
                if (shift == 63 && part > 1) throw new InvalidDataException("Overflowing protobuf integer.");
                value |= (ulong)(part & 127) << shift;
                if ((part & 128) == 0) return value;
            }
            throw new InvalidDataException("Invalid protobuf integer.");
        }
        while (position < bytes.Length)
        {
            var tag = Varint();
            var number = checked((int)(tag >> 3));
            var wire = (int)(tag & 7);
            if (number == 0) throw new InvalidDataException("Invalid protobuf field.");
            if (wire == 0) { yield return new Field(number, wire, Varint(), default); continue; }
            var size = wire switch { 1 => 8, 2 => checked((int)Varint()), 5 => 4, _ => throw new InvalidDataException("Unsupported protobuf wire type.") };
            if (size > bytes.Length - position) throw new InvalidDataException("Truncated protobuf field.");
            yield return new Field(number, wire, 0, bytes.Slice(position, size));
            position += size;
        }
    }

    internal static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320U : 0);
        }
        return ~crc;
    }
}
