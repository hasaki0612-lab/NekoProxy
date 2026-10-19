using System.Diagnostics;
using System.Text.Json;
using NekoProxy.Core;
using NekoProxy.Exporters;
using NekoProxy.Infrastructure;
using Xunit;
namespace NekoProxy.Tests;

public class ClientPrivacyTests
{
    public static ClientNodeConfiguration Node(ProxyProtocol protocol=ProxyProtocol.TrojanTls)=>new("Test","Proxy","192.0.2.1",443,protocol,protocol==ProxyProtocol.VlessReality?"7970c3ac-384c-44d9-83e5-31d262b71ddc":protocol==ProxyProtocol.Shadowsocks2022?Convert.ToBase64String(new byte[16]):"fixture-password-only",protocol==ProxyProtocol.Shadowsocks2022?"":"example.com",protocol==ProxyProtocol.VlessReality?"XG0_DkRA-CV6RZUmk9onb8X9TEv9hFRFV-xWDP0E4xY":null,protocol==ProxyProtocol.VlessReality?"aabbccdd":null);
    [Theory][InlineData("udp://1.1.1.1")][InlineData("http://example.com/dns-query")][InlineData("https://a:b@example.com/")][InlineData("https://1.1.1.1/dns-query#DIRECT")]
    public void DnsRejectsCleartextAndInjectedRouting(string endpoint)=>Assert.Throws<ArgumentException>(()=>PrivacyPolicyValidation.DnsEndpoint(endpoint));
    [Fact]public void DomainBootstrapLoopsAreRejected()=>Assert.Throws<ArgumentException>(()=>PrivacyPolicyValidation.Check(new(){BootstrapDns="https://dns.example.com/dns-query"},ClientType.SingBox,"example.org"));
    [Theory][InlineData(ClientType.SingBox)][InlineData(ClientType.Mihomo)]
    public void CrossActionSuffixConflictsAreRejected(ClientType client)=>Assert.Throws<ArgumentException>(()=>PrivacyPolicyValidation.Check(new(){Mode=PrivacyMode.Custom,DirectDomains=["example.com"],RejectDomains=["ads.example.com"]},client,"192.0.2.1"));
    [Fact]public void Ipv6BypassRequiresConsent()=>Assert.Throws<ArgumentException>(()=>PrivacyPolicyValidation.Check(new(){Mode=PrivacyMode.Custom,Ipv6=Ipv6Route.Direct},ClientType.SingBox,"192.0.2.1"));
    [Theory][InlineData("lan")][InlineData("api.local")][InlineData("node.example.com")]
    public void ReservedDomainRoutesCannotConflict(string domain)=>Assert.Throws<ArgumentException>(()=>PrivacyPolicyValidation.Check(new(){Mode=PrivacyMode.Custom,DirectDomains=[domain]},ClientType.SingBox,"node.example.com"));
    [Fact]public void UnrepresentableClientOptionsFailExplicitly()
    {
        Assert.Throws<ArgumentException>(()=>new ShadowrocketExporter().Generate(Node() with{Policy=new()}));
        Assert.Throws<ArgumentException>(()=>new MihomoExporter().Generate(Node() with{Policy=new(){Mode=PrivacyMode.Custom,DnsCache=false}}));
        Assert.Contains("trojan://",new ShadowrocketExporter().GenerateLink(Node()));
    }
    [Fact]public void PrivacyPresetRejectsIpv6AndLanWithoutDirectFallback()
    {
        var s=new SingBoxExporter().Generate(Node() with{Policy=new(){Mode=PrivacyMode.Privacy}});
        using var d=JsonDocument.Parse(s);var r=d.RootElement;
        Assert.Equal("proxy-node",r.GetProperty("route").GetProperty("final").GetString());
        Assert.Equal("proxy-node",r.GetProperty("dns").GetProperty("servers")[1].GetProperty("detour").GetString());
        Assert.DoesNotContain("insecure",s);Assert.DoesNotContain("tun",s);Assert.Contains("reject",s);
        Assert.DoesNotContain(r.GetProperty("dns").GetProperty("servers").EnumerateArray(),x=>x.GetProperty("type").GetString()=="local");
    }
    [Fact]public async Task DefaultsPersistWithoutCredentialsAndOldSettingsRemainCompatible()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        try{Directory.CreateDirectory(dir);var store=new JsonSettingsStore(dir);await store.SaveAsync(new(){ClientPolicy=new(){Mode=PrivacyMode.Custom,DirectDomains=["example.com"]}});Assert.Equal("example.com",(await store.LoadAsync()).ClientPolicy.DirectDomains.Single());Assert.Equal(PrivacyMode.Smart,JsonSerializer.Deserialize<AppSettings>("{}")!.ClientPolicy.Mode);}
        finally{Directory.Delete(dir,true);}
    }
    [Fact]public async Task LinkExportIsExplicitAndCarriesNoPolicy()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        try{var service=new ExportService([new ShadowrocketExporter()]);var r=await service.ExportLinkAsync(Node() with{Policy=new(){Mode=PrivacyMode.Privacy}},ClientType.Shadowrocket,dir);Assert.Contains("node-link",r.FilePath);Assert.StartsWith("trojan://",await File.ReadAllTextAsync(r.FilePath));}
        finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}

public sealed class ClientCoreFactAttribute:FactAttribute
{
    public ClientCoreFactAttribute(){if(Environment.GetEnvironmentVariable("NEKOPROXY_CORE_ROOT") is null)Skip="Set NEKOPROXY_CORE_ROOT to the existing trusted core tools folder.";}
}
public class ClientCoreChecks
{
    [ClientCoreFact]public async Task GeneratedPoliciesPassBothPinnedCores()
    {
        var root=Environment.GetEnvironmentVariable("NEKOPROXY_CORE_ROOT")!;
        var dir=Path.Combine(Path.GetTempPath(),"NekoProxy-core-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            foreach(var protocol in Enum.GetValues<ProxyProtocol>())foreach(var mode in Enum.GetValues<PrivacyMode>())foreach(var fake in mode==PrivacyMode.Custom?new[]{false,true}:new[]{false})
            {
                var n=ClientPrivacyTests.Node(protocol) with{Policy=new(){Mode=mode,FakeIp=fake,DirectDomains=mode==PrivacyMode.Custom?["direct.example"]:[],RejectDomains=mode==PrivacyMode.Custom?["reject.example"]:[]}};
                var file=Path.Combine(dir,"config.json");await File.WriteAllTextAsync(file,new SingBoxExporter().Generate(n));
                await Check(TestCorePaths.SingBox(root),["check","-c",file]);
                file=Path.Combine(dir,"config.yaml");await File.WriteAllTextAsync(file,new MihomoExporter().Generate(n));
                await Check(TestCorePaths.Mihomo(root),["-t","-d",dir,"-f",file]);
            }
        }
        finally{Directory.Delete(dir,true);}
    }
    private static async Task Check(string exe,string[] args)
    {
        var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};foreach(var a in args)start.ArgumentList.Add(a);
        using var p=Process.Start(start)!;var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try{await p.WaitForExitAsync(timeout.Token);}catch{p.Kill(true);throw;}
        Assert.True(p.ExitCode==0,(await output)+(await error));
    }
}
