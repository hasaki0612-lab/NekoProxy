using System.Security.Cryptography;
namespace NekoProxy.Core;

public static class AppVersion { public const string Current="0.6.0"; }

public static class ProtocolCatalog
{
    public static IReadOnlyList<ProxyProtocol> Supported { get; } = [ProxyProtocol.VlessReality,ProxyProtocol.Hysteria2,ProxyProtocol.TrojanTls,ProxyProtocol.Shadowsocks2022];
}
public static class ShadowsocksKeys
{
    public const string DefaultMethod="2022-blake3-aes-128-gcm";
    public static IReadOnlyList<string> Methods { get; } = [DefaultMethod,"2022-blake3-aes-256-gcm","2022-blake3-chacha20-poly1305"];
    public static int Length(string method) => method switch { DefaultMethod => 16, "2022-blake3-aes-256-gcm" or "2022-blake3-chacha20-poly1305" => 32, _ => throw new ArgumentException("不支持的 SS2022 加密方法。") };
    public static string Generate(string method)
    {
        var bytes=RandomNumberGenerator.GetBytes(Length(method));
        try{return Convert.ToBase64String(bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    public static void Validate(string method,string key)
    {
        var required=Length(method); byte[] bytes;
        try { bytes=Convert.FromBase64String(key); } catch(FormatException) { throw new ArgumentException("SS2022 密钥必须是标准 Base64。" ); }
        try { if(bytes.Length!=required || Convert.ToBase64String(bytes)!=key) throw new ArgumentException("SS2022 密钥长度或格式不符合加密方法。" ); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
public sealed record ServerNodeConfiguration(ClientNodeConfiguration Client,string? RealityPrivateKey=null,string? CertificatePath=null,string? CertificateKeyPath=null,AutomaticCertificateOptions? AutomaticCertificate=null);
public interface IServerConfigurationGenerator { string Generate(ServerNodeConfiguration node); }
