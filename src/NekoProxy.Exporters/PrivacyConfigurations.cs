using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NekoProxy.Core;

namespace NekoProxy.Exporters;

// Typed DNS syntax targets sing-box 1.14.3; Mihomo syntax is checked by the pinned QA core.
public static class PrivacyConfigurations
{
    private static JsonNode J(object value)=>JsonSerializer.SerializeToNode(value)!;
    private static JsonObject Dns(string url,string tag,string detour)
    {
        var u=PrivacyPolicyValidation.DnsEndpoint(url);var host=u.Host.Trim('[',']');
        var n=new JsonObject{["type"]=u.Scheme=="tls"?"tls":"https",["tag"]=tag,["server"]=host,["server_port"]=u.Port<0?853:u.Port,["detour"]=detour};
        if(u.Scheme=="https")n["path"]=u.AbsolutePath;
        if(!IPAddress.TryParse(host,out _))n["domain_resolver"]="bootstrap";
        return n;
    }
    public static string SingBox(ClientNodeConfiguration n)
    {
        var p=PrivacyPolicyValidation.Check(n.Policy!,ClientType.SingBox,n.Host);
        var root=JsonNode.Parse(new SingBoxExporter().Generate(n with{Policy=null}))!.AsObject();
        var outbound=root["outbounds"]![0]!;outbound["tag"]="proxy-node";
        if(!IPAddress.TryParse(n.Host,out _))outbound["domain_resolver"]=p.Ipv6==Ipv6Route.Reject?J(new{server="bootstrap",strategy="ipv4_only"}):J(new{server="bootstrap"});
        root["outbounds"]!.AsArray().Add(new JsonObject{["type"]="direct",["tag"]="direct",["domain_resolver"]="dns-direct"});
        root["inbounds"]!.AsArray().Add(J(new{type="direct",tag="dns-in",listen="127.0.0.1",listen_port=1053}));
        var dnsServers=new JsonArray(Dns(p.BootstrapDns,"bootstrap","direct"),Dns(p.ProxyDns,"dns-proxy","proxy-node"),Dns(p.DirectDns,"dns-direct","direct"));
        if(p.AllowLan)dnsServers.Add(J(new{type="local",tag="dns-local"}));
        if(p.FakeIp)dnsServers.Add(J(new{type="fakeip",tag="dns-fake",inet4_range="198.18.0.0/15",inet6_range="fc00::/18"}));
        var dnsRules=new JsonArray();
        if(!IPAddress.TryParse(n.Host,out _))dnsRules.Add(J(new{domain=new[]{n.Host},server="bootstrap"}));
        if(p.Ipv6==Ipv6Route.Reject)dnsRules.Add(J(new{query_type=new[]{"AAAA"},action="reject"}));
        dnsRules.Add(p.AllowLan?J(new{domain_suffix=PrivacyPolicyValidation.LocalDomains,server="dns-local"}):J(new{domain_suffix=PrivacyPolicyValidation.LocalDomains,action="reject"}));
        if(p.RejectDomains.Length>0)dnsRules.Add(J(new{domain_suffix=p.RejectDomains,action="reject"}));
        if(p.DirectDomains.Length>0)dnsRules.Add(J(new{domain_suffix=p.DirectDomains,server="dns-direct"}));
        if(p.FakeIp)dnsRules.Add(J(new{query_type=p.Ipv6==Ipv6Route.Reject?new[]{"A"}:new[]{"A","AAAA"},server="dns-fake"}));
        root["dns"]=new JsonObject{["servers"]=dnsServers,["rules"]=dnsRules,["final"]="dns-proxy",["independent_cache"]=true,["disable_cache"]=!p.DnsCache};
        var rules=new JsonArray(J(new{inbound=new[]{"dns-in"},action="hijack-dns"}));
        if(p.RejectDomains.Length>0)rules.Add(J(new{domain_suffix=p.RejectDomains,action="reject"}));
        if(p.Ipv6!=Ipv6Route.Proxy)rules.Add(p.Ipv6==Ipv6Route.Reject?J(new{ip_cidr=new[]{"::/0"},action="reject"}):J(new{ip_cidr=new[]{"::/0"},outbound="direct"}));
        rules.Add(p.AllowLan?J(new{ip_cidr=PrivacyPolicyValidation.PrivateNetworks,outbound="direct"}):J(new{ip_cidr=PrivacyPolicyValidation.PrivateNetworks,action="reject"}));
        rules.Add(p.AllowLan?J(new{domain_suffix=PrivacyPolicyValidation.LocalDomains,outbound="direct"}):J(new{domain_suffix=PrivacyPolicyValidation.LocalDomains,action="reject"}));
        if(p.DirectDomains.Length>0)rules.Add(J(new{domain_suffix=p.DirectDomains,outbound="direct"}));
        if(p.ProxyDomains.Length>0)rules.Add(J(new{domain_suffix=p.ProxyDomains,outbound="proxy-node"}));
        root["route"]=new JsonObject{["rules"]=rules,["final"]="proxy-node",["default_domain_resolver"]="dns-proxy"};
        return root.ToJsonString(new JsonSerializerOptions{WriteIndented=true});
    }
    public static string Mihomo(ClientNodeConfiguration n)
    {
        var p=PrivacyPolicyValidation.Check(n.Policy!,ClientType.Mihomo,n.Host);
        var config=new MihomoExporter().Generate(n with{Policy=null,NodeName="NekoProxy · "+n.NodeName});
        var b=new StringBuilder(config[..config.IndexOf("rules:\n",StringComparison.Ordinal)]);
        static string Q(string s)=>JsonSerializer.Serialize(s);
        b.Append($"ipv6: {(p.Ipv6==Ipv6Route.Reject?"false":"true")}\ndns:\n  enable: true\n  listen: 127.0.0.1:1053\n  ipv6: {(p.Ipv6==Ipv6Route.Reject?"false":"true")}\n  use-hosts: false\n  use-system-hosts: false\n  respect-rules: true\n  enhanced-mode: {(p.FakeIp?"fake-ip":"redir-host")}\n  default-nameserver: [{Q(p.BootstrapDns)}]\n  proxy-server-nameserver: [{Q(p.BootstrapDns+"#DIRECT")}]\n  direct-nameserver: [{Q(p.DirectDns+"#DIRECT")}]\n  direct-nameserver-follow-policy: false\n  nameserver: [{Q(p.ProxyDns+"#Proxy")}]\n");
        if(p.FakeIp)
        {
            b.Append("  fake-ip-range: 198.18.0.1/16\n  fake-ip-filter:\n");
            foreach(var d in PrivacyPolicyValidation.LocalDomains.Concat(p.DirectDomains).Concat(p.RejectDomains).Append(n.Host))b.Append("    - "+Q("+."+d)+"\n");
        }
        b.Append("  nameserver-policy:\n");
        foreach(var d in PrivacyPolicyValidation.LocalDomains)b.Append("    "+Q("+."+d)+": "+Q(p.AllowLan?"system":"rcode://refused")+"\n");
        foreach(var d in p.DirectDomains)b.Append("    "+Q("+."+d)+": "+Q(p.DirectDns+"#DIRECT")+"\n");
        foreach(var d in p.RejectDomains)b.Append("    "+Q("+."+d)+": 'rcode://refused'\n");
        b.Append("rules:\n");
        foreach(var d in p.RejectDomains)b.Append("  - "+Q("DOMAIN-SUFFIX,"+d+",REJECT")+"\n");
        if(p.Ipv6!=Ipv6Route.Proxy)b.Append("  - "+Q("IP-CIDR6,::/0,"+(p.Ipv6==Ipv6Route.Reject?"REJECT":"DIRECT")+",no-resolve")+"\n");
        foreach(var cidr in PrivacyPolicyValidation.PrivateNetworks)b.Append("  - "+Q((cidr.Contains(':')?"IP-CIDR6,":"IP-CIDR,")+cidr+","+(p.AllowLan?"DIRECT":"REJECT")+",no-resolve")+"\n");
        foreach(var d in PrivacyPolicyValidation.LocalDomains)b.Append("  - "+Q("DOMAIN-SUFFIX,"+d+","+(p.AllowLan?"DIRECT":"REJECT"))+"\n");
        foreach(var d in p.DirectDomains)b.Append("  - "+Q("DOMAIN-SUFFIX,"+d+",DIRECT")+"\n");
        foreach(var d in p.ProxyDomains)b.Append("  - "+Q("DOMAIN-SUFFIX,"+d+",Proxy")+"\n");
        b.Append("  - MATCH,Proxy\n");return b.ToString();
    }
}
