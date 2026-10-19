using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;
namespace NekoProxy.Desktop.ViewModels;
public partial class DeploymentViewModel : ObservableObject
{
    private readonly IDeploymentService _deployment;
    private readonly IRealityKeyGenerator keyGenerator;
    private readonly DraftCredentials _credentials;
    private bool _loadingDraft;
    public IRealityTargetService? Targets {get;set;}
    public Func<ConnectionAttempt?>? LatestConnection {get;set;}
    public Action<OperationNotice>? Feedback {get;set;}
    partial void OnMessageChanged(string value)
    {
        if(value.Length==0||IsOperating)return;
        var error=value.StartsWith("请",StringComparison.Ordinal)||value.Contains("失败")||value.Contains("未完成")||value.Contains("没有完成")||value.Contains("尚未验证");
        Feedback?.Invoke(new(value,error?NoticeLevel.Error:NoticeLevel.Information));
    }
    public DeploymentViewModel(IRealityKeyGenerator keys,IDeploymentService deployment,IServerConfigurationGenerator? configurationGenerator=null)
    {keyGenerator=keys;_credentials=new(keys);_deployment=deployment;_generator=configurationGenerator??new NekoProxy.Deployment.ServerConfigurationGenerator();PrepareCredentials();}
    private void RememberCredentials(){if(!_loadingDraft)_credentials.Remember(Protocol,Method,new(IsReality?Uuid:HyPassword,_serverPrivateKey,PublicKey,ShortId));}
    private void PrepareCredentials(){if(_loadingDraft)return;var k=_credentials.Get(Protocol,Method);Uuid=IsReality?k.Credential:Uuid;HyPassword=IsReality?HyPassword:k.Credential;if(IsReality){PublicKey=k.PublicKey;ShortId=k.ShortId;_serverPrivateKey=k.PrivateKey;}}
    public string TlsTargetLabel=>IsReality?"REALITY 目标站点（不是证书域名）":"你的节点证书域名";
    public string PreparedStatus=>(IsReality?Guid.TryParse(Uuid,out _):!string.IsNullOrWhiteSpace(HyPassword))?"连接参数已自动准备；返回、重试与切页均保留当前凭据。":"当前凭据未读取或无效，请检查本地凭据保护状态。";
    public bool IsSniEditable=>!IsReality||CustomRealityTarget;
    partial void OnCustomRealityTargetChanged(bool value)=>OnPropertyChanged(nameof(IsSniEditable));
    [ObservableProperty] private bool advancedParameters;
    [ObservableProperty] private bool rotationRequested;
    [ObservableProperty] private bool customRealityTarget;
    [ObservableProperty] private string targetStatus="目标尚未从 VPS 验证。";
    [ObservableProperty] private string connectionDetail="";
    public System.Collections.ObjectModel.ObservableCollection<RealityTargetResult> TargetResults {get;}=new(NekoProxy.Deployment.RealityTargetService.Candidates.Select(h=>new RealityTargetResult(h,RealityTargetState.Unverified,"尚未验证")));
    [RelayCommand] private void ChooseTarget(string host){CustomRealityTarget=true;Sni=host;TargetStatus="已选择 "+host+"；需要从 VPS 验证。";}
    [RelayCommand] private void ConfirmRotation(){RotationRequested=false;RotateCredentials();}
    [RelayCommand] private void CancelRotation()=>RotationRequested=false;
    private void RequestRotation(){if(_serviceUnit is not null){RotationRequested=true;Message="重新生成后，已有客户端需要重新导入配置。";return;}RotateCredentials();}
    private void RotateCredentials(){_credentials.Regenerate(Protocol,Method);PrepareCredentials();Message="凭据已重新生成，请更新对应客户端配置。";}
    [RelayCommand] private async Task CheckTargetsAsync()
    {
        if(Targets is null){TargetStatus="目标检测服务未接入。";return;}
        await RunOperation(async token=>
        {
            var hosts=CustomRealityTarget?new[]{Sni.Trim()}:NekoProxy.Deployment.RealityTargetService.Candidates;
            var passed=false;
            foreach(var host in hosts)
            {
                TargetStatus="正在从 VPS 检查 "+host;
                RealityTargetResult result;try{result=await Targets.CheckAsync(ConnectionProfile(),host,TimeSpan.FromSeconds(SshTimeoutSeconds),KeyPassphrase,token);}catch{TargetStatus="SSH 未完成，候选尚未验证；保留草稿后可直接重试。";throw;}
                var old=TargetResults.ToList().FindIndex(x=>x.Host==host);if(old<0)TargetResults.Add(result);else TargetResults[old]=result;
                if(result.State==RealityTargetState.Verified){Sni=host;passed=true;TargetStatus=host+" · "+result.Reason;break;}
            }
            if(!passed){TargetStatus="候选未通过验证，请查看原因或输入自定义目标。";CustomRealityTarget=true;}
        });
    }
    private readonly IServerConfigurationGenerator _generator;
    private Guid _serverId=Guid.NewGuid(),_nodeId=Guid.NewGuid();
    public Func<LocalNodeDraft,bool,Task>? SaveLocalAsync { get; set; }
    [ObservableProperty] private string serverName="";
    [ObservableProperty] private string nodeName="";
    public IReadOnlyList<string> Protocols { get; } = ProtocolCatalog.Supported.Select(p=>p.Display()).ToArray();
    [ObservableProperty] private int step = 2;
    [ObservableProperty] private int protocolIndex;
    [ObservableProperty] private string host = "";
    [ObservableProperty] private string sshPort = "22";
    [ObservableProperty] private string username = "root";
    [ObservableProperty] private string privateKeyPath = "";
    [ObservableProperty] private string proxyPort = "443";
    [ObservableProperty] private string sni = "www.microsoft.com";
    [ObservableProperty,NotifyPropertyChangedFor(nameof(PreparedStatus))] private string uuid = "";
    [ObservableProperty] private string publicKey = "";
    [ObservableProperty] private string shortId = "";
    [ObservableProperty,NotifyPropertyChangedFor(nameof(PreparedStatus))] private string hyPassword = "";
    [ObservableProperty] private string message = "";
    // Persisted in the encrypted vault only after explicit local save.
    private string? _serverPrivateKey;
    public bool Step1 => Step == 1; public bool Step2 => Step == 2; public bool Step3 => Step == 3; public bool Step4 => Step == 4; public bool Step5 => Step == 5;
    public ProxyProtocol Protocol => ProtocolCatalog.Supported[Math.Clamp(ProtocolIndex,0,3)];
    public bool IsReality => ProtocolIndex == 0; public bool IsHysteria => ProtocolIndex==1;
    public bool IsTrojan => ProtocolIndex==2;public bool IsShadowsocks=>ProtocolIndex==3;
    public bool IsCertificate=>IsTrojan||IsHysteria; public bool NeedsTls=>!IsShadowsocks;
    public IReadOnlyList<string> Methods=>ShadowsocksKeys.Methods;
    [ObservableProperty] private string method=ShadowsocksKeys.DefaultMethod;
    [ObservableProperty] private string certificatePath="";
    [ObservableProperty] private string certificateKeyPath="";
    public Func<Task<string?>>? PickServerFileAsync{get;set;}
    [RelayCommand] private void GenerateCredential()=>RequestRotation();
    partial void OnMethodChanging(string value)=>RememberCredentials();
    partial void OnMethodChanged(string value)=>PrepareCredentials();
    private ServerNodeConfiguration Configuration() => new(new(string.IsNullOrWhiteSpace(ServerName)?Host.Trim():ServerName.Trim(),string.IsNullOrWhiteSpace(NodeName)?Protocol.Display()+" · "+ProxyPort:NodeName.Trim(),Host.Trim(),int.Parse(ProxyPort),Protocol,IsReality?Uuid:HyPassword,NeedsTls?Sni.Trim():"",IsReality?PublicKey:null,IsReality?ShortId:null,IsShadowsocks?Method:ShadowsocksKeys.DefaultMethod),IsReality?_serverPrivateKey:null,ManualCertificate?CertificatePath:null,ManualCertificate?CertificateKeyPath:null,CertificateOptions);
    public LocalNodeDraft BuildLocalDraft()
    {
        if(!int.TryParse(ProxyPort,out var proxyPort)||proxyPort is <1 or >65535)throw new ArgumentException("代理端口必须在 1–65535 之间。");
        if(!int.TryParse(SshPort,out var sshPort))throw new ArgumentException("请输入有效 SSH 端口。");
        var config=Configuration();_= _generator.Generate(config);
        var node=new ProxyNode(_nodeId,config.Client.NodeName,Protocol,proxyPort){Verification=_verification??new NodeVerification{GeneratedAt=DateTimeOffset.UtcNow},ParametersConfigured=true,ServiceUnit=_serviceUnit,LastDeployment=_lastDeployment,NeedsDeployment=_needsDeployment,Sni=config.Client.Sni,Method=config.Client.Method,RealityPublicKey=config.Client.RealityPublicKey??"",ShortId=config.Client.ShortId??"",CertificatePath=ManualCertificate?CertificatePath.Trim():"",CertificateKeyPath=ManualCertificate?CertificateKeyPath.Trim():"",AutomaticCertificate=CertificateOptions};
        var server=new ServerProfile(_serverId,config.Client.ServerName,Host.Trim(),"未设置","",sshPort,Username,IsPassword?null:PrivateKeyPath,[node]){Authentication=(SshAuthenticationKind)AuthenticationIndex};
        var error=ProfileValidation.Validate(server);if(error is not null)throw new ArgumentException(error);
        return new(server,node,new(config.Client.Credential,config.RealityPrivateKey));
    }
    [RelayCommand] private async Task SaveLocalNodeAsync()=>await SaveLocal(false);
    [RelayCommand] private async Task ExportClientAsync()=>await SaveLocal(true);
    private async Task SaveLocal(bool export)
    {
        try{var draft=BuildLocalDraft();if(SaveLocalAsync is null)throw new InvalidOperationException();await SaveLocalAsync(draft,export);Message="节点参数已保存到本机。";}
        catch(ArgumentException e){Message=e.Message;}catch(NekoProxy.Infrastructure.CredentialStorageUnavailableException e){Message=e.Message;}catch{Message="保存节点资料失败。";}
    }
    [RelayCommand] private async Task SaveServerFileAsync()
    {
        try { if(!int.TryParse(ProxyPort,out var port)||port is <1 or >65535)throw new ArgumentException("代理端口必须在 1–65535 之间。");var text=_generator.Generate(Configuration());var path=PickServerFileAsync is null?null:await PickServerFileAsync();if(path is null)return;await NekoProxy.Infrastructure.SensitiveConfigurationFile.SaveAsync(path,text);Message="服务端配置已在本机保存；证书和远程部署尚未验证。"; }
        catch(ArgumentException e){Message=e.Message;}catch{Message="服务端配置保存失败。";}
    }
    public bool CanBack => Step != 2&&!IsOperating; public bool CanNext => Step < 5&&!IsOperating;
    public string StepLabel => $"步骤 {(Step==2?1:Step==1?2:Step)} / 5";
    public string StepTitle => Step switch { 1 => "选择代理协议", 2 => "添加或选择服务器", 3 => "准备连接参数", 4 => "查看部署预览", _ => "执行与结果" };
    public string ContextTitle => Step switch { 1 => Protocol.Display(), 2 => "连接准备", 3 => "参数与安全", 4 => "预检摘要", _ => "执行状态" };
    public string ContextBody => Step switch {
        1 => IsTrojan ? "Trojan 走 TCP / TLS，可自动申请证书，也可以使用服务器上的已有证书。" : IsShadowsocks ? "SS2022 不使用 TLS。密钥会按所选方法自动准备。" : IsReality ? "使用 TCP + REALITY + XTLS Vision。部署时会检查 SNI 站点的基础握手。" : "Hysteria 2 使用 QUIC / UDP，需要域名和有效证书，也要放行 UDP 端口。",
        2 => "填写服务器登录资料，先确认连接和环境。首次连接需要核对主机指纹。",
        3 => IsShadowsocks ? "AES-128 密钥为 16 字节，另外两种方法为 32 字节。更换方法会准备对应密钥；切回原方法保留原草稿密钥。" : IsReality ? "公钥供客户端使用，私钥只用于服务端。保存节点时会加密存储，客户端导出不会包含它。" : "自动签发先检查 DNS 与验证端口。已有证书会检查域名、有效期及私钥匹配。",
        4 => LocalOnly?"这里只保存本机参数，服务器不会改动。": "检查左侧的目标、端口和写入位置。已有 NekoProxy 节点更新前会保留备份，其它代理服务不会覆盖。",
        _ => Outcome?.Success==true?"服务已经启动，可以保存节点并导出客户端配置。是否能正常访问，还需要用客户端验证。":IsOperating?"正在按步骤部署。你可以取消操作；已经写入的节点会尝试恢复原配置。":"还没有执行部署。" };
    public string ContextStatus => Outcome?.Success==true?"服务已启动，状态已确认。":IsOperating?"正在处理，请稍候。":LocalOnly?"只生成本地配置。":IsConnected?"最近连接检查通过；操作时会重新核验。":"还没有连接服务器。";
    public void RefreshLocalizedText(){OnPropertyChanged(nameof(Preview));OnPropertyChanged(nameof(RemotePreview));OnPropertyChanged(nameof(EnvironmentSummary));}
    private static string T(string text)=>Localization.LocalizationHub.Current.Translate(text);
    public string Preview => string.Join("\n",new[]{
        T("目标服务器：")+Host,T("SSH 用户与端口：")+Username+" · "+SshPort,
        T("代理协议：")+Protocol.Display(),T("监听端口：")+ProxyPort,
        T("SNI / 证书域名：")+(IsShadowsocks?"—":Sni),
        T("传输与安全：")+(IsReality?"TCP · REALITY · XTLS Vision · xtls-rprx-vision":IsShadowsocks?"TCP / UDP · SS2022":IsTrojan?"TCP · TLS":"QUIC / UDP · TLS"),
        T(Inspection is not null&&!LocalOnly?"系统、权限、端口与配置归属已检查。":"环境与配置所有权：待检测。"),T("证书 / SNI 兼容性尚未检测。")});
    partial void OnStepChanged(int value) { foreach (var n in new[] { nameof(Step1), nameof(Step2), nameof(Step3), nameof(Step4), nameof(Step5), nameof(CanBack), nameof(CanNext), nameof(StepLabel), nameof(StepTitle), nameof(Preview), nameof(ContextTitle), nameof(ContextBody), nameof(ContextStatus),nameof(NextLabel),nameof(CanExecuteDeployment),nameof(NextAvailable),nameof(ShowResultActions) }) OnPropertyChanged(n); }
    private readonly Dictionary<bool,string> _tlsTargets=[];
    partial void OnProtocolIndexChanging(int value){RememberCredentials();if(!_loadingDraft)_tlsTargets[IsReality]=Sni;}
    partial void OnProtocolIndexChanged(int value) { PrepareCredentials();if(!_loadingDraft)Sni=_tlsTargets.GetValueOrDefault(IsReality,IsReality?"www.microsoft.com":""); OnPropertyChanged(nameof(IsReality)); OnPropertyChanged(nameof(IsHysteria)); OnPropertyChanged(nameof(IsTrojan));OnPropertyChanged(nameof(IsShadowsocks));OnPropertyChanged(nameof(IsCertificate));OnPropertyChanged(nameof(ManualCertificate));OnPropertyChanged(nameof(CertificateOptions));OnPropertyChanged(nameof(NeedsTls));OnPropertyChanged(nameof(TlsTargetLabel));OnPropertyChanged(nameof(IsSniEditable));OnPropertyChanged(nameof(PreparedStatus)); OnPropertyChanged(nameof(ContextTitle)); OnPropertyChanged(nameof(ContextBody)); }
    [RelayCommand] private void GenerateKeys()=>RequestRotation();
    [RelayCommand] private async Task NextAsync()
    {
        Message = "";
        if (Step == 2)
        {
            if (!int.TryParse(SshPort, out var port)) { Message = "请输入有效 SSH 端口。"; return; }
            var error = ProfileValidation.Validate(new(Guid.NewGuid(), "待部署", Host.Trim(), "未设置", "", port, Username, IsPassword?null:PrivateKeyPath, []){Authentication=(SshAuthenticationKind)AuthenticationIndex});
            if (error is not null) { Message = error; return; }
            if (!LocalOnly&&!IsPassword&&!File.Exists(PrivateKeyPath)) { Message = "请选择存在的 SSH 私钥文件。"; return; }
            if(HasRemoteBackend&&!LocalOnly&&!IsConnected){Message="请先连接服务器并检查环境。";return;}
        }
        if (Step == 3)
        {
            if (!int.TryParse(ProxyPort, out var port) || port is < 1 or > 65535) { Message = "代理端口必须在 1–65535 之间。"; return; }
            if (!IsShadowsocks && (Uri.CheckHostName(Sni) != UriHostNameType.Dns || !Sni.Contains('.'))) { Message = "请输入有效 SNI / 证书域名。"; return; }
            if (IsReality && (_serverPrivateKey is null || !Guid.TryParse(Uuid, out _) || string.IsNullOrEmpty(ShortId))) { Message = "请先生成 REALITY 密钥材料。"; return; }
            if (IsCertificate && string.IsNullOrWhiteSpace(HyPassword)) { Message = "请输入节点认证资料。"; return; }
        }
        if(Step==3) { try { _=_generator.Generate(Configuration()); }catch(ArgumentException e){Message=e.Message;return;} }
        if(Step==3&&IsReality&&RemoteMode&&Targets is not null&&!TargetResults.Any(x=>x.Host==Sni&&x.State==RealityTargetState.Verified))
        {await CheckTargetsAsync();if(!TargetResults.Any(x=>x.Host==Sni&&x.State==RealityTargetState.Verified)){Message="REALITY 目标尚未验证，请重试或选择自定义目标。";return;}}
        if(Step==3&&HasRemoteBackend&&!LocalOnly)
        {
            await RunOperation(async token=>{Inspection=await ((IRemoteDeploymentService)_deployment).InspectAsync(BuildRequest(),token);DeploymentConfirmed=false;Step=4;Message=Inspection.CanDeploy?"检查已完成，请确认部署内容。":PreflightStatus;Feedback?.Invoke(new(Message,Inspection.CanDeploy?NoticeLevel.Success:NoticeLevel.Error));});return;
        }
        if(Step==4&&HasRemoteBackend&&!LocalOnly){await ExecuteAsync();return;}
        if(Step==2)Step=1;else if(Step==1)Step=3;else if(Step<5)Step++;
        if(Step==3&&IsReality&&RemoteMode&&IsConnected&&!CustomRealityTarget)await CheckTargetsAsync();
    }
    [RelayCommand] private void Back() { if(Step==1)Step=2;else if(Step==3)Step=1;else if(Step>3)Step--;Message=""; }
    [RelayCommand] private void Reset() { if(IsOperating){CancelOperation();return;}Step = 2; _loadingDraft=true;_editingDraft=false;_portEdited=false;_credentials.Clear();_tlsTargets.Clear();ProtocolIndex=0;Method=ShadowsocksKeys.DefaultMethod;ProxyPort="443";CustomRealityTarget=false;AdvancedParameters=false; Host = ""; PrivateKeyPath = ""; Uuid = ""; PublicKey = ""; ShortId = ""; HyPassword = ""; CertificatePath=""; CertificateKeyPath=""; _serverPrivateKey = null;ServerName="";NodeName="";_serverId=Guid.NewGuid();_nodeId=Guid.NewGuid();AuthenticationIndex=0;AutomaticCertificate=true;AcmeStaging=false;AcmeEmail="";DomainAuthorized=false;ResetRemote();Sni="www.microsoft.com";_loadingDraft=false;PrepareCredentials(); Message = "已创建新草稿，连接参数已自动准备。"; }
}
