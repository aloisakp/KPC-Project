using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KpcLauncher.Core;

/// <summary>Generic signed-package host. Contains no game recipe, patch sites, builders or hooks.</summary>
public static class TesterPackageHost
{
    private const int MaximumPackage = 512*1024*1024;
    public const string PipeVariable = "KPC_MEMORY_PIPE";
    private static string PublicKey()
    {
        using var stream = typeof(TesterPackageHost).Assembly.GetManifestResourceStream("KpcLauncher.tester-signing-public.pem")
            ?? throw new TesterException("This launcher does not have a tester release verification key.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public static TesterRelease VerifyRelease(SignedRelease envelope) => VerifyRelease(envelope,PublicKey());
    internal static TesterRelease VerifyRelease(SignedRelease envelope,string key)
    {
        byte[] payload;
        try
        {
            payload=Convert.FromBase64String(envelope.Payload);
            if (payload.Length > 2*1024*1024) throw new TesterException("Release metadata is too large.");
            using var rsa=RSA.Create();rsa.ImportFromPem(key);
            if (!rsa.VerifyData(payload,Convert.FromBase64String(envelope.Signature),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))
                throw new TesterException("The private release signature is invalid.");
        }
        catch (Exception e) when (e is FormatException or CryptographicException) { throw new TesterException("The private release signature is invalid."); }
        var release=JsonSerializer.Deserialize<TesterRelease>(payload,TesterClient.Json) ?? throw new TesterException("Release metadata is empty.");
        bool Id(string value) => System.Text.RegularExpressions.Regex.IsMatch(value ?? "","^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$");
        if(release.Schema!=1 || !Id(release.ReleaseId) || !Id(release.MergeVersion) || !Id(release.RuntimeVersion) ||
            !Version.TryParse(release.MinLauncherVersion,out var minimum) || minimum>typeof(TesterPackageHost).Assembly.GetName().Version ||
            release.Packages is null || release.Packages.Length!=2 || release.Packages.Count(p=>p.Kind=="instructions")!=1 || release.Packages.Count(p=>p.Kind=="tools")!=1)
            throw new TesterException("This private update requires a newer launcher or has unsupported metadata.");
        if (release.KeyAcquisition is not { Operation: "getKey" } acquisition || !Id(acquisition.Version) ||
            !LauncherConfig.RequiredArchives.Any(a => a.ManifestId.ToString() == acquisition.ArchiveManifest))
            throw new TesterException("The private update has an unsupported local data preparation request.");
        foreach(var package in release.Packages)
        {
            if(package.Bytes<=0 || package.Bytes>MaximumPackage || !System.Text.RegularExpressions.Regex.IsMatch(package.Sha256 ?? "","^[a-f0-9]{64}$"))
                throw new TesterException("The private package has invalid metadata.");
        }
        return release;
    }
    internal static string ChildPath(string root,string relative)
    {
        if(string.IsNullOrEmpty(relative) || relative.Contains('\\') || relative.Contains(':') || relative.StartsWith('/') ||
            relative.Split('/').Any(p=>p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.') || p.IndexOfAny(Path.GetInvalidFileNameChars())>=0))
            throw new TesterException("A private package contains an unsafe path.");
        var result=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!SafePaths.Within(result,root) || SafePaths.Same(result,root)) throw new TesterException("A private package escaped its installation folder.");
        SafePaths.NoLinks(result); return result;
    }
    private static bool ToolsValid(string root,TesterToolFile[] files)
    {
        foreach(var entry in files)
        {
            var file=ChildPath(root,entry.Path);
            if(!File.Exists(file) || new FileInfo(file).Length!=entry.Bytes) return false;
            using var stream=File.OpenRead(file);
            if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.Sha256,StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
    public static async Task RunAsync(TesterClient client,SignedRelease envelope,string operation,string storageRoot,IReporter reporter,CancellationToken ct)
    {
        var release=VerifyRelease(envelope);
        var tools=release.Packages.Single(p=>p.Kind=="tools");
        var files=tools.Files ?? throw new TesterException("The tools package has no signed file list.");
        if(files.Length is <1 or >20000 || files.Sum(f=>f.Bytes)>2L*1024*1024*1024 || files.Any(f=>f.Bytes<0 ||
            !System.Text.RegularExpressions.Regex.IsMatch(f.Sha256 ?? "","^[a-f0-9]{64}$")) || files.Select(f=>f.Path.ToLowerInvariant()).Distinct().Count()!=files.Length)
            throw new TesterException("The tools package has an invalid file list.");
        var toolsRoot=Path.Combine(LauncherConfig.AppDataDir,"tester-tools",tools.Sha256);
        reporter.Step("Checking private runtime tools");
        if(!await Task.Run(()=>ToolsValid(toolsRoot,files),ct))
        {
            var bytes=await client.DownloadAsync(release,tools,ct);
            try
            {
                using var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read);
                if(zip.Entries.Count!=files.Length) throw new TesterException("The tools archive file set is unexpected.");
                foreach(var item in files)
                {
                    ct.ThrowIfCancellationRequested(); var entry=zip.GetEntry(item.Path);
                    if(entry is null || entry.Length!=item.Bytes) throw new TesterException("The tools archive does not match its signed manifest.");
                    var target=ChildPath(toolsRoot,item.Path);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
                    try
                    {
                        await using(var source=entry.Open()) await using(var output=new FileStream(temporary,FileMode.CreateNew)) await source.CopyToAsync(output,ct);
                        File.Move(temporary,target,true);
                    }
                    finally {if(File.Exists(temporary))File.Delete(temporary);}
                }
                if(!ToolsValid(toolsRoot,files)) throw new TesterException("Private runtime tools failed verification.");
            }
            finally {CryptographicOperations.ZeroMemory(bytes);}
        }
        reporter.Step("Receiving private instructions");
        var instructions=await client.DownloadAsync(release,release.Packages.Single(p=>p.Kind=="instructions"),ct);
        try
        {
            SafePaths.NoLinks(storageRoot);
            var request=JsonSerializer.Serialize(new {operation,storageRoot,session=client.Session,release,toolsRoot},TesterClient.Json);
            var transport=JsonSerializer.SerializeToUtf8Bytes(new {envelope,request,toolsRoot},TesterClient.Json);
            var pipeName="kpc-private-"+Guid.NewGuid().ToString("N");
            using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct);
            var server=ServeAsync(pipeName,transport,instructions,lifetime.Token);
            var start=new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true,
                RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add("--tester-worker");start.Environment[PipeVariable]=pipeName;
            using var process=Process.Start(start) ?? throw new TesterException("The private worker could not start.");
            using var cancel=ct.Register(()=>{try{if(!process.HasExited)process.Kill(entireProcessTree:true);}catch{}});
            async Task Pump(StreamReader reader)
            {
                while(await reader.ReadLineAsync() is { } line)
                    if(line.StartsWith("KPC_STATUS ",StringComparison.Ordinal)) reporter.Step(line[11..]);
                    else if(line.StartsWith("KPC_ERROR ",StringComparison.Ordinal)) reporter.Log(line[10..],LogLevel.Error);
            }
            try
            {
                await Task.WhenAll(Pump(process.StandardOutput),Pump(process.StandardError),process.WaitForExitAsync(ct));
                if(process.ExitCode!=0) throw new TesterException("The private operation failed. The previous installation was preserved where possible; see the status above.");
            }
            finally {lifetime.Cancel();await server;CryptographicOperations.ZeroMemory(transport);}
        }
        finally {CryptographicOperations.ZeroMemory(instructions);}
    }
    private static async Task ServeAsync(string name,byte[] metadata,byte[] instructions,CancellationToken ct)
    {
        try
        {
            while(!ct.IsCancellationRequested)
            {
                await using var pipe=new NamedPipeServerStream(name,PipeDirection.Out,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct);
                try {await WriteFrame(pipe,metadata,ct);await WriteFrame(pipe,instructions,ct);await pipe.FlushAsync(ct);}
                catch(IOException) { /* A child can exit during transfer; keep serving the other workers. */ }
            }
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){}
        catch(IOException) when(ct.IsCancellationRequested){}
    }
    private static async Task WriteFrame(Stream stream,byte[] bytes,CancellationToken ct)
    {await stream.WriteAsync(BitConverter.GetBytes(bytes.Length),ct);await stream.WriteAsync(bytes,ct);}
    private static async Task<byte[]> ReadFrame(Stream stream,int maximum,CancellationToken ct)
    {
        var prefix=new byte[4];await stream.ReadExactlyAsync(prefix,ct);var size=BitConverter.ToInt32(prefix);
        if(size<1 || size>maximum)throw new TesterException("The private worker received an invalid frame.");
        var data=new byte[size];await stream.ReadExactlyAsync(data,ct);return data;
    }
    public static int RunWorker(string[] args)
    {
        try {return RunWorkerAsync(args).GetAwaiter().GetResult();}
        catch(Exception ex) {Console.Error.WriteLine("KPC_ERROR "+(ex is TargetInvocationException ? ex.InnerException?.Message : ex.Message));return 1;}
    }
    private static async Task<int> RunWorkerAsync(string[] args)
    {
        var name=Environment.GetEnvironmentVariable(PipeVariable);
        if(name is null || !System.Text.RegularExpressions.Regex.IsMatch(name,"^kpc-private-[a-f0-9]{32}$"))throw new TesterException("Private worker owner is missing.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var pipe=new NamedPipeClientStream(".",name,PipeDirection.In,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        var metadata=await ReadFrame(pipe,4*1024*1024,timeout.Token);
        var instructions=await ReadFrame(pipe,MaximumPackage,timeout.Token);
        try
        {
            using var document=JsonDocument.Parse(metadata);var root=document.RootElement;
            var envelope=root.GetProperty("envelope").Deserialize<SignedRelease>(TesterClient.Json)!;
            var release=VerifyRelease(envelope);var item=release.Packages.Single(p=>p.Kind=="instructions");
            if(instructions.LongLength!=item.Bytes || !Convert.ToHexString(SHA256.HashData(instructions)).Equals(item.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new TesterException("Private worker instruction signature verification failed.");
            using var archive=new ZipArchive(new MemoryStream(instructions),ZipArchiveMode.Read);
            var assets=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in archive.Entries)
            {
                if(entry.Length>MaximumPackage || assets.Count>20000 || assets.Values.Sum(v=>(long)v.Length)+entry.Length>MaximumPackage)
                    throw new TesterException("Private instructions exceed the memory budget.");
                using var source=entry.Open();using var bytes=new MemoryStream();source.CopyTo(bytes);
                if(!assets.TryAdd(entry.FullName,bytes.ToArray()))throw new TesterException("Private instructions contain duplicate assets.");
            }
            var context=new PrivateLoadContext(assets);
            try
            {
                using var module=new MemoryStream(assets["assemblies/KpcPrivateRuntime.dll"]);
                var assembly=context.LoadFromStream(module);
                var entry=assembly.GetType("KpcPrivateRuntime.Entry",true)!.GetMethod("RunAsync",BindingFlags.Public|BindingFlags.Static)!;
                Func<string,byte[]> read=key=>assets.TryGetValue(key,out var bytes)?bytes:throw new FileNotFoundException("Private asset is missing.");
                Func<string[]> names=()=>assets.Keys.ToArray();
                var task=(Task<int>)entry.Invoke(null,[root.GetProperty("request").GetString()!,args,read,names])!;
                return await task;
            }
            finally {context.Unload();foreach(var bytes in assets.Values)CryptographicOperations.ZeroMemory(bytes);}
        }
        finally {CryptographicOperations.ZeroMemory(metadata);CryptographicOperations.ZeroMemory(instructions);}
    }
    private sealed class PrivateLoadContext(Dictionary<string,byte[]> assets):AssemblyLoadContext(isCollectible:true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if(!assets.TryGetValue("assemblies/"+name.Name+".dll",out var bytes))return null;
            using var stream=new MemoryStream(bytes);return LoadFromStream(stream);
        }
    }
}
