using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace KpcLauncher.Core;

/// <summary>Short-lived authenticated memory transport for the Windows game worker.</summary>
internal sealed class WorkerChannel : IDisposable
{
    internal const string PortVariable = "KPC_WORKER_PORT", TokenVariable = "KPC_WORKER_TOKEN";
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly byte[] token = RandomNumberGenerator.GetBytes(32);
    public WorkerChannel() => listener.Start();
    public void Configure(System.Diagnostics.ProcessStartInfo start)
    {
        start.Environment[PortVariable] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment[TokenVariable] = Convert.ToHexString(token);
    }
    internal static async Task<Stream> ConnectAsync(CancellationToken ct)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable(PortVariable), out var port) || port is < 1 or > 65535)
            throw new TesterException("Invalid game worker connection.");
        var value = Environment.GetEnvironmentVariable(TokenVariable) ?? "";
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c))) throw new TesterException("Missing game worker authorization.");
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(IPAddress.Loopback, port, ct);
            var stream = new NetworkStream(socket, ownsSocket: true);
            await stream.WriteAsync(Convert.FromHexString(value), ct);
            return stream;
        }
        catch { socket.Dispose(); throw; }
    }
    public async Task ServeAsync(byte[] metadata, byte[] instructions, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    await using var stream = client.GetStream();
                    var supplied = new byte[32];
                    await stream.ReadExactlyAsync(supplied, timeout.Token);
                    if (!CryptographicOperations.FixedTimeEquals(supplied, token)) continue;
                    foreach (var frame in new[] { metadata, instructions })
                    {
                        await stream.WriteAsync(BitConverter.GetBytes(frame.Length), timeout.Token);
                        await stream.WriteAsync(frame, timeout.Token);
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    public void Dispose() { listener.Stop(); CryptographicOperations.ZeroMemory(token); }
}
