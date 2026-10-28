using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace KpcLauncher.Core;

/// <summary>
/// Downloads one signed package over a slow or lossy route (21 Oct 2026: players in Asia hung for over an hour at
/// "Checking character export tools"; their connections broke off silently and the launcher waited forever).
/// The package is split into pieces fetched in parallel with HTTP ranges. A piece that receives nothing for
/// <see cref="StallTimeout"/> is dropped and asked for again from where it stopped. Progress (bytes, speed) goes
/// to the reporter, and a public package can keep its finished pieces on disk so the next attempt resumes.
/// A server without range support gets one whole-file stream with the same stall timeout and retries.
/// The caller still checks the complete bytes against the signed SHA-256.
/// </summary>
internal sealed class PackageDownloader
{
    internal static TimeSpan StallTimeout = TimeSpan.FromSeconds(45);
    internal static Func<int,TimeSpan> RetryDelay = attempt => TimeSpan.FromSeconds(Math.Min(15, 1 << Math.Min(attempt, 4)));
    internal const int MaxPieces = 6, AttemptsWithoutProgress = 8;
    private const long MinPiece = 1024 * 1024;

    private readonly HttpClient http;
    private readonly Func<long?,long?,HttpRequestMessage> request;
    private readonly Func<HttpResponseMessage,Task> check;
    private readonly long size;
    private readonly IReporter? reporter;
    private readonly string step;
    private readonly string? resumeFile;
    private readonly string sha256;
    private readonly byte[] buffer;
    private long[] done = [];
    private (long Start, long End)[] pieces = [];
    private long received;
    private readonly object gate = new();

    /// <param name="request">Builds a request for bytes [from, to] (both null for the whole file).</param>
    /// <param name="check">Throws for a final refusal (401, 403, 409 …).</param>
    /// <param name="resumeFile">A path for partial data, or null to keep everything in memory (secret packages).</param>
    internal PackageDownloader(HttpClient http, Func<long?,long?,HttpRequestMessage> request, Func<HttpResponseMessage,Task> check,
        long size, string sha256, IReporter? reporter, string step, string? resumeFile)
    {
        if (size < 0 || size > 512L * 1024 * 1024) throw new TesterException("The server response exceeded its permitted size.");
        this.http = http; this.request = request; this.check = check; this.size = size; this.sha256 = sha256;
        this.reporter = reporter; this.step = step; this.resumeFile = resumeFile;
        buffer = new byte[size];
    }

