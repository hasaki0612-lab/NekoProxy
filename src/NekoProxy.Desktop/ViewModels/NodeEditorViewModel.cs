using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;
using NekoProxy.Deployment;
namespace NekoProxy.Desktop.ViewModels;

public partial class NodeEditorViewModel : ObservableObject
{
    public ServerProfile Server { get; }
    public ProxyNode? Original { get; }
    private readonly Guid _nodeId;
    private readonly IServerConfigurationGenerator _generator;
    private readonly IRealityKeyGenerator _keys;
    private readonly DraftCredentials _credentials;
    private bool _loading=true;
    [ObservableProperty] private bool rotationRequested;
    private void Remember(){if(!_loading)_credentials.Remember(Protocol,Method,new(Credential,RealityPrivateKey,RealityPublicKey,ShortId));}
    private void Prepare(){if(_loading)return;var k=_credentials.Get(Protocol,Method);Credential=k.Credential;RealityPrivateKey=k.PrivateKey??"";RealityPublicKey=k.PublicKey;ShortId=k.ShortId;}
    [RelayCommand] private void ConfirmRotation(){RotationRequested=false;Rotate();}
    [RelayCommand] private void CancelRotation()=>RotationRequested=false;
    private void Rotate(){_credentials.Regenerate(Protocol,Method);Prepare();Error="已重新生成，请重新导出对应客户端配置。";}
    public IReadOnlyList<string> Protocols { get; } = ProtocolCatalog.Supported.Select(p=>p.Display()).ToArray();
    public IReadOnlyList<string> Methods => ShadowsocksKeys.Methods;
    public Func<Task<string?>>? PickServerFileAsync { get; init; }
    [ObservableProperty] private string name="";
    [ObservableProperty] private string port="443";
    [ObservableProperty] private int protocolIndex;
    [ObservableProperty] private string error="";
    [ObservableProperty] private string sni="";
    [ObservableProperty] private string credential="";
    [ObservableProperty] private string method=ShadowsocksKeys.DefaultMethod;
    [ObservableProperty] private string realityPublicKey="";
    [ObservableProperty] private string realityPrivateKey="";
    [ObservableProperty] private string shortId="";
    [ObservableProperty] private string certificatePath="";
    [ObservableProperty] private string certificateKeyPath="";
    [ObservableProperty] private bool metadataOnly;
    [ObservableProperty] private bool automaticCertificate;
    [ObservableProperty] private string acmeEmail="";
    [ObservableProperty] private int acmeChallengeIndex;
    [ObservableProperty] private bool acmeStaging=true;
    [ObservableProperty] private bool domainAuthorized;
    public IReadOnlyList<string> CertificateChallenges {get;}=["HTTP-01 · TCP 80","TLS-ALPN-01 · TCP 443"];
    public bool ManualCertificate=>IsCertificate&&!AutomaticCertificate;
    public AutomaticCertificateOptions? CertificateOptions=>IsCertificate&&AutomaticCertificate?new(AcmeEmail.Trim(),(CertificateChallenge)AcmeChallengeIndex,AcmeStaging,DomainAuthorized):null;
    partial void OnAutomaticCertificateChanged(bool value)=>OnPropertyChanged(nameof(ManualCertificate));
    public ProxyProtocol Protocol => ProtocolCatalog.Supported[Math.Clamp(ProtocolIndex,0,3)];
    public bool IsReality => Protocol==ProxyProtocol.VlessReality;
    public bool IsShadowsocks => Protocol==ProxyProtocol.Shadowsocks2022;
    public bool IsCertificate => Protocol is ProxyProtocol.TrojanTls or ProxyProtocol.Hysteria2;
    public bool NeedsTls => !IsShadowsocks;
    public NodeEditorViewModel(ServerProfile server,ProxyNode? original=null,IServerConfigurationGenerator? generator=null,IRealityKeyGenerator? keys=null)
    {
        Server=server;Original=original;_nodeId=original?.Id??Guid.NewGuid();_generator=generator??new ServerConfigurationGenerator();_keys=keys??new RealityKeyGenerator();_credentials=new(_keys);

        if(original is {} n)
        {
            Name=n.Name;Port=n.Port.ToString();ProtocolIndex=ProtocolCatalog.Supported.ToList().IndexOf(n.Protocol);
            Sni=n.Sni;Method=n.Method;CertificatePath=n.CertificatePath;CertificateKeyPath=n.CertificateKeyPath;
            RealityPublicKey=n.RealityPublicKey;ShortId=n.ShortId;MetadataOnly=!n.ParametersConfigured;
            AutomaticCertificate=n.AutomaticCertificate is not null;
            if(n.AutomaticCertificate is {} a){AcmeEmail=a.Email;AcmeChallengeIndex=(int)a.Challenge;AcmeStaging=a.Staging;DomainAuthorized=a.DomainAuthorized;}
        }
        _loading=false;if(original is null)Prepare();
    }
    partial void OnProtocolIndexChanging(int value)=>Remember();
    partial void OnProtocolIndexChanged(int value)
    {
        Prepare();
        foreach(var n in new[]{nameof(IsReality),nameof(IsShadowsocks),nameof(IsCertificate),nameof(NeedsTls),nameof(ManualCertificate)})OnPropertyChanged(n);
    }
    partial void OnMethodChanging(string value)=>Remember();
    partial void OnMethodChanged(string value)=>Prepare();
    public void ApplySecrets(NodeSecrets secrets)
    {
        Credential=secrets.Credential;RealityPrivateKey=secrets.RealityPrivateKey??"";
        Remember();
        if(Original is { } n && !string.IsNullOrEmpty(Credential) && (n.Protocol is ProxyProtocol.TrojanTls or ProxyProtocol.Shadowsocks2022))MetadataOnly=false;
    }
    [RelayCommand] private void GenerateCredential()
    {
        if(Original is not null){RotationRequested=true;Error="重新生成会影响已有客户端；请确认后更新客户端配置。";return;}Rotate();
    }
    public ClientNodeConfiguration BuildClient()=>new(Server.Name,Name.Trim(),Server.Host,int.Parse(Port),Protocol,Credential,
        NeedsTls?Sni.Trim():"",IsReality?RealityPublicKey.Trim():null,IsReality?ShortId.Trim():null,IsShadowsocks?Method:ShadowsocksKeys.DefaultMethod);
    public NodeSecrets BuildSecrets()=>new(Credential,IsReality&&!string.IsNullOrEmpty(RealityPrivateKey)?RealityPrivateKey:null);
    public ProxyNode? Build()
    {
        Error="";
        if(string.IsNullOrWhiteSpace(Name)){Error="请输入节点名称。";return null;}
        if(!int.TryParse(Port,out var number)||number is <1 or >65535){Error="监听端口必须在 1–65535 之间。";return null;}
        if(MetadataOnly)
        {
            var changed=Original is null||Original.Port!=number||Original.Protocol!=Protocol;
            return (Original??new(_nodeId,Name.Trim(),Protocol,number)) with {Name=Name.Trim(),Port=number,Protocol=Protocol,ParametersConfigured=!changed&&Original?.ParametersConfigured==true,NeedsDeployment=Original?.NeedsDeployment==true||changed&&Original?.ServiceUnit is not null,Verification=changed?null:Original?.Verification};
        }
        try
        {
            ProtocolParameters.ValidateClient(BuildClient());
            if(CertificateOptions is {} automatic)CertificatePolicy.Validate(BuildClient(),automatic);
            if(IsReality&&!string.IsNullOrEmpty(RealityPrivateKey))_= _generator.Generate(new(BuildClient(),RealityPrivateKey));
        }
        catch(ArgumentException e){Error=e.Message;return null;}
        return new(_nodeId,Name.Trim(),Protocol,number)
        {
            ServiceUnit=Original?.ServiceUnit,LastDeployment=Original?.LastDeployment,NeedsDeployment=Original?.ServiceUnit is not null,
            ParametersConfigured=true,Sni=NeedsTls?Sni.Trim():"",Method=IsShadowsocks?Method:ShadowsocksKeys.DefaultMethod,
            CertificatePath=ManualCertificate?CertificatePath.Trim():"",CertificateKeyPath=ManualCertificate?CertificateKeyPath.Trim():"",AutomaticCertificate=CertificateOptions,
            RealityPublicKey=IsReality?RealityPublicKey.Trim():"",ShortId=IsReality?ShortId.Trim():""
        };
    }
    [RelayCommand] private async Task SaveServerFileAsync()
    {
        try
        {
            if(MetadataOnly){Error="请先补全节点参数。";return;}
            if(Build() is null)return;
            var text=_generator.Generate(new(BuildClient(),IsReality?RealityPrivateKey:null,ManualCertificate?CertificatePath:null,ManualCertificate?CertificateKeyPath:null,CertificateOptions));
            var file=PickServerFileAsync is null?null:await PickServerFileAsync();if(file is null)return;
            await NekoProxy.Infrastructure.SensitiveConfigurationFile.SaveAsync(file,text);
            Error="服务端配置已在本机保存；证书和远程部署尚未验证。";
        }
        catch(ArgumentException e){Error=e.Message;}catch{Error="服务端配置保存失败。";}
    }
    public void ClearSecrets(){Credential="";RealityPrivateKey="";}
}
