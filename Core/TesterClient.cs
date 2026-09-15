using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace KpcLauncher.Core;

public sealed record TesterSession(string SteamId, string Token, string Server);
public sealed record SignedRelease(string Payload, string Signature);
public sealed record TesterToolFile(string Path, string Sha256, long Bytes);
public sealed record TesterPackage(string Kind, string File, string Sha256, long Bytes, TesterToolFile[]? Files);
public sealed record TesterKeyAcquisition(string Operation, string ArchiveManifest, string Version);
public sealed record TesterRelease(int Schema, string ReleaseId, string MergeVersion, string RuntimeVersion,
    string MinLauncherVersion, TesterPackage[] Packages, TesterKeyAcquisition? KeyAcquisition = null,
    int CharacterTransferVersion = 0);
public sealed record CharacterCreationStatus(string SchemaVersion, string State, string? DraftUid);
public sealed record AccountDeletionStatus(string SchemaVersion, string State);
public sealed record TesterStatus(bool Tester, string SteamId, SignedRelease? Release);
public sealed class TesterException(string message) : Exception(message);

public sealed class TesterClient : IDisposable
{
    public const string Server = "https://178.104.156.210:11006";
    internal const string CertificateSha256 = "3630195B7FD5C1E7A60080B367D82DA922288B38E2975C4C81E774A03460E389";
    internal static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static string SessionFile => Path.Combine(LauncherConfig.AppDataDir, "tester-session.dat");
    private readonly HttpClient http;
    public TesterSession? Session { get; private set; }
    public TesterClient()
    {
        http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => CertificateMatches(cert),
        }) { Timeout = TimeSpan.FromMinutes(10) };
        Session = LoadSession();
    }
    internal static bool CertificateMatches(X509Certificate2? certificate) => certificate is not null &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.RawData), Convert.FromHexString(CertificateSha256));
    private static TesterSession? LoadSession()
    {
        try
        {
            if (!File.Exists(SessionFile) || new FileInfo(SessionFile).Length > 16384) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(SessionFile), null, DataProtectionScope.CurrentUser);
            try
            {
                var saved = JsonSerializer.Deserialize<TesterSession>(plain, Json);
                if (saved is not null && ValidSession(saved)) return saved;
                // Do not retain or forward sessions bound to an obsolete endpoint.
                File.Delete(SessionFile);
                return null;
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { return null; }
    }
    internal static bool ValidSession(TesterSession value) => value.Server == Server &&
        ulong.TryParse(value.SteamId, out var id) && SteamOpenId.IsIndividualId(id) &&
        System.Text.RegularExpressions.Regex.IsMatch(value.Token, "^[A-Za-z0-9_-]{43}$");
    public void Forget()
    {
        Session = null;
        if (File.Exists(SessionFile)) File.Delete(SessionFile);
    }
    public async Task<ulong> SignInAsync(IReporter reporter, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5)); ct = timeout.Token;
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
        string? challenge = null;
        TesterSession? established = null;
        reporter.Step("Verify Steam account with the server");
        var id = await SteamOpenId.AuthenticateAsync(url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(), http, ct,
            async returnTo =>
            {
                var result = await SendAsync<JsonElement>(HttpMethod.Post, "/auth/start", new
                {
                    returnTo, verifierHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).ToLowerInvariant(),
                }, ct, authenticate: false);
                challenge = result.GetProperty("challengeId").GetString();
                var login = result.GetProperty("loginUrl").GetString()!;
                if (!Uri.TryCreate(login, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Path) != SteamOpenId.Endpoint)
                    throw new TesterException("The server returned an unexpected Steam sign-in address.");
                return login;
            },
            async assertion =>
            {
                var result = await SendAsync<JsonElement>(HttpMethod.Post, "/auth/complete", new { challengeId = challenge, verifier, assertion }, ct, authenticate: false);
                established = new TesterSession(result.GetProperty("steamId").GetString()!,result.GetProperty("token").GetString()!,Server);
                if (!ValidSession(established)) throw new TesterException("The server returned an invalid account session.");
                return ulong.Parse(established.SteamId);
            }).ConfigureAwait(false);
        Session = established ?? throw new TesterException("Steam sign-in did not finish.");
        Directory.CreateDirectory(LauncherConfig.AppDataDir);
        var plain = JsonSerializer.SerializeToUtf8Bytes(Session, Json);
        try
        {
            var temporary = SessionFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser));
            File.Move(temporary,SessionFile,true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
        return id;
    }
    public Task<TesterStatus> StatusAsync(CancellationToken ct) => SendAsync<TesterStatus>(HttpMethod.Get,"/status",null,ct);
    public Task<JsonElement> RedeemAsync(string code, CancellationToken ct) => SendAsync<JsonElement>(HttpMethod.Post,"/redeem",new {code},ct);
    public Task<JsonElement> LaunchAsync(TesterRelease release, CancellationToken ct) => SendAsync<JsonElement>(HttpMethod.Post,"/launch",
        new {release.ReleaseId,release.MergeVersion,release.RuntimeVersion},ct);
    public Task<CharacterCreationStatus> CharacterCreationAsync(CancellationToken ct) =>
        SendAsync<CharacterCreationStatus>(HttpMethod.Get,"/auth/player/character-creation",null,ct,playerControl:true);
    public Task<CharacterCreationStatus> ImportCharacterAsync(JsonElement exported,CancellationToken ct) =>
        SendAsync<CharacterCreationStatus>(HttpMethod.Post,"/auth/player/character-creation/import",exported,ct,playerControl:true);
    public async Task DeleteAccountAsync(CancellationToken ct)
    {
        using var request=Request(HttpMethod.Post,"/auth/player/account/delete",new {confirmation="DELETE"},true,playerControl:true);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
        var bytes=await ReadLimitedAsync(response,4096,ct).ConfigureAwait(false);
        try
        {
            if(response.StatusCode==HttpStatusCode.Conflict)
            {
                using var problem=JsonDocument.Parse(bytes);
                if(problem.RootElement.TryGetProperty("code",out var code)&&code.GetString()=="account_in_use")
                    throw new TesterException("Close KurtzPel before deleting your account, then try again.");
                throw new TesterException("Account deletion could not complete. Please try again.");
            }
            await CheckAsync(response).ConfigureAwait(false);
            ValidateDeletion(JsonSerializer.Deserialize<AccountDeletionStatus>(bytes,Json));
        }
        finally {CryptographicOperations.ZeroMemory(bytes);}
    }
    internal static void ValidateDeletion(AccountDeletionStatus? result)
    {
        if(result?.SchemaVersion!="kp-account-deletion/v1"||result.State!="empty")
            throw new TesterException("The server did not confirm account deletion.");
    }
    private HttpRequestMessage Request(HttpMethod method,string path,object? body,bool authenticate,bool playerControl=false)
    {
        var request = new HttpRequestMessage(method,Server + (playerControl ? "" : "/tester/v1") + path);
        if (authenticate)
        {
            if (Session is null) throw new TesterException("Authorize Steam in Settings first.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",Session.Token);
        }
        if (body is not null) request.Content = JsonContent.Create(body,options:Json);
        return request;
    }
    private async Task CheckAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.Unauthorized) { Forget(); throw new TesterException("Your server session expired. Authorize Steam again."); }
        throw new TesterException(response.StatusCode switch
        {
            HttpStatusCode.Forbidden => "Tester access or the code was refused. Check the code or contact the testing administrator.",
            HttpStatusCode.Conflict => "A newer game update is available. Check tester access and merge again.",
            HttpStatusCode.TooManyRequests => "Too many attempts. Please wait before trying again.",
            _ => "The tester service is unavailable. Please try again later.",
        });
    }
    private async Task<T> SendAsync<T>(HttpMethod method,string path,object? body,CancellationToken ct,bool authenticate=true,bool playerControl=false)
    {
        using var request = Request(method,path,body,authenticate,playerControl);
        using var response = await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
        await CheckAsync(response);
        var bytes = await ReadLimitedAsync(response,2*1024*1024,ct);
        try { return JsonSerializer.Deserialize<T>(bytes,Json) ?? throw new TesterException("The tester server returned an empty response."); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public async Task<byte[]> DownloadAsync(TesterRelease release,TesterPackage package,CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get,$"/packages/{release.ReleaseId}/{package.Sha256}",null,true);
        using var response = await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
        await CheckAsync(response);
        var bytes = await ReadLimitedAsync(response,package.Bytes,ct);
        if (bytes.LongLength != package.Bytes || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(package.Sha256,StringComparison.OrdinalIgnoreCase))
        { CryptographicOperations.ZeroMemory(bytes); throw new TesterException("The private update failed its signature-backed file hash."); }
        return bytes;
    }
    internal static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response,long limit,CancellationToken ct)
    {
        if (limit < 0 || limit > 512L*1024*1024 || response.Content.Headers.ContentLength > limit)
            throw new TesterException("The server response exceeded its permitted size.");
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var target = new MemoryStream();
        var buffer = new byte[65536];
        try
        {
            int count;
            while ((count = await source.ReadAsync(buffer,ct)) != 0)
            {
                if (target.Length + count > limit) throw new TesterException("The server response exceeded its permitted size.");
                await target.WriteAsync(buffer.AsMemory(0,count),ct);
            }
            return target.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(buffer); if (target.TryGetBuffer(out var segment)) CryptographicOperations.ZeroMemory(segment.AsSpan()); }
    }
    public void Dispose() => http.Dispose();
}
