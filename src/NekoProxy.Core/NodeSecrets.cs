using System.Text.Json;
namespace NekoProxy.Core;

// This envelope is stored INSIDE the existing encrypted vault, never metadata.
public sealed record NodeSecrets(string Credential,string? RealityPrivateKey=null);
public static class NodeSecretsCodec
{
    private const string Prefix="NekoProxy.NodeSecrets.v1\n";
    public static string Encode(NodeSecrets value)=>Prefix+JsonSerializer.Serialize(value);
    public static NodeSecrets Decode(string? value)=>value is null?new(""):value.StartsWith(Prefix,StringComparison.Ordinal)
        ?JsonSerializer.Deserialize<NodeSecrets>(value[Prefix.Length..])??throw new InvalidDataException("Invalid secret envelope."):new(value);
}
public sealed record LocalNodeDraft(ServerProfile Server,ProxyNode Node,NodeSecrets Secrets);

public static class ProtocolParameters
{
    public static void ValidateClient(ClientNodeConfiguration n)
    {
        if(!ProtocolCatalog.Supported.Contains(n.Protocol))throw new ArgumentException("协议无效。");
        if(string.IsNullOrWhiteSpace(n.Host)||n.Port is <1 or >65535||string.IsNullOrEmpty(n.Credential))throw new ArgumentException("请补全地址、端口、SNI 和客户端认证信息。");
        if(Uri.CheckHostName(n.Host)==UriHostNameType.Unknown)throw new ArgumentException("地址或 SNI 域名格式无效。");
        if(n.Protocol==ProxyProtocol.Shadowsocks2022)
        {
            if(!string.IsNullOrEmpty(n.Sni)||!string.IsNullOrEmpty(n.RealityPublicKey)||!string.IsNullOrEmpty(n.ShortId))throw new ArgumentException("所选协议不支持这些 TLS / REALITY 字段。");
            ShadowsocksKeys.Validate(n.Method,n.Credential);return;
        }
        if(Uri.CheckHostName(n.Sni)!=UriHostNameType.Dns||!n.Sni.Contains('.'))throw new ArgumentException("请输入有效 SNI / 证书域名。");
        if(n.Method!=ShadowsocksKeys.DefaultMethod)throw new ArgumentException("所选协议不支持这些 TLS / REALITY 字段。");
        if(n.Protocol!=ProxyProtocol.VlessReality)
        {if(!string.IsNullOrEmpty(n.RealityPublicKey)||!string.IsNullOrEmpty(n.ShortId))throw new ArgumentException("所选协议不支持这些 TLS / REALITY 字段。");return;}
        if(!Guid.TryParse(n.Credential,out _)||string.IsNullOrWhiteSpace(n.RealityPublicKey)||string.IsNullOrWhiteSpace(n.ShortId))throw new ArgumentException("REALITY 需要有效 UUID、公钥和 Short ID。");
        if(!Key32(n.RealityPublicKey)||n.ShortId.Length is <2 or >16||n.ShortId.Length%2!=0||n.ShortId.Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException("REALITY 公钥应为 43 位 Base64URL；Short ID 应为 2–16 位偶数长度十六进制。");
    }
    public static bool Key32(string? value)
    {
        if(value is null||value.Length!=43||value.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not '-' and not '_'))return false;
        try{var bytes=Convert.FromBase64String(value.Replace('-','+').Replace('_','/')+"=");try{return bytes.Length==32&&Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_')==value;}finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);}}
        catch(FormatException){return false;}
    }
}
