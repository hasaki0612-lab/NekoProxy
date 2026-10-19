using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using NekoProxy.Core;
using NekoProxy.Deployment;
using NekoProxy.Desktop.Controls;
using NekoProxy.Desktop.ViewModels;
using NekoProxy.Exporters;
using NekoProxy.Infrastructure;
using Xunit;
namespace NekoProxy.Tests;

public sealed class Part2RegressionTests
{
    private static string Temp()=>Path.Combine(Path.GetTempPath(),"NekoProxy-part2",Guid.NewGuid().ToString("N"));
    private static ServerProfile Server(params ProxyNode[] nodes)=>new(Guid.NewGuid(),"local test","192.0.2.1","","",22,"root",null,[..nodes]){Authentication=SshAuthenticationKind.Password};
    private static MainViewModel Create(IServerRepository repo,ISettingsStore settings,CredentialVault vault)=>new(repo,settings,new DisconnectedServerMonitor(),new NoPlatform(),new ExportService([new SingBoxExporter()]),new RealityKeyGenerator(),new PendingDeploymentService(),vault:vault);
    [Fact] public void ProtectedFieldsExposeLabelAndFocusWithoutValueOrTextChildren()
    {
        var box=new SecretTextBox{Text="test-only-not-a-real-secret"};AutomationProperties.SetName(box,"SSH password");
        var peer=ControlAutomationPeer.CreatePeerForElement(box)!;
        Assert.Equal('●',box.PasswordChar);Assert.Null(peer.GetProvider<IValueProvider>());
        Assert.Empty(peer.GetChildren()??[]);Assert.Equal("SSH password",peer.GetName());
        box.Text="changed-test-only";Assert.Null(peer.GetProvider<IValueProvider>());
    }
    [Fact] public async Task RemovingServerDropsOnlyItsUnreferencedSecretsAndNoRemoteAction()
    {
        var dir=Temp();var repo=new JsonServerRepository(dir);var shared=new ProxyNode(Guid.NewGuid(),"shared",ProxyProtocol.TrojanTls);
        var removed=new ProxyNode(Guid.NewGuid(),"remove",ProxyProtocol.Hysteria2);var keep=new ProxyNode(Guid.NewGuid(),"keep",ProxyProtocol.Shadowsocks2022);
        var a=Server(removed,shared);var b=Server(keep,shared);await repo.SaveAsync([a,b]);
        var vault=new CredentialVault(dir,TestCredentialStore.Create(dir));foreach(var n in new[]{removed,shared,keep})await vault.SetAsync(n.Id,"test-only-"+n.Id);
        var vm=Create(repo,new JsonSettingsStore(dir),vault);await vm.InitializeAsync();vm.SelectedServer=a;
        await vm.ConfirmRemoveCommand.ExecuteAsync(null);
        Assert.Null(await vault.GetAsync(removed.Id));Assert.NotNull(await vault.GetAsync(shared.Id));Assert.NotNull(await vault.GetAsync(keep.Id));
        Assert.Equal(b.Id,Assert.Single(await repo.LoadAsync()).Id);Assert.Contains("远程服务未作更改",vm.Status);
        vm.Toast.Dispose();vm.Settings.Updates.Dispose();await vm.Monitoring.DisposeAsync();
    }
    [Fact] public async Task BackupsExcludeOrphanSecretsAndRestoreCannotReintroduceThem()
    {
        var dir=Temp();var repo=new JsonServerRepository(dir);var node=new ProxyNode(Guid.NewGuid(),"keep",ProxyProtocol.Hysteria2);await repo.SaveAsync([Server(node)]);
        var settings=new JsonSettingsStore(dir);var vault=new CredentialVault(dir,TestCredentialStore.Create(dir));var orphan=Guid.NewGuid();
        await vault.SetAsync(node.Id,"keep-test-only");await vault.SetAsync(orphan,"orphan-test-only");
        var path=Path.Combine(dir,"test.nekobak");var privacy=new LocalPrivacyService(dir,settings,repo,vault);await privacy.BackupAsync(path,"part2-test-password");
        var payload=BackupCrypto.Decrypt(await File.ReadAllBytesAsync(path),"part2-test-password");Assert.Single(payload.Credentials);Assert.False(payload.Credentials.ContainsKey(orphan.ToString()));
        await privacy.RestoreAsync(path,"part2-test-password",true);Assert.Null(await vault.GetAsync(orphan));Assert.Equal("keep-test-only",await vault.GetAsync(node.Id));
    }
    [Fact] public async Task EditingAndTransientSelectorNullPreserveNodeById()
    {
        var dir=Temp();var repo=new JsonServerRepository(dir);var first=new ProxyNode(Guid.NewGuid(),"first",ProxyProtocol.Hysteria2);var second=new ProxyNode(Guid.NewGuid(),"second",ProxyProtocol.TrojanTls);
        await repo.SaveAsync([Server(first,second)]);var vm=Create(repo,new JsonSettingsStore(dir),new(dir,TestCredentialStore.Create(dir)));await vm.InitializeAsync();vm.SelectedNode=second;
        vm.EditServerCommand.Execute(null);vm.Editor!.Name="edited";await vm.SaveServerCommand.ExecuteAsync(null);
        Assert.Equal(second.Id,vm.SelectedNode?.Id);vm.SelectedNode=null;Assert.Equal(second.Id,vm.SelectedNode?.Id);
        vm.Toast.Dispose();vm.Settings.Updates.Dispose();await vm.Monitoring.DisposeAsync();
    }
    [Fact] public async Task MonitorStartsOnEntryAndStopsWithoutLateValuesOrBackgroundPolling()
    {
        var monitor=new CounterMonitor();var vm=new MonitoringViewModel(monitor,new MemoryHistory(),()=>new());
        await vm.SelectAsync(Server(),null,false);Assert.Equal(0,monitor.Calls);Assert.Equal(5,vm.Intervals[vm.IntervalIndex].Value);
        vm.SetVisible(true);await monitor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));Assert.True(vm.IsPolling);
        vm.SetVisible(false);await vm.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(2));Assert.False(vm.IsPolling);Assert.Single(vm.Samples);
        var count=monitor.Calls;await Task.Delay(80);Assert.Equal(count,monitor.Calls);Assert.Contains("暂停",vm.Message);await vm.DisposeAsync();
    }
    [Fact] public void StaticChecksDoNotPretendToHaveStatisticalSuccessRates()
    {
        var row=new NetworkProbeResult(new("vps","VPS","",""),ProbeDirection.ServerConfiguration,"IPv4","192.0.2.1",443,"ss",ProbeState.NotListening,0,0,null,null,DateTimeOffset.UtcNow,"read-only ss");
        Assert.Contains("静态检查",row.Timing);Assert.Contains("不计算成功率",row.Timing);Assert.DoesNotContain("—",row.Timing);
        var measured=row with{Attempts=3,Successes=2,MeanMilliseconds=12,P95Milliseconds=18,State=ProbeState.Partial};
        Assert.Contains("67%",measured.Timing);Assert.Contains("3 次",measured.Timing);Assert.Contains("P95 18",measured.Timing);
        Assert.Contains("不支持",(row with{State=ProbeState.Unsupported}).StateLabel);Assert.Contains("尚未检测",(row with{State=ProbeState.NotTested}).StateLabel);
    }
    [Theory]
    [InlineData("请输入有效的 IPv4、IPv6 或服务器域名。","Host")]
    [InlineData("请输入有效的服务器 IPv6 地址。","Ipv6Address")]
    [InlineData("SSH 端口必须是整数。","SshPort")]
    [InlineData("请先连接服务器并检查环境。","Connect")]
    [InlineData("请先勾选确认部署内容，再执行部署。","DeploymentConfirmed")]
    public void ValidationTargetsTheRelevantField(string message,string field)=>Assert.Equal(field,FormAssist.ErrorField(message));
    [Fact] public void SuccessfulPreflightDoesNotTriggerErrorScrolling()=>Assert.Null(FormAssist.ErrorField("检查已完成，请确认部署内容。"));
    [Fact] public void SavedTlsDraftUpdatesPreparedLabelAfterCredentialIsLoaded()
    {
        var node=new ProxyNode(Guid.NewGuid(),"saved",ProxyProtocol.TrojanTls,9443);var vm=new DeploymentViewModel(new RealityKeyGenerator(),new PendingDeploymentService());var labels=new List<string>();
        vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(vm.PreparedStatus))labels.Add(vm.PreparedStatus);};
        vm.LoadSavedDraft(new(Server(node),node,new("test-only-existing-password",null)));
        Assert.Equal("test-only-existing-password",vm.HyPassword);Assert.Contains("已自动准备",vm.PreparedStatus);Assert.Equal(vm.PreparedStatus,labels.Last());
    }
    [Fact] public void CubicCurveDoesNotInventPeaksAndKeepsOriginalHoverSamples()
    {
        var now=DateTimeOffset.UtcNow;var values=new double[]{1,80,5,5,90};
        var rows=values.Select((v,i)=>new MonitoringSample(now.AddSeconds(i*5),ServiceSnapshot.Disconnected with{Success=true,CpuPercent=v},5)).ToArray();
        var points=ChartSamples.Segments(rows,MonitorMetric.Cpu).Single();var tangents=ChartSamples.Tangents(points);
        for(var i=1;i<points.Count;i++)for(var j=0;j<=20;j++)
        {
            var t=j/20d;var a=points[i-1].Value;var b=points[i].Value;var c=a+tangents[i-1]*5000/3;var d=b-tangents[i]*5000/3;
            var value=Math.Pow(1-t,3)*a+3*Math.Pow(1-t,2)*t*c+3*(1-t)*t*t*d+t*t*t*b;
            Assert.InRange(value,Math.Min(a,b)-1e-9,Math.Max(a,b)+1e-9);
        }
        Assert.Same(rows[1],ChartSamples.Nearest(rows,rows[1].Timestamp,MonitorMetric.Cpu));
    }
    [Fact] public void PlotBackgroundIsInteractiveBetweenPaintedLines()
    {
        var chart=new TimeSeriesChart();chart.Arrange(new Avalonia.Rect(0,0,600,270));
        Assert.True(chart.HitTest(new(300,70)));Assert.False(chart.HitTest(new(1,1)));
    }
    [Fact] public async Task ServiceCancelDoesNotExecuteAndConfirmRunsExactlyOnceWithNodeContext()
    {
        var admin=new CounterAdmin();var vm=new AdvancedViewModel(admin,null,()=>new());var n=new ProxyNode(Guid.NewGuid(),"node",ProxyProtocol.TrojanTls,9443);vm.Select(Server(n),n);
        vm.RequestServiceCommand.Execute("Stop");Assert.True(vm.HasConfirmation);Assert.Contains("9443",vm.ConfirmationText);Assert.Equal(0,admin.Calls);
        vm.CancelConfirmationCommand.Execute(null);await vm.ConfirmCommand.ExecuteAsync(null);Assert.Equal(0,admin.Calls);
        vm.RequestServiceCommand.Execute("Restart");await vm.ConfirmCommand.ExecuteAsync(null);await vm.ConfirmCommand.ExecuteAsync(null);Assert.Equal(1,admin.Calls);await vm.DisposeAsync();
    }
    [Fact] public async Task ToastReversalAndDismissCannotBeClosedByAnOldExpiry()
    {
        using var toast=new ToastViewModel();toast.Show(new("failure",NoticeLevel.Error));Assert.True(toast.IsError);
        toast.Show(new("success",NoticeLevel.Success));Assert.False(toast.IsError);Assert.True(toast.IsOpen);toast.DismissCommand.Execute(null);
        await Task.Delay(30);Assert.False(toast.IsOpen);Assert.Equal("success",toast.Message);
    }
    [Fact] public async Task SwitchingNodeCancelsPendingConfirmationAndEnvironmentNeedsOnlyServer()
    {
        var admin=new CounterAdmin();var vm=new AdvancedViewModel(admin,null,()=>new());var a=new ProxyNode(Guid.NewGuid(),"a",ProxyProtocol.TrojanTls);var b=new ProxyNode(Guid.NewGuid(),"b",ProxyProtocol.Hysteria2);var server=Server(a,b);
        vm.Select(server,a);vm.RequestServiceCommand.Execute("Stop");Assert.Equal("确认停止",vm.ConfirmButtonLabel);
        vm.Select(server,b);await vm.ConfirmCommand.ExecuteAsync(null);Assert.Equal(0,admin.Calls);Assert.False(vm.HasConfirmation);
        vm.Select(server,null);vm.RequestServiceCommand.Execute("Environment");Assert.Equal(1,admin.Calls);await vm.DisposeAsync();
    }
    [LocalSshFact] public async Task LocalFixtureHandshakeProducesSafeStageEvidence()
    {
        using var info=System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("NEKOPROXY_SSH_FIXTURE")!));var i=info.RootElement;
        var s=Server() with{Host=i.GetProperty("host").GetString()!,SshPort=i.GetProperty("port").GetInt32(),Username=i.GetProperty("username").GetString()!,Authentication=SshAuthenticationKind.PrivateKey,PrivateKeyPath=i.GetProperty("keyPath").GetString()};
        var factory=new SshConnectionFactory(i.GetProperty("fingerprint").GetString()!);
        try{await using var session=await factory.ConnectAsync(s,TimeSpan.FromSeconds(3));Assert.Equal("NEKOPROXY-SSH-TEST",await session.ExecuteAsync("printf 'NEKOPROXY-SSH-TEST'"));}
        catch(SshConnectionException e)
        {
            // Local fixture only: inspect the library exception before product
            // redaction to distinguish a fixture algorithm mismatch from KEX.
            using var key=new Renci.SshNet.PrivateKeyFile(s.PrivateKeyPath!);
            using var direct=new Renci.SshNet.SshClient(s.Host,s.SshPort,s.Username,key);
            direct.HostKeyReceived+=(_,a)=>a.CanTrust=("SHA256:"+Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(a.HostKey)).TrimEnd('='))==i.GetProperty("fingerprint").GetString();
            string detail="";try{await direct.ConnectAsync(CancellationToken.None);}catch(Exception error){detail=error.GetType().Name+": "+error.Message;}
            throw new Xunit.Sdk.XunitException(string.Join("\n",e.Attempt?.Records.Select(r=>r.Display+" · "+r.ErrorType)??[])+"\nLocal fixture key algorithms: "+string.Join(",",key.HostKeyAlgorithms.Select(a=>a.Name))+"\nLocal fixture library detail: "+detail);
        }
    }
    private sealed class NoPlatform:IPlatformActions{public Task CopyAsync(string text)=>Task.CompletedTask;public Task OpenDirectoryAsync(string directory)=>Task.CompletedTask;}
    private sealed class MemoryHistory:IMonitoringHistoryStore{public Task<IReadOnlyList<MonitoringSample>> LoadAsync(Guid id,CancellationToken token=default)=>Task.FromResult<IReadOnlyList<MonitoringSample>>([]);public Task AppendAsync(Guid id,MonitoringSample sample,int retentionHours=24,CancellationToken token=default)=>Task.CompletedTask;}
    private sealed class CounterMonitor:IServerMonitor
    {
        public int Calls;public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ServiceSnapshot> RefreshAsync(ServerProfile server,AppSettings settings,CancellationToken cancellationToken=default){Calls++;Started.TrySetResult();return Task.FromResult(ServiceSnapshot.Disconnected with{Success=true,RefreshedAt=DateTimeOffset.UtcNow,CpuPercent=1});}
    }
    private sealed class CounterAdmin:IRemoteAdministration
    {
        public int Calls;
        public Task<RemoteToolResult> RunAsync(ServerProfile s,ProxyNode? n,RemoteServiceAction a,TimeSpan t,CancellationToken token=default){Calls++;return Task.FromResult(new RemoteToolResult(true,"test-only result","","fixture"));}
        public Task<RemoteToolResult> TestTcpAsync(ServerProfile s,int p,CancellationToken token=default)=>throw new NotSupportedException();
        public Task<RemoteToolResult> TestSshAsync(ServerProfile s,TimeSpan t,CancellationToken token=default)=>throw new NotSupportedException();
    }
}
