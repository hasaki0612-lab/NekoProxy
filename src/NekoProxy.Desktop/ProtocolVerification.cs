using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NekoProxy.Core;
using NekoProxy.Deployment;
using NekoProxy.Exporters;
namespace NekoProxy.Desktop;
internal static class ProtocolVerification
{
    public static async Task GenerateAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        // Ephemeral, self-signed test material for local parser checks only.
        using var rsa=RSA.Create(2048);
        var request=new CertificateRequest("CN=example.com",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var san=new SubjectAlternativeNameBuilder();san.AddDnsName("example.com");request.CertificateExtensions.Add(san.Build());
        using var certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddDays(1));
        var certPath=Path.GetFullPath(Path.Combine(directory,"test-certificate.pem"));var keyPath=Path.GetFullPath(Path.Combine(directory,"test-key.pem"));
        await File.WriteAllTextAsync(certPath,certificate.ExportCertificatePem());await File.WriteAllTextAsync(keyPath,rsa.ExportPkcs8PrivateKeyPem());
        var reality=new RealityKeyGenerator().Generate();
        var fixtures=new List<ClientNodeConfiguration>();
        foreach(var protocol in ProtocolCatalog.Supported)
        {
            var methods=protocol==ProxyProtocol.Shadowsocks2022?ShadowsocksKeys.Methods:new[]{ShadowsocksKeys.DefaultMethod};
            foreach(var method in methods)
                fixtures.Add(new("LOCAL-TEST-ONLY",protocol==ProxyProtocol.Shadowsocks2022?method:protocol.Display(),"192.0.2.1",443,protocol,
                    protocol==ProxyProtocol.VlessReality?reality.Uuid:protocol==ProxyProtocol.Shadowsocks2022?ShadowsocksKeys.Generate(method):"local-test-only-password",protocol==ProxyProtocol.Shadowsocks2022?"":"example.com",protocol==ProxyProtocol.VlessReality?reality.PublicKey:null,protocol==ProxyProtocol.VlessReality?reality.ShortId:null,method));
        }
        foreach(var node in fixtures)
        {
            var stem=ExportService.SafeName(node.NodeName);
            var certificateProtocol=node.Protocol is ProxyProtocol.Hysteria2 or ProxyProtocol.TrojanTls;
            await File.WriteAllTextAsync(Path.Combine(directory,stem+"-server.json"),new ServerConfigurationGenerator().Generate(new(node,node.Protocol==ProxyProtocol.VlessReality?reality.PrivateKey:null,certificateProtocol?certPath:null,certificateProtocol?keyPath:null)));
            await File.WriteAllTextAsync(Path.Combine(directory,stem+"-singbox.json"),new SingBoxExporter().Generate(node));
            await File.WriteAllTextAsync(Path.Combine(directory,stem+"-mihomo.yaml"),new MihomoExporter().Generate(node));
        }
    }
}
