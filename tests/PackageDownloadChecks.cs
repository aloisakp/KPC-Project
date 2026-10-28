using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using KpcLauncher.Core;

/// <summary>PackageDownloader against a fake server: ranges, stalls, broken connections, no range support,
/// refusals, a bad hash and a resume from disk.</summary>
internal static class PackageDownloadChecks
{
    public static async Task Run(Action<bool,string> check,string root)
    {
        var previousStall=PackageDownloader.StallTimeout;var previousDelay=PackageDownloader.RetryDelay;
        PackageDownloader.StallTimeout=TimeSpan.FromMilliseconds(300);PackageDownloader.RetryDelay=_=>TimeSpan.FromMilliseconds(5);
        try
        {
            var bytes=RandomNumberGenerator.GetBytes(7*1024*1024+123);var sha=Convert.ToHexString(SHA256.HashData(bytes));
            async Task<(byte[]? Bytes,Exception? Error)> Download(FakePackageServer server,string? resume=null,IReporter? reporter=null)
            {
                using var http=new HttpClient(server);
                try{return (await new PackageDownloader(http,server.Request,Check,bytes.Length,sha,reporter,"Downloading",resume).RunAsync(default),null);}
                catch(Exception error){return (null,error);}
            }

            var plain=new FakePackageServer(bytes);
            var result=await Download(plain);
            check(result.Bytes is not null && result.Bytes.AsSpan().SequenceEqual(bytes) && plain.Ranged==PackageDownloader.MaxPieces,
                "a package downloads as parallel ranged pieces");

            var stalling=new FakePackageServer(bytes){StallFirstHalf=true};
            result=await Download(stalling);
            check(result.Bytes is not null && result.Bytes.AsSpan().SequenceEqual(bytes) && stalling.Resumed>=PackageDownloader.MaxPieces,
                "stalled pieces are dropped after the stall timeout and resume where they stopped");

            var breaking=new FakePackageServer(bytes){BreakFirstHalf=true,BusyFirst=2};
            result=await Download(breaking);
            check(result.Bytes is not null && result.Bytes.AsSpan().SequenceEqual(bytes) && breaking.Resumed>=PackageDownloader.MaxPieces,
                "broken connections and busy answers are retried");

            var whole=new FakePackageServer(bytes){NoRanges=true};
            result=await Download(whole);
            check(result.Bytes is not null && result.Bytes.AsSpan().SequenceEqual(bytes) && whole.Requests==2,
                "a server without range support gets one whole-file download after the range probe");

            var refused=new FakePackageServer(bytes){Status=HttpStatusCode.Unauthorized};
            result=await Download(refused);
            check(result.Error is TesterException && refused.Requests==1,"a refusal is final, not retried");

            var resume=Path.Combine(root,"package-resume","tools");Directory.CreateDirectory(Path.GetDirectoryName(resume)!);
            var dying=new FakePackageServer(bytes){DieAfter=bytes.Length/3};
            result=await Download(dying,resume);
            check(result.Error is TesterException && File.Exists(resume+".part") && File.Exists(resume+".json"),
                "a download that keeps breaking off fails and keeps its finished part");
            var finishing=new FakePackageServer(bytes);var progress=new QuietReporter();
            result=await Download(finishing,resume,progress);
            check(result.Bytes is not null && result.Bytes.AsSpan().SequenceEqual(bytes) && finishing.Served<bytes.Length*3/4 &&
                !File.Exists(resume+".part") && !File.Exists(resume+".json"),
                "the next attempt downloads only the missing bytes, then removes the partial file");

            var wrong=(byte[])bytes.Clone();wrong[^1]^=1;
            result=await Download(new FakePackageServer(wrong),resume);
            check(result.Error is TesterException && !File.Exists(resume+".part"),"a package with the wrong hash is refused and not kept");

            var slow=new FakePackageServer(bytes){Delay=TimeSpan.FromMilliseconds(15)};var seen=new QuietReporter();
            result=await Download(slow,null,seen);
            check(result.Bytes is not null && seen.LastProgress is {Total:>0} last && last.Detail.Contains(" of ") && last.Detail.Contains("/s"),
                "progress shows bytes and speed");
        }
        finally {PackageDownloader.StallTimeout=previousStall;PackageDownloader.RetryDelay=previousDelay;}
    }

    private static Task Check(HttpResponseMessage response)=>response.IsSuccessStatusCode?Task.CompletedTask:
        throw new TesterException("refused "+(int)response.StatusCode);
}

internal sealed class FakePackageServer(byte[] bytes) : HttpMessageHandler
{
    public bool StallFirstHalf, BreakFirstHalf, NoRanges; public int BusyFirst; public long DieAfter=-1;
    public HttpStatusCode Status=HttpStatusCode.OK; public TimeSpan Delay;
    public int Requests, Ranged, Resumed; public long Served;
    private readonly HashSet<long> starts=[];
    public HttpRequestMessage Request(long? from,long? to)
    {
        var request=new HttpRequestMessage(HttpMethod.Get,"https://packages.test/p");
        if(from is not null) request.Headers.Range=new RangeHeaderValue(from,to);
        return request;
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        lock(this)
        {
            Requests++;
            if(Status!=HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(Status));
            if(BusyFirst>0){BusyFirst--;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));}
            if(DieAfter>=0 && Served>=DieAfter) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var range=request.Headers.Range?.Ranges.Single();
            if(NoRanges || range is null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=Body(0,bytes.Length,false)});
            long start=range.From!.Value,end=Math.Min(range.To!.Value,bytes.Length-1)+1;
            var first=starts.Add(start);if(!first)Resumed++;else Ranged++;
            // A fresh piece (its first request) stalls or breaks halfway; a resumed one completes.
            var response=new HttpResponseMessage(HttpStatusCode.PartialContent){Content=Body(start,end,first)};
            response.Content.Headers.ContentRange=new ContentRangeHeaderValue(start,end-1,bytes.Length);
            return Task.FromResult(response);
        }
    }
    private HttpContent Body(long start,long end,bool fresh)
    {
        var half=fresh && (StallFirstHalf||BreakFirstHalf) ? start+(end-start)/2 : -1;
        var content=new StreamContent(new FakeBody(this,bytes,start,end,half,StallFirstHalf));
        content.Headers.ContentLength=end-start;return content;
    }
    private sealed class FakeBody(FakePackageServer server,byte[] bytes,long position,long end,long half,bool stall) : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> target,CancellationToken ct=default)
        {
            if(server.Delay>TimeSpan.Zero) await Task.Delay(server.Delay,ct);
            if(position>=end) return 0;
            if(half>=0 && position>=half)
            {
                if(stall){await Task.Delay(Timeout.Infinite,ct);}
                throw new IOException("connection reset");
            }
            lock(server){if(server.DieAfter>=0 && server.Served>=server.DieAfter) throw new IOException("connection reset");}
            var limit=half>=0?half:end;
            var count=(int)Math.Min(Math.Min(target.Length,16384),limit-position);
            bytes.AsMemory((int)position,count).CopyTo(target);position+=count;
            lock(server) server.Served+=count;
            return count;
        }
        public override int Read(byte[] buffer,int offset,int count)=>ReadAsync(buffer.AsMemory(offset,count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead=>true; public override bool CanSeek=>false; public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException(); public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush(){} public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();
        public override void SetLength(long v)=>throw new NotSupportedException(); public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }
}
