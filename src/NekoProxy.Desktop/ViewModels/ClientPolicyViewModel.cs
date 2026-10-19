using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;

namespace NekoProxy.Desktop.ViewModels;
public partial class ClientPolicyViewModel : ObservableObject
{
    public IReadOnlyList<string> Modes {get;}=["智能模式（默认）","隐私优先","自定义"];
    public IReadOnlyList<string> Ipv6Choices {get;}=["IPv6 经代理","拒绝 IPv6","IPv6 直连（需明确同意）"];
    public IReadOnlyList<string> Upstreams {get;}=["https://1.1.1.1/dns-query","https://8.8.8.8/dns-query","tls://1.1.1.1"];
    [ObservableProperty] private int modeIndex;
    [ObservableProperty] private int ipv6Index;
    [ObservableProperty] private bool ipv6DirectConsent;
    [ObservableProperty] private bool allowLan=true;
    [ObservableProperty] private bool fakeIp;
    [ObservableProperty] private bool dnsCache=true;
    [ObservableProperty] private string proxyDns="https://1.1.1.1/dns-query";
    [ObservableProperty] private string directDns="https://1.1.1.1/dns-query";
    [ObservableProperty] private string bootstrapDns="https://1.1.1.1/dns-query";
    [ObservableProperty] private string directDomains="";
    [ObservableProperty] private string proxyDomains="";
    [ObservableProperty] private string rejectDomains="";
    [ObservableProperty] private string message="策略尚未导入客户端；本机系统网络保持原状。";
    public Func<ClientPrivacyPolicy,Task>? Save {get;set;}
    public bool Custom=>ModeIndex==2;
    public string PolicyHeader=>"客户端隐私与解析路径 · "+Modes[Math.Clamp(ModeIndex,0,Modes.Count-1)];
    public string PathExplanation=>PrivacyPolicyValidation.Explain(Build());
    partial void OnModeIndexChanged(int value){OnPropertyChanged(nameof(Custom));OnPropertyChanged(nameof(PathExplanation));OnPropertyChanged(nameof(PolicyHeader));}
    partial void OnIpv6IndexChanged(int value)=>OnPropertyChanged(nameof(PathExplanation));
    partial void OnAllowLanChanged(bool value)=>OnPropertyChanged(nameof(PathExplanation));
    public ClientPrivacyPolicy Build()=>new(){Mode=(PrivacyMode)ModeIndex,Ipv6=(Ipv6Route)Ipv6Index,Ipv6DirectConsent=Ipv6DirectConsent,AllowLan=AllowLan,FakeIp=FakeIp,DnsCache=DnsCache,ProxyDns=ProxyDns.Trim(),DirectDns=DirectDns.Trim(),BootstrapDns=BootstrapDns.Trim(),DirectDomains=Split(DirectDomains),ProxyDomains=Split(ProxyDomains),RejectDomains=Split(RejectDomains)};
    private static string[] Split(string s)=>s.Split(['\n','\r',',',';'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    public void Load(ClientPrivacyPolicy p){ModeIndex=(int)p.Mode;Ipv6Index=(int)p.Ipv6;Ipv6DirectConsent=p.Ipv6DirectConsent;AllowLan=p.AllowLan;FakeIp=p.FakeIp;DnsCache=p.DnsCache;ProxyDns=p.ProxyDns;DirectDns=p.DirectDns;BootstrapDns=p.BootstrapDns;DirectDomains=string.Join('\n',p.DirectDomains);ProxyDomains=string.Join('\n',p.ProxyDomains);RejectDomains=string.Join('\n',p.RejectDomains);}
    [RelayCommand] private void Reset(){Load(new());Message="已恢复智能模式，尚未保存为默认。";}
    [RelayCommand] private async Task SaveDefaultAsync()
    {
        try{var p=PrivacyPolicyValidation.Check(Build(),ClientType.SingBox,"192.0.2.1");if(Save is null)throw new InvalidOperationException();await Save(p);Message="已保存为此设备的默认导出策略。现有客户端配置不会被修改。";}
        catch(ArgumentException e){Message=e.Message;}catch{Message="默认策略保存失败，请检查本地存储。";}
    }
}
