using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using KpcLauncher.Core;

internal static class RelayDeviceChecks
{
    public static void Run(Action<bool,string> check,string testRoot)
    {
        var root=Path.Combine(testRoot,"portable-device","RelayState");
        var request=RelayDeviceIdentity.Initialize(root);
        using var doc=JsonDocument.Parse(File.ReadAllText(request));var pin=doc.RootElement.GetProperty("fingerprint").GetString();
        var certPath=Path.Combine(root,"device-identity","client-cert.pem");
        var keyPath=Path.Combine(root,"device-identity","client-key.pem");
        using var certificate=X509Certificate2.CreateFromPemFile(certPath,keyPath);
        check(certificate.HasPrivateKey&&Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant()==pin,
            "portable identity has a matching independently generated key and public enrollment fingerprint");
        check(certificate.NotAfter.ToUniversalTime()>DateTime.UtcNow.AddDays(6)&&certificate.NotAfter.ToUniversalTime()<DateTime.UtcNow.AddDays(8),
            "portable development identity expires after one week");
        check(RelayDeviceIdentity.Initialize(root)==request,"repeated device startup reuses its identity without overwriting it");
        check(!File.ReadAllText(request).Contains("KEY")&&!File.ReadAllText(request).Contains("Steam"),"enrollment request contains no key or Steam session");
        var other=Path.Combine(testRoot,"other-device","RelayState");
        using var otherDoc=JsonDocument.Parse(File.ReadAllText(RelayDeviceIdentity.Initialize(other)));
        check(otherDoc.RootElement.GetProperty("fingerprint").GetString()!=pin,"second device generates a distinct identity");
        var bad=Path.Combine(testRoot,"unowned-device","RelayState");Directory.CreateDirectory(bad);File.WriteAllText(Path.Combine(bad,"existing.txt"),"keep");
        var rejected=false;try{RelayDeviceIdentity.Initialize(bad);}catch(InvalidOperationException){rejected=true;}
        check(rejected&&File.ReadAllText(Path.Combine(bad,"existing.txt"))=="keep","device setup never adopts or clears an unowned directory");
    }
}
