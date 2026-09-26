using System.Security.Cryptography;
using System.Text;
using KpcLauncher.Core;

internal static class ManifestRecoveryChecks
{
    internal static void Write(string path, ulong manifest, string name, byte[] content, uint depot = 844871,
        bool encrypted = false, byte[]? hash = null)
    {
        byte[] Message(Action<BinaryWriter> write)
        { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); write(writer); return stream.ToArray(); }
        void Varint(BinaryWriter writer, ulong value)
        { while (value >= 128) { writer.Write((byte)(value | 128)); value >>= 7; } writer.Write((byte)value); }
        void Number(BinaryWriter writer, int field, ulong value) { Varint(writer, (ulong)(field << 3)); Varint(writer, value); }
        void Bytes(BinaryWriter writer, int field, byte[] value)
        { Varint(writer, (ulong)((field << 3) | 2)); Varint(writer, (ulong)value.Length); writer.Write(value); }
        var payload = Message(w => Bytes(w, 1, Message(f =>
        {
            Bytes(f, 1, Encoding.UTF8.GetBytes(name)); Number(f, 2, (ulong)content.Length);
            Bytes(f, 5, hash ?? SHA1.HashData(content));
        })));
        var crcInput = Message(w => { w.Write(payload.Length); w.Write(payload); });
        var metadata = Message(w =>
        {
            Number(w, 1, depot); Number(w, 2, manifest); Number(w, 4, encrypted ? 1UL : 0UL);
            Number(w, 5, (ulong)content.Length); Number(w, 9, SteamDepotManifest.Crc32(crcInput));
        });
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Message(w =>
        {
            w.Write(0x71F617D0U); w.Write(payload.Length); w.Write(payload);
            w.Write(0x1F4812BEU); w.Write(metadata.Length); w.Write(metadata);
            w.Write(0x1B81B817U); w.Write(0); w.Write(0x32C415ABU);
        }));
    }

    internal static void Run(Action<bool, string> check, string root, IReporter reporter)
    {
        var folder = Path.Combine(root, "manifest-recovery"); Directory.CreateDirectory(folder);
        var cache = Path.Combine(root, "fixture.manifest");
        var bytes = Encoding.UTF8.GetBytes("complete saved archive");
        Write(cache, 123, "data\\payload.bin", bytes);
        var manifest = SteamDepotManifest.Read(cache, 844871, 123);
        check(SteamDepotManifest.Crc32(Encoding.ASCII.GetBytes("123456789")) == 0xCBF43926, "manifest CRC matches the standard check vector");
        check(manifest.Files.Count == 1 && manifest.Bytes == bytes.Length, "cached manifest binds depot, manifest and file totals");
        check(!manifest.Matches(folder, reporter, "test", default), "empty folder cannot recover an archive");
        Directory.CreateDirectory(Path.Combine(folder, "data"));
        var file = Path.Combine(folder, "data", "payload.bin"); File.WriteAllBytes(file, bytes);
        check(manifest.Matches(folder, reporter, "test", default), "saved archive recovers only after matching each Steam file hash");
        var damaged = bytes.ToArray(); damaged[0] ^= 1; File.WriteAllBytes(file, damaged);
        check(!manifest.Matches(folder, reporter, "test", default), "equal-size corrupted archive is rejected by its hash");
        File.WriteAllBytes(file, bytes);
        File.WriteAllText(Path.Combine(folder, "extra.bin"), "extra");
        check(!manifest.Matches(folder, reporter, "test", default), "mixed archive with extra files is not adopted");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        File.Delete(Path.Combine(folder, "extra.bin"));
        var cancelled = false;
        try { manifest.Matches(folder, reporter, "test", cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "archive recovery hashing is cancellable");
        void Reject(Action action, string label)
        {
            var rejected = false;
            try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or OverflowException) { rejected = true; }
            check(rejected, label);
        }
        Reject(() => SteamDepotManifest.Read(cache, 844871, 124), "another manifest cannot authorize recovery");
        Reject(() => SteamDepotManifest.Read(cache, 844872, 123), "another depot cannot authorize recovery");
        foreach (var name in new[] { "../escape", "/absolute", "data/../escape", "data//file", "C:/file" })
        {
            Write(cache, 123, name, bytes);
            Reject(() => SteamDepotManifest.Read(cache, 844871, 123), "recovery manifest rejects unsafe path " + name);
        }
        Write(cache, 123, "payload.bin", bytes, encrypted: true);
        Reject(() => SteamDepotManifest.Read(cache, 844871, 123), "encrypted cache is not used without Steam decrypting it");
        Write(cache, 123, "payload.bin", bytes, hash: new byte[19]);
        Reject(() => SteamDepotManifest.Read(cache, 844871, 123), "malformed file hash is rejected");
        Write(cache, 123, "payload.bin", bytes);
        var raw = File.ReadAllBytes(cache); raw[12] ^= 1; File.WriteAllBytes(cache, raw);
        Reject(() => SteamDepotManifest.Read(cache, 844871, 123), "damaged manifest payload fails its CRC");
        File.WriteAllBytes(cache, raw[..12]);
        Reject(() => SteamDepotManifest.Read(cache, 844871, 123), "truncated cache is rejected");
        // Optional local format check against a real Steam-owned cache, read-only.
        if (Environment.GetEnvironmentVariable("KPC_TEST_MANIFEST") is { Length: > 0 } real)
        {
            var actual = SteamDepotManifest.Read(real, 844871, 4819182874103212568);
            check(actual.Bytes == 28860911366 && actual.Files.Count > 0, "real Steam Archive A cache agrees with the player's staged byte count");
        }
    }
}
