using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;
using NekoProxy.Infrastructure;
using NekoProxy.Desktop.Onboarding;

namespace NekoProxy.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IServerRepository _repository;
    private readonly ISettingsStore _settingsStore;
    private readonly IServerMonitor _monitor;
    private readonly ISshSessionControl? _sessions;
    [RelayCommand] private async Task DisconnectSshAsync(){Monitoring.Stop();await Network.CancelAndWaitAsync();await Advanced.DisconnectAsync();if(SelectedServer is {} s&&_sessions is not null)await _sessions.DisconnectAsync(s.Id);Status="SSH 会话已断开；下一次操作会重新核验指纹并认证。";}
    private readonly IPlatformActions _platform;
    private readonly IExportService _exportService;
    private readonly CredentialVault? _vault;
    private readonly IServerConfigurationGenerator _configurationGenerator;
    private readonly IRealityKeyGenerator _keys;
    public SettingsViewModel Settings { get; }
    public DeploymentViewModel Deployment { get; }
    public HostTrustViewModel HostTrust {get;}
    public SshSecretViewModel SshSecrets {get;}
    public MonitoringViewModel Monitoring {get;}
    public AdvancedViewModel Advanced {get;}
    public NetworkDiagnosticsViewModel Network {get;}
    public bool DetailNetwork=>DetailIndex==2;
    [RelayCommand] private void OpenNetwork(){Page="管理服务";DetailIndex=2;Network.RefreshConnectionCommand.Execute(null);}
    [RelayCommand] private void OpenTools(){Page="高级功能";}
    [RelayCommand] private void QuickDeploy(){if(SelectedServer is {} server){Deployment.UseServer(server);Page="部署代理";}}
    public OnboardingViewModel Onboarding { get; }
    public bool HasOnboarding => Onboarding.IsOpen;
    public bool CanInteractWithMain => !HasOnboarding && Privacy?.IsBusy != true;
    private AppSettings _settings = new();
    private bool _dataHealthy;
    public ObservableCollection<ServerProfile> Servers { get; } = [];
    public ObservableCollection<ServerProfile> FilteredServers { get; } = [];
    [ObservableProperty] private ServerProfile? selectedServer;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private int detailIndex;
    public bool DetailOverview=>DetailIndex==0;
    public bool DetailMonitor=>DetailIndex==1;
    [RelayCommand] private void SelectDetail(string index){if(int.TryParse(index,out var n))DetailIndex=Math.Clamp(n,0,2);}
    partial void OnDetailIndexChanged(int value){Monitoring.SetVisible(IsManage&&value==1);OnPropertyChanged(nameof(DetailOverview));OnPropertyChanged(nameof(DetailMonitor));OnPropertyChanged(nameof(DetailNetwork));}
    [ObservableProperty] private string page = "管理服务";
    [ObservableProperty] private string status = "正在读取本地资料…";
    [ObservableProperty] private ServerEditorViewModel? editor;
    [ObservableProperty] private bool isRemoving;
    [ObservableProperty] private ServiceSnapshot snapshot = ServiceSnapshot.Disconnected;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private ExportViewModel? exporter;
    [ObservableProperty] private NodeEditorViewModel? nodeEditor;
    private readonly Dictionary<Guid,Guid> _nodeSelection = [];
    private ProxyNode? selectedNode;
    public ProxyNode? SelectedNode
    {
        get => selectedNode;
        set
        {
            // ItemsSource replacement can transiently write null through a
            // selector binding. Resolve by stable ID, never lose a saved choice.
            if (SelectedServer is {} server)
                value = value is not null ? server.Nodes.FirstOrDefault(n=>n.Id==value.Id)
                    : server.Nodes.FirstOrDefault(n=>n.Id==_nodeSelection.GetValueOrDefault(server.Id)) ?? server.Nodes.FirstOrDefault();
            if (!SetProperty(ref selectedNode,value)) return;
            if (SelectedServer is {} current && value is not null) _nodeSelection[current.Id]=value.Id;
            OnSelectedNodeChanged(value);
        }
    }
    public bool HasModal=>HasEditor||HasExporter||HasNodeEditor||IsRemoving||IsExitSaveFailed||HostTrust.IsOpen||SshSecrets.IsOpen;
    partial void OnIsRemovingChanged(bool value)=>OnPropertyChanged(nameof(HasModal));
    partial void OnIsExitSaveFailedChanged(bool value)=>OnPropertyChanged(nameof(HasModal));
    public bool PendingNodeDeployment=>SelectedNode?.NeedsDeployment==true;
    public bool HasNode=>SelectedNode is not null;
    public string NodeVerificationSummary=>SelectedNode is {} node?(node.NeedsDeployment?"参数已修改，原部署与验证记录不代表当前配置。\n":"")+(node.Verification??new NodeVerification()).Summary:"";
    public string NodeClientStatus=>SelectedNode?.Verification?.ClientLabel??"客户端尚未验证";
    public bool HasExporter => Exporter is not null;
    public bool HasNodeEditor => NodeEditor is not null;
    public bool IsManage => Page == "管理服务";
    public bool IsDeploy => Page == "部署代理";
    public bool IsAdvanced => Page == "高级功能";
    public bool IsSettings => Page == "设置";
    public bool IsPrivacy => Page == "隐私";
    public bool IsSettingsSection => IsSettings;
    public int PageIndex => Page switch { "部署代理" => 0, "管理服务" => 1, "高级功能" => 2, "隐私" => 3, _ => 4 };
    public PrivacyViewModel? Privacy { get; }
    public event Action? ExitRequested;
    public event Action? ExitWithoutSavingRequested;
    [ObservableProperty] private bool isExitSaveFailed;
    [RelayCommand] private void CancelExit() => IsExitSaveFailed=false;
    [RelayCommand] private void ExitWithoutSaving() { IsExitSaveFailed=false; ExitWithoutSavingRequested?.Invoke(); }
    public event Action<string>? WindowPresetRequested;
    private Task _monitorPreferenceWrite=Task.CompletedTask;
    public Task FlushPreferencesAsync() => Task.WhenAll(Onboarding.PendingPreferenceWrite,Settings.Updates.PendingPreferenceWrite,_monitorPreferenceWrite);
    private async Task SaveMonitorIntervalAsync(int seconds){try{_settings=await _settingsStore.UpdateAsync(s=>s with{MonitorIntervalSeconds=seconds});}catch{Status="监测频率保存失败，请检查目录权限。";}}
    public bool HasSelection => SelectedServer is not null;
    public bool NoSelection => !HasSelection;
    public bool HasEditor => Editor is not null;
    public bool ShowEmptyList => FilteredServers.Count == 0;
    public bool ShowWelcome => Servers.Count == 0;
    public string ServerCount => $"{Servers.Count} 台服务器";
    public string NodeCount => $"{SelectedServer?.Nodes.Count ?? 0} 个节点";
    public string ExportDirectory => _settings.ExportDirectory;
    public string LastRefresh => Snapshot.RefreshedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未刷新";
    public string CpuDisplay => Snapshot.CpuPercent is { } n ? $"{n:F1}%" : "—";
    public string MemoryDisplay => Snapshot.MemoryBytes is { } n ? $"{n / 1048576.0:F0} MB" : "—";
    public string UploadDisplay => Snapshot.UploadBytesPerSecond is { } n ? $"{n / 1024.0:F1} KB/s" : "—";
    public string DownloadDisplay => Snapshot.DownloadBytesPerSecond is { } n ? $"{n / 1024.0:F1} KB/s" : "—";
    public string UptimeDisplay => Snapshot.Uptime is { } t ? $"{t.Days} 天 {t.Hours} 小时" : "—";
    public MainViewModel(IServerRepository repository, ISettingsStore settingsStore, IServerMonitor monitor, IPlatformActions platform,
        IExportService exportService, IRealityKeyGenerator keyGenerator, IDeploymentService deployment, LocalPrivacyService? privacy = null, CredentialVault? vault = null,IServerConfigurationGenerator? configurationGenerator=null,HostTrustViewModel? hostTrust=null,IMonitoringHistoryStore? history=null,IRemoteAdministration? administration=null,IInteractiveSshFactory? terminalFactory=null,SshSecretViewModel? sshSecrets=null,INetworkDiagnostics? networkDiagnostics=null,ConnectionJournal? journal=null,IRealityTargetService? realityTargets=null,ISshSessionControl? sessionControl=null,IServerSecurityInspector? securityInspector=null)
    {
        _sessions=sessionControl;_repository = repository; _settingsStore = settingsStore; _monitor = monitor; _platform = platform; _exportService = exportService;
        _configurationGenerator=configurationGenerator??new NekoProxy.Deployment.ServerConfigurationGenerator();_keys=keyGenerator;
        SshSecrets=sshSecrets??new();SshSecrets.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(SshSecretViewModel.IsOpen))OnPropertyChanged(nameof(HasModal));};
        Monitoring=new(monitor,history??new MonitoringHistoryStore(Path.Combine(Path.GetTempPath(),"NekoProxy-isolated-history",Guid.NewGuid().ToString("N"))),()=>_settings);
        Advanced=new(administration,terminalFactory,()=>_settings,securityInspector){Feedback=Toast.Show};
        Advanced.ServiceCompleted+=(serverId,nodeId,action,success)=>_=HandleServiceCompletedAsync(serverId,nodeId,action,success);
        Network=new(networkDiagnostics,journal,()=>_settings,platform);
        Monitoring.IntervalChanged+=seconds=>_monitorPreferenceWrite=SaveMonitorIntervalAsync(seconds);
        Monitoring.Sampled+=snap=>{Snapshot=snap;Status=snap.Message;SelectedServer?.Observe(snap);};
        HostTrust=hostTrust??new();HostTrust.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(HostTrustViewModel.IsOpen))OnPropertyChanged(nameof(HasModal));};Settings = new(settingsStore, platform, privacy?.LogDirectory); Deployment = new(keyGenerator, deployment,_configurationGenerator){Feedback=Toast.Show,SaveLocalAsync=SaveDraftAsync,ShowManagement=()=>Page="管理服务",Targets=realityTargets};
        Deployment.LatestConnection=()=>journal?.Latest(Deployment.ConnectionServerId);
        if (privacy is not null) { Privacy = new(privacy, settingsStore, platform, async () => { await ReloadLocalAsync(); }){RemoteOperationBusy=()=>Deployment.IsOperating||Advanced.IsBusy||Advanced.IsConnected||Monitoring.IsSampling||Monitoring.IsPolling||Network.IsBusy}; Privacy.PropertyChanged += (_, e) => { if (e.PropertyName == "IsBusy") OnPropertyChanged(nameof(CanInteractWithMain)); }; }
        Settings.PrivacyRequested += async () => { Page = "隐私"; if (Privacy is not null) await Privacy.RefreshAsync(); };
        Settings.ExitRequested += () => ExitRequested?.Invoke();
        Settings.WindowPresetRequested += value => WindowPresetRequested?.Invoke(value);
        _vault = vault;
        Onboarding = new(settingsStore, new TranslationCatalog(), new MotionPreferences());
        Settings.Saved += next => { _settings = next;Deployment.SshTimeoutSeconds=next.SshTimeoutSeconds; Onboarding.ApplyPreferences(next); OnPropertyChanged(nameof(ExportDirectory)); };
        Settings.RewatchRequested += () => Onboarding.Open(true);
        Settings.LanguageRequested += value => Onboarding.SelectedLanguage = value;
        Onboarding.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Onboarding.SelectedLanguage)) { Localization.LocalizationHub.Current.Change(Onboarding.SelectedLanguage.Code); Settings.SetLanguage(Onboarding.SelectedLanguage);Deployment.RefreshLocalizedText(); } };
        Onboarding.PreferencesSaved += next => { _settings = next; Settings.UpdatePreferences(next); OnPropertyChanged(nameof(ExportDirectory)); };
        Onboarding.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Onboarding.IsOpen)) { OnPropertyChanged(nameof(HasOnboarding)); OnPropertyChanged(nameof(CanInteractWithMain)); } };
    }
    public async Task InitializeAsync()
    {
        try
        {
            _settings = await _settingsStore.LoadAsync();
            Settings.Load(_settings);Settings.Updates.Initialize(_settings);Monitoring.LoadPreferences(_settings);
            Deployment.SshTimeoutSeconds=_settings.SshTimeoutSeconds;
            Onboarding.Initialize(_settings);
            Localization.LocalizationHub.Current.Change(Onboarding.SelectedLanguage.Code);
            var loaded = await _repository.LoadAsync();
            foreach (var server in loaded) Servers.Add(server);
            _dataHealthy = true; RefreshList(); SelectedServer = Servers.FirstOrDefault();
            Status = "本地资料已载入 · 远程服务尚未连接";
            OnPropertyChanged(nameof(ExportDirectory));
        }
        catch { Status = "无法读取本地资料。请检查数据文件；为保护原文件，保存已停用。"; }
    }
    partial void OnPageChanged(string value) {Monitoring.SetVisible(value=="管理服务"&&DetailMonitor);foreach (var n in new[] { nameof(IsManage), nameof(IsDeploy), nameof(IsAdvanced), nameof(IsSettings), nameof(IsPrivacy), nameof(IsSettingsSection), nameof(PageIndex) }) OnPropertyChanged(n); }
    partial void OnSearchChanged(string value) => RefreshList();
    partial void OnEditorChanged(ServerEditorViewModel? value) {WireEditorFeedback(value);OnPropertyChanged(nameof(HasEditor));OnPropertyChanged(nameof(HasModal));}
    partial void OnExporterChanged(ExportViewModel? value) {OnPropertyChanged(nameof(HasExporter));OnPropertyChanged(nameof(HasModal));}
    partial void OnNodeEditorChanged(NodeEditorViewModel? value) {WireNodeFeedback(value);OnPropertyChanged(nameof(HasNodeEditor));OnPropertyChanged(nameof(HasModal));}
    partial void OnSelectedServerChanged(ServerProfile? value)
    {
        Snapshot = ServiceSnapshot.Disconnected;Network.Select(value);
        var nextNode=value?.Nodes.FirstOrDefault(n=>n.Id==_nodeSelection.GetValueOrDefault(value.Id))??value?.Nodes.FirstOrDefault();var same=Equals(SelectedNode,nextNode);SelectedNode=nextNode;
        if(same){_=Monitoring.SelectAsync(value,nextNode,IsManage&&DetailMonitor);Advanced.Select(value,nextNode);}
        foreach (var n in new[] { nameof(HasSelection), nameof(NoSelection), nameof(NodeCount) }) OnPropertyChanged(n);
    }
    private void OnSelectedNodeChanged(ProxyNode? value){OnPropertyChanged(nameof(PendingNodeDeployment));OnPropertyChanged(nameof(HasNode));OnPropertyChanged(nameof(NodeVerificationSummary));OnPropertyChanged(nameof(NodeClientStatus));OnPropertyChanged(nameof(NodeConfigStatus));OnPropertyChanged(nameof(NodeRuntimeStatus));OnPropertyChanged(nameof(NodeClientEvidence));Snapshot=ServiceSnapshot.Disconnected;_=Monitoring.SelectAsync(SelectedServer,value,IsManage&&DetailMonitor);Advanced.Select(SelectedServer,value);}
    partial void OnSnapshotChanged(ServiceSnapshot value)
    { foreach (var n in new[] { nameof(LastRefresh), nameof(CpuDisplay), nameof(MemoryDisplay), nameof(UploadDisplay), nameof(DownloadDisplay), nameof(UptimeDisplay),nameof(NodeRuntimeStatus) }) OnPropertyChanged(n); }
    private void RefreshList()
    {
        var selected = SelectedServer;
        FilteredServers.Clear();
        foreach (var server in Servers.Where(x => (x.Name + x.Host + x.Location + x.ProtocolSummary).Contains(Search, StringComparison.OrdinalIgnoreCase))) FilteredServers.Add(server);
        if (selected is not null && FilteredServers.Contains(selected)) SelectedServer = selected;
        else SelectedServer = FilteredServers.FirstOrDefault();
        OnPropertyChanged(nameof(ServerCount)); OnPropertyChanged(nameof(ShowEmptyList)); OnPropertyChanged(nameof(ShowWelcome));
    }
    [RelayCommand] private void OpenUpdates() {Settings.CategoryIndex=2;Page="设置";}
    [RelayCommand(AllowConcurrentExecutions=true)] private async Task NavigateAsync(string destination) { Page = destination; if (IsPrivacy && Privacy is not null) await Privacy.RefreshAsync(); }
    [RelayCommand] private void AddServer() { Editor = new(); }
    [RelayCommand] private void EditServer() { if (SelectedServer is { } s) Editor = new(s); }
    [RelayCommand] private void RenameServer() { if (SelectedServer is { } s) Editor = new(s, true); }
    [RelayCommand] private void CancelEditor() { Editor?.ClearSecret();Editor = null; }
    [RelayCommand] private async Task SaveServerAsync()
    {
        if (!_dataHealthy || Editor is null) { Status = "本地存储不可用。"; return; }
        var profile = Editor.Build(); if (profile is null) return;
        var next = Servers.ToList(); var index = next.FindIndex(s => s.Id == profile.Id);
        if (index >= 0) next[index] = profile; else next.Add(profile);
        try
        {
            await _repository.SaveAsync(next);
            if(Editor.Original is {} original&&!Editor.RenameOnly&&(original.Host!=profile.Host||original.SshPort!=profile.SshPort||original.Username!=profile.Username||original.Authentication!=profile.Authentication||original.PrivateKeyPath!=profile.PrivateKeyPath))SshSecrets.Forget(original);
            if(!Editor.RenameOnly&&!string.IsNullOrEmpty(Editor.LoginSecret))SshSecrets.SetSessionSecret(profile,Editor.LoginSecret);
            Editor.ClearSecret();
            if (index >= 0) Servers[index] = profile; else Servers.Add(profile);
            Editor = null; Search = ""; RefreshList(); SelectedServer = profile; Notify("服务器资料已保存到本机。");
        }
        catch { if (Editor is { } currentEditor) currentEditor.Error = "保存失败。请检查目录权限和磁盘空间。"; else Status = "保存失败。请检查目录权限和磁盘空间。"; }
    }
    [RelayCommand] private void RequestRemove() { if(Deployment.IsOperating||Advanced.IsBusy){Toast.Show(new("请等待当前远程操作结束，再移除本地服务器资料。",NoticeLevel.Error));return;}if (SelectedServer is not null) IsRemoving = true; }
    [RelayCommand] private void CancelRemove() { IsRemoving = false; }
    [RelayCommand] private async Task ConfirmRemoveAsync()
    {
        if (!_dataHealthy || SelectedServer is null) return;
        var removed = SelectedServer;
        var original = Servers.ToList();
        var remaining = original.Where(s=>s.Id!=removed.Id).ToList();
        var persisted=false;
        try
        {
            Monitoring.Stop();await Monitoring.WaitForIdleAsync();await Network.CancelAndWaitAsync();await Advanced.DisconnectAsync();
            if(_sessions is not null)await _sessions.DisconnectAsync(removed.Id);
            await _repository.SaveAsync(remaining);
            persisted=true;
            try
            {
                if (_vault is not null) await _vault.PruneUnreferencedAsync(remaining.SelectMany(s=>s.Nodes).Select(n=>n.Id));
            }
            catch { await _repository.SaveAsync(original);persisted=false;throw; }
            SshSecrets.Forget(removed); CloseExporter(); NodeEditor?.ClearSecrets(); NodeEditor=null;
            Servers.Remove(removed); RefreshList(); IsRemoving = false;
            Notify("已移除本地服务器资料与无引用的节点凭据。远程服务未作更改。");
        }
        catch { IsRemoving = false;Notify(persisted?"服务器记录已移除，但凭据清理或记录恢复未完成。请检查本地存储；远程服务未更改。":"移除失败，本地资料仍保留。",NoticeLevel.Error); }
    }
    [RelayCommand] private async Task LoadExamplesAsync()
    {
        if (!_dataHealthy || Servers.Count != 0) return;
        var examples = SampleProfiles.Create();
        try { await _repository.SaveAsync(examples); foreach (var s in examples) Servers.Add(s); RefreshList(); SelectedServer = Servers.First(); Status = "已载入示例资料 · 文档专用 IP · 没有真实监控数据"; }
        catch { Status = "示例资料保存失败。"; }
    }
    [RelayCommand] private async Task CopyIpAsync()
    { if (SelectedServer is { } s) { try { await _platform.CopyAsync(s.Host); Notify("公网 IP 已复制。"); } catch { Status = "剪贴板不可用。"; } } }
    [RelayCommand] private async Task RefreshAsync()
    {
        if (SelectedServer is not { } server) return;
        IsBusy = true;
        var node=SelectedNode;
        try { await Monitoring.RefreshAsync();Toast.Show(new(Snapshot.Success?"服务器状态已刷新。":Snapshot.Message,Snapshot.Success?NoticeLevel.Success:NoticeLevel.Error)); }
        catch { Status = "无法刷新服务器状态。请检查连接设置。"; }
        finally { IsBusy = false; }
    }
    [RelayCommand] private async Task OpenExportDirectoryAsync()
    { try { await _platform.OpenDirectoryAsync(ExportDirectory); Notify("已打开导出目录。"); } catch { Status = "无法打开目录，请检查路径与权限。"; } }
    [RelayCommand] private async Task OpenExporterAsync()
    {
        if (SelectedServer is not { } server) return;
        if (server.Nodes.Count == 0) { Status = "请先添加已有代理节点的资料。"; return; }
        Exporter = new(server, ExportDirectory, _exportService, _platform) { SelectedNode = SelectedNode ?? server.Nodes.First(), Vault = _vault, Feedback=Toast.Show };
        Exporter.Policy.Load(_settings.ClientPolicy);
        Exporter.Policy.Save=async p=>{_settings=await _settingsStore.UpdateAsync(s=>s with{ClientPolicy=p});};
        await Exporter.LoadCredentialCommand.ExecuteAsync(null);
    }
    [RelayCommand] private void CloseExporter() { Exporter?.Dispose(); Exporter = null; }
    public Func<Task<string?>>? PickServerFileAsync { get; set; }
    [RelayCommand] private void AddNode() { if (SelectedServer is { } server) NodeEditor = new(server,generator:_configurationGenerator,keys:_keys) { PickServerFileAsync=PickServerFileAsync }; }
    [RelayCommand] private async Task EditNodeAsync()
    {
        if(SelectedServer is not {} server || SelectedNode is not {} node)return;
        var editor=new NodeEditorViewModel(server,node,_configurationGenerator,_keys){PickServerFileAsync=PickServerFileAsync};NodeEditor=editor;
        try { if(_vault is not null) { var secrets=await _vault.GetNodeSecretsAsync(node.Id); if(NodeEditor==editor)editor.ApplySecrets(secrets); } }
        catch (CredentialStorageUnavailableException e) { editor.Error=e.Message; }
        catch { editor.Error=OperatingSystem.IsWindows()?"无法解密认证资料，请使用原 Windows 用户。":"无法解密认证资料，请解锁原系统密钥环或恢复加密备份。"; }
    }
    [RelayCommand] private void CancelNodeEditor() { NodeEditor?.ClearSecrets(); NodeEditor = null; }
    [RelayCommand] private async Task PrepareDeploymentAsync()
    {
        if(Deployment.IsOperating||SelectedServer is not {} server||SelectedNode is not {} node||_vault is null)return;
        try{var secrets=await _vault.GetNodeSecretsAsync(node.Id);Deployment.LoadSavedDraft(new(server,node,secrets));Page="部署代理";}
        catch{Status="无法读取节点凭据，请检查本地安全存储。";}
    }
    [RelayCommand] private async Task SaveNodeAsync()
    {
        if (!_dataHealthy || NodeEditor is not { } editor || editor.Build() is not { } node) return;
        var nodes = editor.Server.Nodes.ToList(); var index = nodes.FindIndex(n => n.Id == node.Id);
        if (index >= 0) nodes[index] = node; else nodes.Add(node);
        var updated = editor.Server with { Nodes = nodes };
        try
        {
            string? previous=null;
            if(!editor.MetadataOnly)
            {
                if(_vault is null)throw new InvalidOperationException();
                previous=await _vault.GetAsync(node.Id); await _vault.SetNodeSecretsAsync(node.Id,editor.BuildSecrets());
            }
            try { await PersistUpdatedServerAsync(updated); }
            catch { if(!editor.MetadataOnly && _vault is not null) { if(previous is null)await _vault.RemoveAsync(node.Id);else await _vault.SetAsync(node.Id,previous); }throw; }
            SelectedNode = node; editor.ClearSecrets(); NodeEditor = null;
            Status = node.ServiceUnit is null?"节点参数已保存到本机。":"本机参数已修改，重新部署后才会生效。";
        }
        catch (CredentialStorageUnavailableException e) { editor.Error = e.Message; }
        catch { editor.Error = "保存节点资料失败。"; }
    }
    private async Task SaveDraftAsync(LocalNodeDraft draft,bool export)
    {
        if(!_dataHealthy||_vault is null)throw new InvalidOperationException();
        var existing=Servers.FirstOrDefault(s=>s.Id==draft.Server.Id);
        var nodes=existing?.Nodes.Where(n=>n.Id!=draft.Node.Id).Append(draft.Node).ToList()??[draft.Node];
        var updated=draft.Server with {Nodes=nodes,Notes=existing?.Notes??draft.Server.Notes,Location=existing?.Location??draft.Server.Location,Ipv6Address=existing?.Ipv6Address??draft.Server.Ipv6Address};
        var next=Servers.ToList();var index=next.FindIndex(s=>s.Id==updated.Id);if(index<0)next.Add(updated);else next[index]=updated;
        var previous=await _vault.GetAsync(draft.Node.Id);await _vault.SetNodeSecretsAsync(draft.Node.Id,draft.Secrets);
        try{await _repository.SaveAsync(next);}
        catch{if(previous is null)await _vault.RemoveAsync(draft.Node.Id);else await _vault.SetAsync(draft.Node.Id,previous);throw;}
        if(index<0)Servers.Add(updated);else Servers[index]=updated;Search="";RefreshList();SelectedServer=updated;SelectedNode=draft.Node;
        Status=draft.Node.LastDeployment is not null&&!draft.Node.NeedsDeployment?"部署记录已保存。":"节点参数已保存到本机。";
        if(updated.Authentication==SshAuthenticationKind.Password&&!string.IsNullOrEmpty(Deployment.KeyPassphrase))SshSecrets.SetSessionSecret(updated,Deployment.KeyPassphrase);
        if(export){Page="管理服务";await OpenExporterAsync();}
    }
    private async Task ReloadLocalAsync()
    {
        Monitoring.Stop();await Network.CancelAndWaitAsync();
        if(_sessions is not null)foreach(var server in Servers.ToArray())await _sessions.DisconnectAsync(server.Id);
        SshSecrets.Clear();
        _settings = await _settingsStore.LoadAsync(); Settings.Load(_settings); Onboarding.ApplyPreferences(_settings);
        Servers.Clear(); foreach (var server in await _repository.LoadAsync()) Servers.Add(server);
        _dataHealthy = true; Search = ""; RefreshList(); OnPropertyChanged(nameof(ExportDirectory));
    }
    private async Task PersistUpdatedServerAsync(ServerProfile updated)
    {
        var next = Servers.ToList(); var index = next.FindIndex(s => s.Id == updated.Id);
        if (index < 0) throw new InvalidOperationException("Server no longer exists");
        next[index] = updated; await _repository.SaveAsync(next); Servers[index] = updated; RefreshList(); SelectedServer = updated;
    }
}
