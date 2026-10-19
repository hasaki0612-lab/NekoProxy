using System.Globalization;
using System.Net;

namespace NekoProxy.Core;

public enum PrivacyMode { Smart, Privacy, Custom }
public enum Ipv6Route { Proxy, Reject, Direct }

// Client-local policy. It neither changes the OS network nor contains node credentials.
public sealed record ClientPrivacyPolicy
{
    public PrivacyMode Mode {get;init;}
    public string ProxyDns {get;init;} = "https://1.1.1.1/dns-query";
    public string DirectDns {get;init;} = "https://1.1.1.1/dns-query";
    public string BootstrapDns {get;init;} = "https://1.1.1.1/dns-query";
    public Ipv6Route Ipv6 {get;init;} = Ipv6Route.Proxy;
    public bool Ipv6DirectConsent {get;init;}
    public bool AllowLan {get;init;} = true;
    public bool FakeIp {get;init;}
    public bool DnsCache {get;init;} = true;
    public string[] DirectDomains {get;init;} = [];
    public string[] ProxyDomains {get;init;} = [];
    public string[] RejectDomains {get;init;} = [];
    public ClientPrivacyPolicy Effective() => Mode switch
    {
        PrivacyMode.Smart => this with {Ipv6=Ipv6Route.Proxy,AllowLan=true,FakeIp=false,DnsCache=true,DirectDomains=[],ProxyDomains=[],RejectDomains=[]},
        PrivacyMode.Privacy => this with {Ipv6=Ipv6Route.Reject,AllowLan=false,FakeIp=false,DnsCache=true,DirectDomains=[],ProxyDomains=[],RejectDomains=[]},
        _ => this
    };
}

public static class PrivacyPolicyValidation
{
    public static readonly string[] LocalDomains = ["localhost","local","lan","home.arpa"];
    public static readonly string[] PrivateNetworks = ["0.0.0.0/8","10.0.0.0/8","100.64.0.0/10","127.0.0.0/8","169.254.0.0/16","172.16.0.0/12","192.168.0.0/16","::/128","::1/128","fc00::/7","fe80::/10"];
    public static Uri DnsEndpoint(string value, bool bootstrap=false)
    {
        if (!Uri.TryCreate(value,UriKind.Absolute,out var uri) || uri.Scheme is not ("https" or "tls") ||
            string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length>0 || uri.Query.Length>0 || uri.Fragment.Length>0 ||
            value.Any(char.IsControl) || uri.Port is 0 or >65535 || (uri.Scheme=="tls"&&uri.AbsolutePath!="/"&&uri.AbsolutePath!=""))
            throw new ArgumentException("DNS 上游只接受无附加参数的 DoH（https）或 DoT（tls）地址。TLS 校验始终开启。");
        if (bootstrap&&!IPAddress.TryParse(uri.Host.Trim('[',']'),out _))
            throw new ArgumentException("引导 DNS 必须使用 IP 地址的加密上游，避免解析上游自身时形成循环。");
        return uri;
    }
    public static string[] Domains(IEnumerable<string> domains)
    {
        var result=new List<string>();
        foreach(var raw in domains)
        {
            var v=raw.Trim().TrimEnd('.'); if(v.Length==0)continue;
            try{v=new IdnMapping().GetAscii(v).ToLowerInvariant();}catch{throw new ArgumentException("域名规则含无效字符。");}
            if(v.Length>253||Uri.CheckHostName(v)!=UriHostNameType.Dns||v.Split('.').Any(p=>p.Length==0||p.StartsWith('-')||p.EndsWith('-')||p.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-')))
                throw new ArgumentException("域名规则请填写纯域名，不含协议、端口或通配符。");
            result.Add(v);
        }
        if(result.Count>128)throw new ArgumentException("每组域名规则最多 128 条。");
        return result.Distinct().ToArray();
    }
    public static bool Overlap(string a,string b)=>a==b||a.EndsWith('.'+b,StringComparison.Ordinal)||b.EndsWith('.'+a,StringComparison.Ordinal);
    public static ClientPrivacyPolicy Check(ClientPrivacyPolicy policy,ClientType client,string nodeHost)
    {
        if(!Enum.IsDefined(policy.Mode)||!Enum.IsDefined(policy.Ipv6))throw new ArgumentException("未知的隐私或 IPv6 策略。");
        if(client==ClientType.Shadowrocket)throw new ArgumentException("Shadowrocket 当前只支持单节点链接，无法表达此附加策略。请选择“单节点链接”，策略不会随链接导入。");
        var p=policy.Effective();
        DnsEndpoint(p.ProxyDns);DnsEndpoint(p.DirectDns);DnsEndpoint(p.BootstrapDns,true);
        if(p.Ipv6==Ipv6Route.Direct&&!p.Ipv6DirectConsent)throw new ArgumentException("IPv6 直连可能绕过代理，请先明确同意此行为。");
        if(client==ClientType.Mihomo&&!p.DnsCache)throw new ArgumentException("当前 Mihomo 导出不支持关闭 DNS 缓存；请启用缓存或改用 sing-box。");
        p=p with{DirectDomains=Domains(p.DirectDomains),ProxyDomains=Domains(p.ProxyDomains),RejectDomains=Domains(p.RejectDomains)};
        var groups=new[]{p.DirectDomains,p.ProxyDomains,p.RejectDomains};
        for(var i=0;i<3;i++)for(var j=i+1;j<3;j++)
            if(groups[i].Any(a=>groups[j].Any(b=>Overlap(a,b))))throw new ArgumentException("直连、代理与拒绝域名规则存在重叠，请删除冲突规则。");
        if(groups.SelectMany(x=>x).Any(a=>LocalDomains.Any(b=>Overlap(a,b))))
            throw new ArgumentException("本地域名由局域网访问开关统一控制，请勿添加冲突规则。");
        if(!IPAddress.TryParse(nodeHost,out _)&&groups.SelectMany(x=>x).Any(a=>Overlap(a,nodeHost.ToLowerInvariant())))
            throw new ArgumentException("代理节点自身的域名必须独立引导解析，不能同时出现在域名分流规则中。");
        if(new[]{p.ProxyDns,p.DirectDns}.Select(x=>DnsEndpoint(x).Host).Where(x=>!IPAddress.TryParse(x,out _)).Any(h=>groups.SelectMany(x=>x).Any(d=>Overlap(d,h))))
            throw new ArgumentException("DNS 上游域名使用独立的引导解析和连接路径，请勿同时添加域名分流规则。");
        return p;
    }
    public static string Explain(ClientPrivacyPolicy p)=>
        "代理域名 → 加密 DNS 经代理；直连域名 → 加密 DNS 直连；节点与 DNS 上游的引导解析 → IP 加密 DNS 直连。\n"+
        (p.Effective().AllowLan?"局域网及 .local/.lan 等本地域名允许直连（本地域名使用系统 DNS）。":"局域网及本地域名拒绝访问。")+
        " IPv6："+(p.Effective().Ipv6 switch{Ipv6Route.Proxy=>"经代理；VPS 不支持时连接失败，不回落直连。",Ipv6Route.Reject=>"拒绝客户端 IPv6 目的地址与 AAAA 返回，不修改系统 IPv6；VPS 端的域名解析地址族需另行核实。",_=>"允许客户端 IPv6 直连，可能绕过代理；域名在 VPS 解析时仍依代理规则。"})+
        "\n仅保护导入本配置并经过客户端的流量。未开启 TUN，也未改变系统 DNS；其他应用仍可能直连。客户端局域网策略不能代替 VPS 端的私网访问限制。Fake-IP 仅为映射机制，不是 DNS 加密。";
}
