using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.Json;

namespace KpcLauncher.Core;

/// <summary>Per-device development identity, never an account or a bundled key.</summary>
public static class RelayDeviceIdentity
{
    public static string Initialize(string root)
    {
        if(!Path.IsPathFullyQualified(root)||Path.GetFileName(Path.TrimEndingDirectorySeparator(root))!="RelayState")
            throw new InvalidOperationException("Use the portable launcher's adjacent RelayState directory.");
        root=SafePaths.Normalize(root);SafePaths.NoLinks(root);
        var marker=Path.Combine(root,RelayDevelopment.MarkerName);
        if(!File.Exists(marker))
        {
            if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any())
                throw new InvalidOperationException("Refusing to adopt an existing unowned state directory.");
            Directory.CreateDirectory(root);
            WriteNew(marker,JsonSerializer.Serialize(new{schema="kp-relay-sandbox/v1",baseline=RelayDevelopment.Baseline,root}));
        }
        RelayDevelopment.RequireRoot(root);
        var credentials=Path.Combine(root,"device-identity");SafePaths.NoLinks(credentials);
        var requestFile=Path.Combine(root,"device-request.json");SafePaths.NoLinks(requestFile);
        var certificateFile=Path.Combine(credentials,"client-cert.pem");
        var privateKeyFile=Path.Combine(credentials,"client-key.pem");
        if(Directory.Exists(credentials))
        {
            SafePaths.NoLinks(certificateFile);SafePaths.NoLinks(privateKeyFile);
            if(!File.Exists(requestFile)||!File.Exists(certificateFile)||!File.Exists(privateKeyFile))
                throw new InvalidOperationException("Device identity is incomplete; no existing files were replaced.");
            using var request=JsonDocument.Parse(File.ReadAllText(requestFile));var data=request.RootElement;
            using var certificate=X509Certificate2.CreateFromPemFile(certificateFile,privateKeyFile);
            var pin=Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();
            if(data.GetProperty("schema").GetString()!="kp-relay-device-request/v1"||
                data.GetProperty("root").GetString()!=root||data.GetProperty("fingerprint").GetString()!=pin||
                !certificate.HasPrivateKey||certificate.NotBefore.ToUniversalTime()>DateTime.UtcNow||
                certificate.NotAfter.ToUniversalTime()<=DateTime.UtcNow)
                throw new InvalidOperationException("Device identity expired or changed. Enroll a new isolated device identity.");
            return requestFile;
        }
        if(File.Exists(requestFile))throw new InvalidOperationException("An enrollment request exists without its identity.");
        var current=WindowsIdentity.GetCurrent().User??throw new InvalidOperationException("Current Windows user is unavailable.");
        var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
        var inheritance=InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit;
        foreach(var sid in new[]{current,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})
            acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,inheritance,PropagationFlags.None,AccessControlType.Allow));
        var directory=new DirectoryInfo(credentials);directory.Create(acl);
        using var rsa=RSA.Create(2048);
        var subject="CN=KPRelayDev-"+Guid.NewGuid().ToString("N");
        var signing=new CertificateRequest(subject,rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        signing.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
        signing.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature,true));
        signing.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.2")},false));
        using var identity=signing.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddDays(7));
        WriteNew(certificateFile,identity.ExportCertificatePem());
        WriteNew(privateKeyFile,rsa.ExportPkcs8PrivateKeyPem());
        WriteNew(requestFile,JsonSerializer.Serialize(new{schema="kp-relay-device-request/v1",root,
            fingerprint=Convert.ToHexString(SHA256.HashData(identity.RawData)).ToLowerInvariant(),
            expiresAtUtc=identity.NotAfter.ToUniversalTime().ToString("o")},new JsonSerializerOptions{WriteIndented=true}));
        return requestFile;
    }
    private static void WriteNew(string path,string contents)
    {
        using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        using var writer=new StreamWriter(stream);writer.Write(contents);
    }
}
