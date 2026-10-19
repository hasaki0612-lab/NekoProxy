using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;
namespace NekoProxy.Desktop.ViewModels;

public partial class DeploymentViewModel
{
    public IReadOnlyList<string> AuthenticationChoices {get;}=["SSH 私钥","SSH 登录密码"];
    [ObservableProperty] private int authenticationIndex;
    public bool IsPassword=>AuthenticationIndex==1;
    public string LoginSecretLabel=>IsPassword?"SSH 登录密码":"SSH 私钥口令（可选）";
    [ObservableProperty] private bool automaticCertificate=true;
    [ObservableProperty] private string acmeEmail="";
    [ObservableProperty] private int acmeChallengeIndex;
    [ObservableProperty] private bool acmeStaging;
    [ObservableProperty] private bool domainAuthorized;
    public IReadOnlyList<string> CertificateChallenges {get;}=["HTTP-01 · TCP 80","TLS-ALPN-01 · TCP 443"];
    public AutomaticCertificateOptions? CertificateOptions=>IsCertificate&&AutomaticCertificate?new(AcmeEmail.Trim(),(CertificateChallenge)AcmeChallengeIndex,AcmeStaging,DomainAuthorized):null;
    public bool ManualCertificate=>IsCertificate&&!AutomaticCertificate;
    partial void OnAuthenticationIndexChanged(int value){InvalidateConnection();KeyPassphrase="";OnPropertyChanged(nameof(IsPassword));OnPropertyChanged(nameof(LoginSecretLabel));}
    partial void OnAutomaticCertificateChanged(bool value){DraftChanged();OnPropertyChanged(nameof(ManualCertificate));OnPropertyChanged(nameof(CertificateOptions));}
    partial void OnAcmeEmailChanged(string value)=>DraftChanged();
    partial void OnAcmeChallengeIndexChanged(int value)=>DraftChanged();
    partial void OnAcmeStagingChanged(bool value)=>DraftChanged();
    private CancellationTokenSource? _operation;
    private string? _serviceUnit;
    private DateTimeOffset? _lastDeployment;
    private bool _needsDeployment;
    private NodeVerification? _verification;
    public string ResultVerification=>(Outcome is {} result?new NodeVerification{GeneratedAt=result.ConfigurationGenerated?DateTimeOffset.UtcNow:null,CoreCheckedAt=result.CoreChecked?DateTimeOffset.UtcNow:null,DeployedAt=result.Success?DateTimeOffset.UtcNow:null,ServiceStartedAt=result.ServiceStarted?DateTimeOffset.UtcNow:null}:new NodeVerification()).Summary;
    private bool _editingDraft, _portEdited;
    private TaskCompletionSource? _completion;
    public Task PendingOperation=>_completion?.Task??Task.CompletedTask;
    public int SshTimeoutSeconds {get;set;}=15;
    public Action? ShowManagement {get;set;}
    public Func<Task<string?>>? PickPrivateKeyAsync {get;set;}
    public ObservableCollection<DeploymentProgress> ProgressSteps {get;}=[];
    [ObservableProperty] private bool isOperating;
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private bool localOnly;
    [ObservableProperty] private string keyPassphrase="";
    [ObservableProperty] private RemoteInspection? inspection;
    [ObservableProperty] private RemoteDeploymentOutcome? outcome;
    [ObservableProperty] private bool deploymentConfirmed;
    [ObservableProperty] private bool backupExisting;
    public bool HasRemoteBackend=>_deployment is IRemoteDeploymentService;
    public Guid ConnectionServerId=>_serverId;
    public void UseServer(ServerProfile server)
    {
        if(IsOperating)return;
        ResetCommand.Execute(null);_serverId=server.Id;Host=server.Host;ServerName=server.Name;SshPort=server.SshPort.ToString();Username=server.Username;AuthenticationIndex=(int)server.Authentication;PrivateKeyPath=server.PrivateKeyPath??"";Step=2;
    }
    public bool RemoteMode=>HasRemoteBackend&&!LocalOnly;
    public bool NextAvailable=>CanNext&&(Step!=4||!RemoteMode||CanExecuteDeployment);
    public bool HasInspection=>Inspection is not null;
    public bool HasOutcome=>Outcome is not null;
    public bool DeploymentSucceeded=>Outcome?.Success==true;
    public bool ShowResultActions=>Step5&&DeploymentSucceeded;
    public string ReviewSummary=>T("服务器：")+(string.IsNullOrWhiteSpace(ServerName)?Host:ServerName)+"\n"+T("代理协议：")+Protocol.Display()+" · "+ProxyPort+"\n"+T("目标地址：")+Host+"\n"+(IsReality?T("REALITY 目标：")+Sni:IsCertificate?T("证书域名：")+Sni:T("SS2022 密钥已准备，协议不使用 TLS。"))+"\n"+T("仅更新此 NekoProxy 节点，保留已有配置备份。")+"\n"+T("客户端连接：部署后单独验证。");
    public bool CanExecuteDeployment=>HasRemoteBackend&&!LocalOnly&&!IsOperating&&Step==4&&DeploymentConfirmed&&Inspection?.CanDeploy==true&&(!Inspection.OwnedConfiguration||BackupExisting);
    public string NextLabel=>Step==4&&!LocalOnly&&HasRemoteBackend?"执行部署":"下一步";
    public string ConnectionStatus=>IsOperating?"正在处理…":IsConnected?"最近 SSH 与环境检查通过；不代表持续在线。":"还没有连接服务器。";
    public string EnvironmentSummary=>Inspection is {} i?string.Join(" · ",i.Distribution,i.Architecture,i.PackageManager,T(i.CanElevate?"有部署权限":"需要 root 或免密 sudo")):"还没有读取服务器信息。";
    public string RemotePreview
    {
        get
        {
            if(Inspection is not {} i)return T("连接后会显示系统、权限和端口检查结果。");
            var p=new NekoProxy.Deployment.RemotePaths(_nodeId);
            return string.Join("\n",new[]{T("系统：")+i.Distribution+" / "+i.Architecture,T("systemd：")+T(i.Systemd?"可用":"不可用"),
                T("权限：")+T(i.CanElevate?"有部署权限":"需要 root 或免密 sudo"),T("端口：")+ProxyPort+" · "+T(i.PortAvailable?"可以使用":"已占用，或归属不明"),
                T("现有 sing-box：")+T(i.SingBoxInstalled?"已检测到，原服务保持不变":"未检测到"),
                T("配置目录：")+p.Directory,T("systemd 服务：")+p.Unit,T("写入方式：")+T(i.ForeignConfiguration?"归属不明，停止部署":i.OwnedConfiguration?"已有节点，可保留备份后更新":"创建新节点"),
                T("内核：")+NekoProxy.Deployment.ServerConfigurationGenerator.TargetKernelVersion+" · SHA-256",
                T("证书：")+T(IsCertificate?(AutomaticCertificate?(i.AcmeDnsMatches&&i.AcmePortAvailable?"DNS 与验证端口可用，执行部署时签发":"DNS 或验证端口检查未通过"):i.CertificatesReadable?"文件可读，部署时校验域名和有效期":"文件不存在或不可读"):IsReality?"部署时检查 REALITY 目标握手":"此协议不使用 TLS"),
                T("目录：")+T(i.ConfigRootExists?"NekoProxy 配置目录已存在":"将创建 NekoProxy 配置目录"),
                T("依赖：缺失时安装 curl、CA 证书、tar 和 OpenSSL。"),T("防火墙规则不会自动修改，请自行放行节点端口。")});
        }
    }
    public string PreflightStatus=>Inspection is not {} i?"还没有完成部署检查。":i.CanDeploy?"环境检查通过，可以确认部署。":!i.CanElevate?"需要 root 或免密 sudo 权限。":!i.PortAvailable?"端口已占用，或无法确认归属。":!i.CertificatesReadable?"请检查服务器上的证书和私钥路径。":!i.AcmeDnsMatches?"域名解析未完全指向目标服务器，证书申请已停止。":!i.AcmePortAvailable?"ACME 验证端口已占用或无法检查。不会自动停止现有服务。":i.ForeignConfiguration?"目标配置不属于 NekoProxy，不会覆盖。":"这台服务器暂不满足部署要求。";
    public void LoadSavedDraft(LocalNodeDraft draft)
    {
        if(IsOperating)return;
        _loadingDraft=true;_editingDraft=true;_credentials.Clear();
        ProtocolIndex=ProtocolCatalog.Supported.ToList().IndexOf(draft.Node.Protocol);Host=draft.Server.Host;SshPort=draft.Server.SshPort.ToString();Username=draft.Server.Username;PrivateKeyPath=draft.Server.PrivateKeyPath??"";
        ProxyPort=draft.Node.Port.ToString();ServerName=draft.Server.Name;NodeName=draft.Node.Name;Sni=draft.Node.Sni;Method=draft.Node.Method;
        CertificatePath=draft.Node.CertificatePath;CertificateKeyPath=draft.Node.CertificateKeyPath;Uuid=IsReality?draft.Secrets.Credential:"";HyPassword=IsReality?"":draft.Secrets.Credential;
        PublicKey=draft.Node.RealityPublicKey;ShortId=draft.Node.ShortId;_serverPrivateKey=draft.Secrets.RealityPrivateKey;
        AuthenticationIndex=(int)draft.Server.Authentication;AutomaticCertificate=draft.Node.AutomaticCertificate is not null;AcmeEmail=draft.Node.AutomaticCertificate?.Email??"";AcmeChallengeIndex=(int)(draft.Node.AutomaticCertificate?.Challenge??CertificateChallenge.Http01);AcmeStaging=draft.Node.AutomaticCertificate?.Staging??true;DomainAuthorized=draft.Node.AutomaticCertificate?.DomainAuthorized??false;
        _serverId=draft.Server.Id;_nodeId=draft.Node.Id;_serviceUnit=draft.Node.ServiceUnit;_lastDeployment=draft.Node.LastDeployment;_needsDeployment=draft.Node.NeedsDeployment;_verification=draft.Node.Verification;InvalidateConnection();Outcome=null;LocalOnly=false;KeyPassphrase="";_loadingDraft=false;RememberCredentials();Step=2;ProgressSteps.Clear();Message="参数已载入，请先连接服务器检查当前环境。";
    }
    private RemoteDeploymentRequest BuildRequest()=>new(BuildLocalDraft(),Configuration(),TimeSpan.FromSeconds(SshTimeoutSeconds),BackupExisting?ExistingDeploymentPolicy.BackupAndReplace:ExistingDeploymentPolicy.Cancel,KeyPassphrase);
    private ServerProfile ConnectionProfile()
    {
        if(!int.TryParse(SshPort,out var port))throw new ArgumentException("请输入有效 SSH 端口。");
        var server=new ServerProfile(_serverId,string.IsNullOrWhiteSpace(ServerName)?Host:ServerName,Host.Trim(),"未设置","",port,Username,PrivateKeyPath,[]){Authentication=(SshAuthenticationKind)AuthenticationIndex};
        var error=ProfileValidation.Validate(server);if(error is not null)throw new ArgumentException(error);return server;
    }
    [RelayCommand] private async Task ConnectAsync()
    {
        if(LocalOnly||_deployment is not IRemoteDeploymentService remote)return;
        await RunOperation(async token=>
        {
            var server=ConnectionProfile();if(!int.TryParse(ProxyPort,out var port)||port is <1 or >65535)throw new ArgumentException("代理端口必须在 1–65535 之间。");
            var inspection=await remote.InspectHostAsync(server,new(_nodeId,Protocol.Display(),Protocol,port),TimeSpan.FromSeconds(SshTimeoutSeconds),KeyPassphrase,token);
            var selected=false;
            if(!_editingDraft&&!_portEdited&&port==443&&!inspection.PortAvailable)
                foreach(var candidate in new[]{8443,9443,10443})
                {
                    var available=await remote.InspectHostAsync(server,new(_nodeId,Protocol.Display(),Protocol,candidate),TimeSpan.FromSeconds(SshTimeoutSeconds),KeyPassphrase,token);
                    if(!available.PortAvailable)continue;
                    ProxyPort=candidate.ToString();inspection=available;selected=true;break;
                }
            Inspection=inspection;IsConnected=true;ConnectionDetail=LatestConnection?.Invoke() is {} attempt?attempt.Summary+"\n"+string.Join("\n",attempt.Records.Select(r=>r.Display)):"检查成功；连接状态会在操作时重新核验，不代表持续在线。";
            Message=selected?$"连接检查通过；443 已占用，已准备端口 {ProxyPort}。公网客户端连通性仍需验证。":"最近连接与环境检查通过，可以继续。";
            Feedback?.Invoke(new(Message,NoticeLevel.Success));
        });
    }
    [RelayCommand] private async Task BrowseKeyAsync()
    {var path=PickPrivateKeyAsync is null?null:await PickPrivateKeyAsync();if(path is not null)PrivateKeyPath=path;}
    [RelayCommand] private void OpenManagement()=>ShowManagement?.Invoke();
    [RelayCommand] private void ReviewDeployment(){if(!IsOperating)Step=4;}
    [RelayCommand] private void CancelOperation(){_operation?.Cancel();Message="正在取消，请稍候。已写入的节点会尝试恢复原配置。";}
    public async Task CancelAndWaitAsync(){_operation?.Cancel();await PendingOperation;}
    private async Task RunOperation(Func<CancellationToken,Task> action)
    {
        if(IsOperating)return;IsOperating=true;_operation=new();_completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        try{await action(_operation.Token);}
        catch(OperationCanceledException){Message="操作已取消。";}
        catch(SshConnectionException e){IsConnected=false;Inspection=null;if(e.Kind is SshFailureKind.Authentication or SshFailureKind.KeyFile)KeyPassphrase="";Message=e.Message;ConnectionDetail=e.Attempt is {} a?a.Summary+"\n"+string.Join("\n",a.Records.Select(r=>r.Display)):e.SafeStandardError??"";Feedback?.Invoke(new(Message,NoticeLevel.Error));}
        catch(ArgumentException e){Message=e.Message;Feedback?.Invoke(new(Message,NoticeLevel.Error));}
        catch{Message="操作没有完成，请检查连接信息和服务器权限。";Feedback?.Invoke(new(Message,NoticeLevel.Error));}
        finally{_operation.Dispose();_operation=null;IsOperating=false;_completion.TrySetResult();}
    }
    [RelayCommand] private async Task ExecuteAsync()
    {
        if(_deployment is not IRemoteDeploymentService remote)
        {Message=(await _deployment.DeployAsync(new(Protocol,ConnectionProfile(),Preview))).Message;return;}
        if(!CanExecuteDeployment){Message=Inspection?.CanDeploy!=true?PreflightStatus:Inspection.OwnedConfiguration&&!BackupExisting?"请先勾选保留备份，再更新已有节点。":"请先勾选确认部署内容，再执行部署。";Feedback?.Invoke(new(Message,NoticeLevel.Error));return;}
        await RunOperation(async token=>
        {
            var request=BuildRequest();
            // Explicit deployment also saves recovery credentials before any remote write.
            if(SaveLocalAsync is null)throw new InvalidOperationException();await SaveLocalAsync(request.Draft,false);
            Step=5;ProgressSteps.Clear();Outcome=null;
            var progress=new Progress<DeploymentProgress>(p=>{var index=ProgressSteps.ToList().FindIndex(s=>s.Step==p.Step);if(index<0)ProgressSteps.Add(p);else ProgressSteps[index]=p;});
            Outcome=await remote.DeployRemoteAsync(request,progress,token);Message=Outcome.Message;
            Feedback?.Invoke(new(Outcome.Success?"服务部署完成。请导出配置，在客户端导入后验证 HTTPS 访问。":Outcome.Message,Outcome.Success?NoticeLevel.Success:NoticeLevel.Error));
            if(Outcome.Success)
            {
                _serviceUnit=Outcome.Unit;_lastDeployment=DateTimeOffset.UtcNow;_needsDeployment=false;
                _verification=new(){GeneratedAt=request.Draft.Node.Verification?.GeneratedAt,CoreCheckedAt=Outcome.CoreChecked?_lastDeployment:null,DeployedAt=_lastDeployment,ServiceStartedAt=Outcome.ServiceStarted?_lastDeployment:null};
                var saved=request.Draft with{Node=request.Draft.Node with{ServiceUnit=Outcome.Unit,LastDeployment=_lastDeployment,NeedsDeployment=false,Verification=_verification}};
                try{await SaveLocalAsync(saved,false);}catch{Message="服务已部署，但本地记录保存失败。请保留当前参数并再次保存。";}
            }
            KeyPassphrase="";
        });
    }
    private void ResetRemote(){Inspection=null;Outcome=null;IsConnected=false;DeploymentConfirmed=false;BackupExisting=false;KeyPassphrase="";_serviceUnit=null;_lastDeployment=null;_needsDeployment=false;_verification=null;ProgressSteps.Clear();}
    private void InvalidateConnection(){IsConnected=false;Inspection=null;DeploymentConfirmed=false;for(var i=0;i<TargetResults.Count;i++)TargetResults[i]=TargetResults[i] with{State=RealityTargetState.Unverified,Reason="尚未从此服务器验证",CheckedAt=null};TargetStatus="目标尚未从 VPS 验证。";}
    partial void OnHostChanged(string value){InvalidateConnection();if(!_loadingDraft)DraftChanged();}
    partial void OnSshPortChanged(string value)=>InvalidateConnection();
    partial void OnUsernameChanged(string value)=>InvalidateConnection();
    partial void OnPrivateKeyPathChanged(string value)=>InvalidateConnection();
    partial void OnKeyPassphraseChanged(string value)=>InvalidateConnection();
    private void DraftChanged(){_needsDeployment=_serviceUnit is not null;if(!_loadingDraft)_verification=null;Inspection=null;DeploymentConfirmed=false;}
    partial void OnSniChanged(string value)=>DraftChanged();
    partial void OnProxyPortChanged(string value){if(!_loadingDraft)_portEdited=true;DraftChanged();}
    partial void OnUuidChanged(string value)=>DraftChanged();
    partial void OnPublicKeyChanged(string value)=>DraftChanged();
    partial void OnShortIdChanged(string value)=>DraftChanged();
    partial void OnHyPasswordChanged(string value)=>DraftChanged();
    partial void OnCertificatePathChanged(string value)=>DraftChanged();
    partial void OnCertificateKeyPathChanged(string value)=>DraftChanged();
    partial void OnLocalOnlyChanged(bool value){InvalidateConnection();OnPropertyChanged(nameof(NextLabel));OnPropertyChanged(nameof(RemoteMode));OnPropertyChanged(nameof(NextAvailable));}
    partial void OnInspectionChanged(RemoteInspection? value){foreach(var name in new[]{nameof(HasInspection),nameof(EnvironmentSummary),nameof(Preview),nameof(RemotePreview),nameof(PreflightStatus),nameof(CanExecuteDeployment),nameof(NextAvailable),nameof(ReviewSummary)})OnPropertyChanged(name);}
    partial void OnIsConnectedChanged(bool value){OnPropertyChanged(nameof(ConnectionStatus));OnPropertyChanged(nameof(ContextStatus));}
    partial void OnIsOperatingChanged(bool value){foreach(var name in new[]{nameof(CanBack),nameof(CanNext),nameof(CanExecuteDeployment),nameof(ConnectionStatus),nameof(NextAvailable),nameof(ContextStatus),nameof(ContextBody)})OnPropertyChanged(name);}
    partial void OnOutcomeChanged(RemoteDeploymentOutcome? value){OnPropertyChanged(nameof(HasOutcome));OnPropertyChanged(nameof(DeploymentSucceeded));OnPropertyChanged(nameof(ShowResultActions));OnPropertyChanged(nameof(ResultVerification));}
    partial void OnDeploymentConfirmedChanged(bool value){OnPropertyChanged(nameof(CanExecuteDeployment));OnPropertyChanged(nameof(NextAvailable));}
    partial void OnBackupExistingChanged(bool value){OnPropertyChanged(nameof(CanExecuteDeployment));OnPropertyChanged(nameof(NextAvailable));}
}