    internal async Task<byte[]> RunAsync(CancellationToken ct)
    {
        Plan();
        if (resumeFile is not null) LoadResume();
        using var progress = new CancellationTokenSource();
        var reporting = ReportAsync(progress.Token);
        try
        {
            if (size == 0) return buffer;
            // The first piece also asks whether the server takes ranges. Without them it is the whole file.
            bool ranged = done[0] < pieces[0].End - pieces[0].Start
                ? await PieceAsync(0, firstProbe: true, ct).ConfigureAwait(false)
                : true;
            if (!ranged) { Array.Clear(done); received = 0; await WholeAsync(ct).ConfigureAwait(false); }
            else await RestAsync(ct).ConfigureAwait(false);
            if (!Convert.ToHexString(SHA256.HashData(buffer)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
            {
                DeleteResume();
                throw new TesterException("The private update failed its signature-backed file hash.");
            }
            DeleteResume();
            return buffer;
        }
        catch
        {
            SaveResume();
            CryptographicOperations.ZeroMemory(buffer);
            throw;
        }
        finally
        {
            progress.Cancel();
            try { await reporting.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    /// <summary>The other pieces, in parallel. The first final failure stops the rest and is the one reported.</summary>
    private async Task RestAsync(CancellationToken ct)
    {
        using var all = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tasks = Enumerable.Range(1, pieces.Length - 1).Select(async i =>
        {
            try { await PieceAsync(i, false, all.Token).ConfigureAwait(false); }
            catch { all.Cancel(); throw; }
        }).ToArray();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch
        {
            ct.ThrowIfCancellationRequested();
            var first = tasks.Select(task => task.Exception?.InnerException).FirstOrDefault(error => error is not null and not OperationCanceledException);
            if (first is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(first);
            throw;
        }
    }

    private void Plan()
    {
        var count = (int)Math.Clamp(size / MinPiece, 1, MaxPieces);
        pieces = new (long, long)[count];
        var share = size / count;
        for (var i = 0; i < count; i++) pieces[i] = (i * share, i == count - 1 ? size : (i + 1) * share);
        done = new long[count];
    }

    /// <summary>Fetches one piece until it is complete. Returns false when the server ignored the range
    /// (only asked on the first piece, before any other connection opens).</summary>
    private async Task<bool> PieceAsync(int index, bool firstProbe, CancellationToken ct)
    {
        var failures = 0;
        while (true)
        {
            var (start, end) = pieces[index];
            var from = start + done[index];
            if (from >= end) return true;
            var before = done[index];
            try
            {
                using var message = request(from, end - 1);
                using var response = await SendAsync(message, ct).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK && firstProbe && from == 0) return false;
                if (Transient(response.StatusCode)) throw new HttpRequestException("busy " + (int)response.StatusCode);
                await check(response).ConfigureAwait(false);
                var range = response.Content.Headers.ContentRange;
                if (response.StatusCode != HttpStatusCode.PartialContent || range?.From != from || range.Length != size)
                    throw new TesterException("The server answered a package piece with the wrong bytes.");
                await CopyAsync(response, index, from, end, ct).ConfigureAwait(false);
            }
            catch (Exception error) when (!ct.IsCancellationRequested && IsTransient(error))
            {
                failures = done[index] > before ? 1 : failures + 1;
                if (failures > AttemptsWithoutProgress)
                    throw new TesterException("The download keeps breaking off. Check the connection and try again; the finished part is kept.");
                reporter?.Log($"Download interrupted ({Reason(error)}); retrying from {Human.Bytes(received)}.", LogLevel.Dim);
                await Task.Delay(RetryDelay(failures), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task WholeAsync(CancellationToken ct)
    {
        pieces = [(0, size)]; done = new long[1];
        var failures = 0;
        while (true)
        {
            var before = done[0];
            done[0] = 0; Interlocked.Exchange(ref received, 0);
            try
            {
                using var message = request(null, null);
                using var response = await SendAsync(message, ct).ConfigureAwait(false);
                if (Transient(response.StatusCode)) throw new HttpRequestException("busy " + (int)response.StatusCode);
                await check(response).ConfigureAwait(false);
                if (response.Content.Headers.ContentLength is { } length && length != size)
                    throw new TesterException("The server response exceeded its permitted size.");
                await CopyAsync(response, 0, 0, size, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception error) when (!ct.IsCancellationRequested && IsTransient(error))
            {
                failures = done[0] > before ? 1 : failures + 1;
                if (failures > AttemptsWithoutProgress)
                    throw new TesterException("The download keeps breaking off. Check the connection and try again.");
                reporter?.Log($"Download interrupted ({Reason(error)}); starting it again.", LogLevel.Dim);
                await Task.Delay(RetryDelay(failures), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        // Waiting for the headers is a stall too.
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(StallTimeout);
        try { return await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("no answer"); }
    }

    private async Task CopyAsync(HttpResponseMessage response, int index, long from, long end, CancellationToken ct)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var source = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
        using var file = resumeFile is null ? null : File.OpenHandle(resumeFile + ".part", FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        var position = from;
        while (position < end)
        {
            stall.CancelAfter(StallTimeout);
            int count;
            try { count = await source.ReadAsync(buffer.AsMemory((int)position, (int)Math.Min(65536, end - position)), stall.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("nothing received for " + StallTimeout.TotalSeconds + " s"); }
            if (count == 0) throw new IOException("the connection closed early");
            if (file is not null) RandomAccess.Write(file, buffer.AsSpan((int)position, count), position);
            position += count;
            lock (gate) done[index] += count;
            Interlocked.Add(ref received, count);
        }
        // A server that sends more than the piece is wrong; the hash would catch it, but stop here.
        if (await source.ReadAsync(new byte[1], stall.Token).ConfigureAwait(false) != 0 && pieces.Length > 1)
            throw new TesterException("The server answered a package piece with the wrong bytes.");
    }

    private async Task ReportAsync(CancellationToken stop)
    {
        var clock = Stopwatch.StartNew();
        var samples = new Queue<(double Seconds, long Bytes)>();
        var lastSaved = TimeSpan.Zero;
        while (!stop.IsCancellationRequested)
        {
            await Task.Delay(500, stop).ConfigureAwait(false);
            var now = Interlocked.Read(ref received);
            samples.Enqueue((clock.Elapsed.TotalSeconds, now));
            while (samples.Count > 20) samples.Dequeue();
            var (seconds, bytes) = samples.Peek();
            var rate = clock.Elapsed.TotalSeconds - seconds > 0.1 ? (now - bytes) / (clock.Elapsed.TotalSeconds - seconds) : 0;
            reporter?.Progress(new StepProgress(step, now, size,
                $"{Human.Bytes(now)} of {Human.Bytes(size)} at {Human.Bytes((long)rate)}/s"));
            if (resumeFile is not null && clock.Elapsed - lastSaved > TimeSpan.FromSeconds(2)) { SaveResume(); lastSaved = clock.Elapsed; }
        }
    }

    private static bool Transient(HttpStatusCode status) =>
        status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway
            or HttpStatusCode.GatewayTimeout or HttpStatusCode.RequestTimeout or HttpStatusCode.InternalServerError;

    private static bool IsTransient(Exception error) =>
        error is HttpRequestException or IOException or TimeoutException ||
        (error is OperationCanceledException && error.InnerException is TimeoutException);

    private static string Reason(Exception error) => error switch
    {
        TimeoutException timeout => timeout.Message,
        HttpRequestException { StatusCode: { } status } => "server " + (int)status,
        _ => "the connection broke off",
    };

    // The resume record: which bytes of each piece are already in <resumeFile>.part. It is only trusted for the
    // same package (hash and size), and the complete file is hashed before use anyway.
    private sealed record ResumeState(string Sha256, long Size, long[][] Pieces);

    private void LoadResume()
    {
        try
        {
            var state = JsonSerializer.Deserialize<ResumeState>(File.ReadAllText(resumeFile + ".json"));
            var part = new FileInfo(resumeFile + ".part");
            if (state is null || state.Size != size || !state.Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase) ||
                state.Pieces.Length != pieces.Length || !part.Exists || part.Length > size) { DeleteResume(); return; }
            for (var i = 0; i < pieces.Length; i++)
            {
                if (state.Pieces[i] is not [var start, var have] || start != pieces[i].Start || have < 0 ||
                    have > pieces[i].End - pieces[i].Start || (have > 0 && start + have > part.Length)) { DeleteResume(); Array.Clear(done); return; }
                done[i] = have;
            }
            using var file = File.OpenHandle(resumeFile + ".part", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            for (var i = 0; i < pieces.Length; i++)
                if (done[i] > 0 && RandomAccess.Read(file, buffer.AsSpan((int)pieces[i].Start, (int)done[i]), pieces[i].Start) != done[i])
                { Array.Clear(done); return; }
            received = done.Sum();
            if (received > 0) reporter?.Log($"Resuming the download: {Human.Bytes(received)} of {Human.Bytes(size)} already here.", LogLevel.Dim);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { Array.Clear(done); received = 0; }
    }

    private void SaveResume()
    {
        if (resumeFile is null) return;
        try
        {
            long[][] snapshot;
            lock (gate) snapshot = pieces.Select((piece, i) => new[] { piece.Start, done[i] }).ToArray();
            var temporary = resumeFile + ".json.tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new ResumeState(sha256, size, snapshot)));
            File.Move(temporary, resumeFile + ".json", true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private void DeleteResume()
    {
        if (resumeFile is null) return;
        foreach (var suffix in new[] { ".json", ".part", ".json.tmp" })
            try { File.Delete(resumeFile + suffix); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
