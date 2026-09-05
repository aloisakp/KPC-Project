using System.IO;
using System.Text;

namespace KpcLauncher.Core;

/// <summary>
/// Reads lines appended to a file another process is still writing to. Steam keeps its logs
/// open, so they can only be opened with <see cref="FileShare.ReadWrite"/>, and it truncates
/// them when it restarts, which shows up here as the file getting shorter.
/// </summary>
public sealed class LogTail
{
    private readonly string _path;
    private long _offset;
    private string _partial = "";
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    private LogTail(string path, long offset)
    {
        _path = path;
        _offset = offset;
    }

    /// <summary>Starts at the current end of the file, so only what happens next is read.</summary>
    public static LogTail FromEnd(string path) => new(path, Length(path));

    public IReadOnlyList<string> ReadNewLines()
    {
        var length = Length(_path);

        if (length < _offset)
        {
            // Steam truncated the log when it restarted; follow it from the beginning again.
            _offset = 0;
            _partial = "";
            _decoder.Reset();
        }

        if (length == _offset) return [];

        byte[] buffer;
        int read;
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(_offset, SeekOrigin.Begin);
            buffer = new byte[(int)Math.Min(length - _offset, 256 * 1024)];
            read = stream.Read(buffer, 0, buffer.Length);
        }
        catch (IOException)
        {
            // Steam is mid-write; whatever was missed is picked up on the next poll.
            return [];
        }

        _offset += read;
        var characters = new char[Encoding.UTF8.GetMaxCharCount(read)];
        var count = _decoder.GetChars(buffer, 0, read, characters, 0, flush: false);
        var text = _partial + new string(characters, 0, count);
        var lines = text.Split('\n');

        // A trailing fragment means the last line is still being written; hold it over.
        var whole = text.EndsWith('\n');
        _partial = whole ? "" : lines[^1];
        if (_partial.Length > 32768) _partial = "";

        return (whole ? lines : lines[..^1])
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .ToArray();
    }

    private static long Length(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception) { return 0; }
    }
}
