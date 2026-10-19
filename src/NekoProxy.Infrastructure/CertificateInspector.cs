using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using NekoProxy.Core;
namespace NekoProxy.Infrastructure;

public static class CertificateInspector
{
    public static string Quote(string value)
    {
        if(value.Any(char.IsControl))throw new ArgumentException("路径含有控制字符。");
        return "'"+value.Replace("'","'\"'\"'")+"'";
    }
    public static string CertificateDirectory(string domain)
    {
        return CertificatePolicy.Storage(domain);
    }
    public static string Privileged(string script)=>"if [ \"$(id -u)\" = 0 ]; then sh -c "+Quote(script)+"; else sudo -n sh -c "+Quote(script)+"; fi";
    public static async Task<CertificateHealth> ReadAsync(ISshSession session,ProxyNode node,CancellationToken token=default)
    {
        var path=node.AutomaticCertificate is null?Quote(node.CertificatePath):"$(find "+Quote(CertificateDirectory(node.Sni))+"/certificates -type f -name '*.crt' "+(node.AutomaticCertificate.Staging?"-path '*staging*'":"! -path '*staging*'")+" 2>/dev/null | head -n1)";
        var raw=await session.ExecuteAsync(Privileged("p="+path+"; if [ -n \"$p\" ] && [ -r \"$p\" ]; then openssl x509 -in \"$p\" -outform PEM; fi"),token);
        if(!raw.Contains("BEGIN CERTIFICATE"))return new(node.Sni,"没有可读证书",null,null,"尚无证书",DateTimeOffset.UtcNow);
        using var cert=X509Certificate2.CreateFromPem(raw);
        var expiry=new DateTimeOffset(cert.NotAfter.ToUniversalTime());
        var hostname=cert.GetNameInfo(X509NameType.DnsName,false);
        var state=!cert.MatchesHostname(node.Sni)?"域名不匹配":expiry<=DateTimeOffset.UtcNow?"已过期":expiry<DateTimeOffset.UtcNow.AddDays(14)?"即将到期":"有效";
        if(node.AutomaticCertificate?.Staging==true)state+=" · 测试证书";
        var renewal="已有证书，未启用自动续期";
        if(node.AutomaticCertificate is not null)
        {
            renewal="服务器自动管理；尚未读取到签发或续期记录";
            if(node.ServiceUnit=="nekoproxy-"+node.Id.ToString("N")+".service")
            {
                var events=await session.ExecuteAsync(Privileged("journalctl -u "+Quote(node.ServiceUnit)+" -n 300 --no-pager -o short-iso | grep -Ei 'renew|certificate obtained|could not get certificate' | tail -n 20 || true"),token);
                renewal=RenewalSummary(events);
            }
        }
        return new(hostname,state,expiry,cert.Issuer,renewal,DateTimeOffset.UtcNow);
    }
    public static string RenewalSummary(string events)
    {
        foreach(var line in events.Split('\n').Reverse())
        {
            var date=line.Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if(!DateTimeOffset.TryParse(date,out var time))continue;
            var label=line.Contains("renewed successfully",StringComparison.OrdinalIgnoreCase)?"最近续期成功":
                line.Contains("renew",StringComparison.OrdinalIgnoreCase)&&(line.Contains("error",StringComparison.OrdinalIgnoreCase)||line.Contains("failed",StringComparison.OrdinalIgnoreCase))?"最近续期失败，服务器会重试":
                line.Contains("certificate obtained successfully",StringComparison.OrdinalIgnoreCase)?"最近签发成功，等待续期窗口":
                line.Contains("got renewal info",StringComparison.OrdinalIgnoreCase)?"已收到证书机构的续期计划":null;
            if(label is not null)return label+" · "+time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }
        return "服务器自动管理；尚未读取到签发或续期记录";
    }
}
