using System.Net.Mail;
using System.Text.RegularExpressions;
namespace NekoProxy.Core;
public static class CertificatePolicy
{
    public const string ProductionDirectory="https://acme-v02.api.letsencrypt.org/directory";
    public const string StagingDirectory="https://acme-staging-v02.api.letsencrypt.org/directory";
    public static string Storage(string domain)
    {
        if(domain.Length>253||!domain.Contains('.')||System.Net.IPAddress.TryParse(domain,out _)||domain.Split('.').Any(label=>!Regex.IsMatch(label,@"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$",RegexOptions.IgnoreCase)))throw new ArgumentException("请输入有效的证书域名。");
        return "/etc/nekoproxy/certificates/"+domain.ToLowerInvariant();
    }
    public static void Validate(ClientNodeConfiguration node,AutomaticCertificateOptions options)
    {
        if(node.Protocol is not (ProxyProtocol.TrojanTls or ProxyProtocol.Hysteria2))throw new ArgumentException("此协议不需要自动 TLS 证书。");
        _=Storage(node.Sni);
        if(!options.DomainAuthorized)throw new ArgumentException("请先确认你有权管理此域名，并同意所选证书机构的条款。");
        if(!string.IsNullOrWhiteSpace(options.Email)&&(!MailAddress.TryCreate(options.Email,out var address)||address.Address!=options.Email||!address.Host.Contains('.')))throw new ArgumentException("请输入有效的 ACME 联系邮箱，或留空。");
        if(!Enum.IsDefined(options.Challenge))throw new ArgumentException("证书验证方式无效。");
    }
}
