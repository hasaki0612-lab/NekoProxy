using System.Text.Json;
using NekoProxy.Core;
namespace NekoProxy.Deployment;

// Pure local generation; it never performs ACME, SSH, file reads or deployment.
public sealed class ServerConfigurationGenerator : IServerConfigurationGenerator
{
    public const string TargetKernelVersion="1.14.3";
    public string Generate(ServerNodeConfiguration options)
    {
        var node=options.Client;
        ProtocolParameters.ValidateClient(node);
        if(options.AutomaticCertificate is {} automatic)CertificatePolicy.Validate(node,automatic);
        if(options.AutomaticCertificate is not null&&(!string.IsNullOrEmpty(options.CertificatePath)||!string.IsNullOrEmpty(options.CertificateKeyPath)))throw new ArgumentException("自动证书与已有证书路径不能同时使用。");
        if(node.Protocol!=ProxyProtocol.VlessReality&&!string.IsNullOrEmpty(options.RealityPrivateKey))throw new ArgumentException("所选协议不支持这些 TLS / REALITY 字段。");
        if(node.Protocol is ProxyProtocol.VlessReality or ProxyProtocol.Shadowsocks2022 && (!string.IsNullOrEmpty(options.CertificatePath)||!string.IsNullOrEmpty(options.CertificateKeyPath)))throw new ArgumentException("所选协议不支持这些 TLS / REALITY 字段。");
        if(node.Port is <1 or >65535 || string.IsNullOrWhiteSpace(node.Credential)) throw new ArgumentException("请补全端口和认证资料。" );
        var inbound=new Dictionary<string,object?> { ["type"]=node.Protocol switch { ProxyProtocol.VlessReality=>"vless",ProxyProtocol.Hysteria2=>"hysteria2",ProxyProtocol.TrojanTls=>"trojan",ProxyProtocol.Shadowsocks2022=>"shadowsocks",_=>throw new ArgumentException("协议无效。") },["tag"]=node.NodeName,["listen"]="::",["listen_port"]=node.Port };
        if(node.Protocol==ProxyProtocol.Shadowsocks2022)
        { ShadowsocksKeys.Validate(node.Method,node.Credential); inbound["method"]=node.Method;inbound["password"]=node.Credential; }
        else
        {
            if(Uri.CheckHostName(node.Sni)!=UriHostNameType.Dns || !node.Sni.Contains('.'))throw new ArgumentException("请输入有效 SNI / 证书域名。" );
            var tls=new Dictionary<string,object?>{["enabled"]=true,["server_name"]=node.Sni};
            if(node.Protocol==ProxyProtocol.VlessReality)
            {
                if(!Guid.TryParse(node.Credential,out _) || string.IsNullOrWhiteSpace(options.RealityPrivateKey) || string.IsNullOrWhiteSpace(node.ShortId))throw new ArgumentException("REALITY 服务端需要 UUID、私钥和 Short ID。" );
                if(!ProtocolParameters.Key32(options.RealityPrivateKey) || node.ShortId.Length is <2 or >16 || node.ShortId.Length%2!=0 || node.ShortId.Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException("REALITY 服务端密钥或 Short ID 无效。" );
                var privateBytes=Convert.FromBase64String(options.RealityPrivateKey.Replace('-','+').Replace('_','/')+"=");
                try
                {
                    var derived=new Org.BouncyCastle.Crypto.Parameters.X25519PrivateKeyParameters(privateBytes).GeneratePublicKey().GetEncoded();
                    var publicKey=Convert.ToBase64String(derived).TrimEnd('=').Replace('+','-').Replace('/','_');
                    if(publicKey!=node.RealityPublicKey)throw new ArgumentException("REALITY 公钥与私钥不匹配。");
                }
                finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(privateBytes);}
                inbound["users"]=new[]{new{uuid=node.Credential,flow="xtls-rprx-vision"}};
                tls["reality"]=new{enabled=true,handshake=new{server=node.Sni,server_port=443},private_key=options.RealityPrivateKey,short_id=new[]{node.ShortId}};
            }
            else
            {
                inbound["users"]=new[]{new{password=node.Credential}};
                if(options.AutomaticCertificate is {} acme)
                    tls["certificate_provider"]=new{type="acme",domain=new[]{node.Sni},data_directory=CertificatePolicy.Storage(node.Sni),email=acme.Email,provider=acme.Staging?CertificatePolicy.StagingDirectory:CertificatePolicy.ProductionDirectory,disable_http_challenge=acme.Challenge!=CertificateChallenge.Http01,disable_tls_alpn_challenge=acme.Challenge!=CertificateChallenge.TlsAlpn01,key_type="p256"};
                else
                {
                    if(!ValidPath(options.CertificatePath) || !ValidPath(options.CertificateKeyPath))throw new ArgumentException("请填写手动 TLS 证书及私钥的绝对路径。" );
                    tls["certificate_path"]=options.CertificatePath; tls["key_path"]=options.CertificateKeyPath;
                }
            }
            inbound["tls"]=tls;
        }
        return JsonSerializer.Serialize(new{log=new{level=options.AutomaticCertificate is null?"warn":"info"},inbounds=new[]{inbound},outbounds=new[]{new{type="direct",tag="direct"}}},new JsonSerializerOptions{WriteIndented=true});
    }
    private static bool ValidPath(string? value)=>!string.IsNullOrWhiteSpace(value)&&!value.Contains('\n')&&!value.Contains('\r')&&(value.StartsWith('/')||Path.IsPathFullyQualified(value));
}
