using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using KpcLauncher.Core;

internal static class TesterChecks
{
    public static async Task Run(Action<bool,string> check,string root)
    {
        var launcher=typeof(TesterPackageHost).Assembly;
        check(launcher.GetManifestResourceNames().Order().SequenceEqual(new[]{"KpcLauncher.g.resources","KpcLauncher.tester-signing-public.pem"}.Order()),
            "launcher embeds only UI resources and the tester public key");
        check(!launcher.GetReferencedAssemblies().Any(a=>a.Name is "KpcPrivateRuntime" or "SkyIslandLobbyBuilder" or "KarmaSkillTreeCompatBuilder" or "AwakeningSkillDataBuilder" or "UAssetAPI"),
            "launcher has no private merge builder dependencies");
        check(!launcher.GetTypes().Any(t=>t.Namespace=="KpcPrivateRuntime"||t.Name is "Merger" or "ClientLauncher" or "PayloadLayout"),
            "launcher contains no private merge or game-launch implementation");
        using var key=RSA.Create(2048);
        var value=new TesterRelease(1,"release-1","merge-1","runtime-1","0.2.0",[
            new("instructions","i.zip",new string('a',64),100,null),new("tools","t.zip",new string('b',64),100,[])],
            new("getKey",LauncherConfig.RequiredArchives[0].ManifestId.ToString(),"local-key-1"));
        var bytes=JsonSerializer.SerializeToUtf8Bytes(value,TesterClient.Json);
        var envelope=new SignedRelease(Convert.ToBase64String(bytes),Convert.ToBase64String(key.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)));
        check(TesterPackageHost.VerifyRelease(envelope,key.ExportSubjectPublicKeyInfoPem()).ReleaseId=="release-1","valid signed tester release");
        void Reject(Action action,string name){try{action();}catch(TesterException){check(true,name);return;}throw new Exception("FAIL: "+name);}
        foreach(var acquisition in new TesterKeyAcquisition?[]{null,new("execute","4819182874103212568","v1"),
            new("getKey","../../outside","v1"),new("getKey","4819182874103212568","")})
        {
            var unsupported=JsonSerializer.SerializeToUtf8Bytes(value with {KeyAcquisition=acquisition},TesterClient.Json);
            var signed=new SignedRelease(Convert.ToBase64String(unsupported),Convert.ToBase64String(key.SignData(unsupported,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)));
            Reject(()=>TesterPackageHost.VerifyRelease(signed,key.ExportSubjectPublicKeyInfoPem()),"unsupported signed local preparation request rejected");
        }
        var config=new LauncherConfig{StorageRoot=Path.Combine(root,"preparation")};
        var archive=LauncherConfig.RequiredArchives[0];var manifest=archive.ManifestId.ToString();
        var directory=config.ArchiveDirectory(archive);Directory.CreateDirectory(directory);
        var receipt=Path.Combine(directory,".kpdl-complete");
        check(!PreservationPipeline.HasVerifiedReceipt(config,manifest),"missing archive cannot trigger preparation");
        File.WriteAllText(receipt,manifest);
        check(!PreservationPipeline.HasVerifiedReceipt(config,manifest),"legacy completion marker cannot trigger preparation");
        File.WriteAllText(receipt,JsonSerializer.Serialize(new ArchiveStamp(manifest,1,10,new string('a',64))));
        check(PreservationPipeline.HasVerifiedReceipt(config,manifest),"completed download receipt allows preparation eligibility");
        File.WriteAllText(receipt,JsonSerializer.Serialize(new ArchiveStamp("wrong",1,10,new string('a',64))));
        check(!PreservationPipeline.HasVerifiedReceipt(config,manifest),"wrong archive receipt cannot trigger preparation");
        foreach(var item in LauncherConfig.RequiredArchives)
        {
            var folder=config.ArchiveDirectory(item);Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"test.bin"),"downloaded fixture");
            var measured=PreservationPipeline.Measure(folder,default);
            File.WriteAllText(Path.Combine(folder,".kpdl-complete"),JsonSerializer.Serialize(new ArchiveStamp(item.ManifestId.ToString(),measured.Files,measured.Bytes,measured.Digest)));
        }
        var ready=new List<ulong>();const ulong account=76561198000000001;
        var steam=new SteamInstall(Path.Combine(root,"fake-steam"),"unused",()=>account,(_,_,_)=>throw new Exception("Unexpected Steam download"));
        await new PreservationPipeline(config,steam,new SteamAuthorization(account,DateTimeOffset.UtcNow),new QuietReporter(),(item,ct)=>
        {
            check(PreservationPipeline.HasVerifiedReceipt(config,item.ManifestId.ToString()),"archive callback receives only a completed receipt");
            ready.Add(item.ManifestId);return Task.CompletedTask;
        }).RunAsync(true,default);
        check(ready.SequenceEqual(LauncherConfig.RequiredArchives.Select(a=>a.ManifestId)),"verified existing archives each notify preparation once");
        var altered=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("merge-1","merge-2"));
        Reject(()=>TesterPackageHost.VerifyRelease(envelope with{Payload=Convert.ToBase64String(altered)},key.ExportSubjectPublicKeyInfoPem()),"tampered recipe metadata rejected");
        using var wrong=RSA.Create(2048);
        Reject(()=>TesterPackageHost.VerifyRelease(envelope,wrong.ExportSubjectPublicKeyInfoPem()),"unknown signing key rejected");
        foreach(var path in new[]{"../escape","/absolute","x/../../escape","x\\escape","x:ads","x/./y","x/y.","x//y"})
            Reject(()=>TesterPackageHost.ChildPath(root,path),"package traversal rejected: "+path);
        check(TesterPackageHost.ChildPath(root,"client/node.exe").StartsWith(root),"ordinary tool path accepted");
        using var cert=new CertificateRequest("CN=test",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
        check(!TesterClient.CertificateMatches(cert)&&!TesterClient.CertificateMatches(null),"untrusted server certificate rejected");
        using var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[30])};
        try{await TesterClient.ReadLimitedAsync(response,20,default);throw new Exception("Limit not enforced");}
        catch(TesterException){check(true,"oversized authenticated response rejected");}
        check(!TesterClient.ValidSession(new TesterSession("76561198000000001",new string('a',43),"https://wrong.example")),"sessions cannot move to a different server");
    }
}
