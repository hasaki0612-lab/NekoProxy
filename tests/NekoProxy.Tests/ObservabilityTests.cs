using System.Text.Json;
using NekoProxy.Core;
using NekoProxy.Deployment;
using NekoProxy.Desktop.ViewModels;
using NekoProxy.Infrastructure;
using Xunit;
namespace NekoProxy.Tests;

public sealed class ObservabilityTests
{
    private static ServerProfile Server()=>new(Guid.NewGuid(),"test","203.0.113.10","","",22,"root",null,[]){Authentication=SshAuthenticationKind.Password};
    private static ClientNodeConfiguration Client(ProxyProtocol p)=>new("test","node","203.0.113.10",443,p,"test-credential","example.com");
    private static ServiceSnapshot Snapshot(double cpu=20)=>ServiceSnapshot.Disconnected with{Success=true,CpuPercent=cpu,MemoryPercent=42,RefreshedAt=DateTimeOffset.UtcNow};
    private static string Temp(){var path=Path.Combine(Path.GetTempPath(),"NekoProxy-v05-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}

    [Theory][InlineData(ProxyProtocol.TrojanTls,CertificateChallenge.Http01)][InlineData(ProxyProtocol.Hysteria2,CertificateChallenge.TlsAlpn01)]
    public void AutomaticCertificatesGenerateExplicitChallengeAndSafePersistentStorage(ProxyProtocol protocol,CertificateChallenge challenge)
    {
        var options=new AutomaticCertificateOptions("",challenge,true,true);
        using var json=JsonDocument.Parse(new ServerConfigurationGenerator().Generate(new(Client(protocol),AutomaticCertificate:options)));
        var tls=json.RootElement.GetProperty("inbounds")[0].GetProperty("tls");var acme=tls.GetProperty("certificate_provider");
        Assert.Equal("acme",acme.GetProperty("type").GetString());Assert.Contains("staging",acme.GetProperty("provider").GetString());
        Assert.Equal(challenge!=CertificateChallenge.Http01,acme.GetProperty("disable_http_challenge").GetBoolean());
        Assert.Equal(challenge!=CertificateChallenge.TlsAlpn01,acme.GetProperty("disable_tls_alpn_challenge").GetBoolean());
        Assert.Equal("/etc/nekoproxy/certificates/example.com",acme.GetProperty("data_directory").GetString());
        Assert.False(tls.TryGetProperty("insecure",out _));Assert.False(tls.TryGetProperty("key_path",out _));
    }
    [Fact]public void AutomaticCertificateAuthorizationAndMutuallyExclusiveModesAreEnforced()
    {
        var generator=new ServerConfigurationGenerator();var c=Client(ProxyProtocol.TrojanTls);
        Assert.Throws<ArgumentException>(()=>generator.Generate(new(c,AutomaticCertificate:new(""))));
        Assert.Throws<ArgumentException>(()=>generator.Generate(new(c,CertificatePath:"/cert.pem",CertificateKeyPath:"/key.pem",AutomaticCertificate:new("",DomainAuthorized:true))));
        Assert.Throws<ArgumentException>(()=>CertificatePolicy.Validate(c with{Protocol=ProxyProtocol.Shadowsocks2022},new("",DomainAuthorized:true)));
        Assert.Throws<ArgumentException>(()=>CertificatePolicy.Storage("../../certs"));
        foreach(var invalid in new[]{"-bad.example.com","bad-.example.com","127.0.0.1",new string('a',64)+".com"})Assert.Throws<ArgumentException>(()=>CertificatePolicy.Storage(invalid));
        Assert.Throws<ArgumentException>(()=>CertificatePolicy.Validate(c,new("wrong-email",DomainAuthorized:true)));
    }
    [Fact]public void PasswordAndAutomaticCertificateDraftsSurviveEditingWithoutPersistingSshPassword()
    {
        var d=new DeploymentViewModel(new RealityKeyGenerator(),new PendingDeploymentService()){Host="203.0.113.10",ProtocolIndex=2,AuthenticationIndex=1,KeyPassphrase="transient-ssh-password",Sni="example.com",HyPassword="node-auth",AutomaticCertificate=true,DomainAuthorized=true,AcmeStaging=false};
        var draft=d.BuildLocalDraft();Assert.Equal(SshAuthenticationKind.Password,draft.Server.Authentication);Assert.Null(draft.Server.PrivateKeyPath);
        var text=JsonSerializer.Serialize(draft.Server);Assert.DoesNotContain("transient-ssh-password",text);Assert.DoesNotContain("node-auth",text);
        var editor=new NodeEditorViewModel(draft.Server,draft.Node);editor.ApplySecrets(draft.Secrets);var saved=editor.Build();Assert.NotNull(saved);Assert.Equal(draft.Node.AutomaticCertificate,saved.AutomaticCertificate);
        d.LoadSavedDraft(new(draft.Server,saved,draft.Secrets));Assert.True(d.IsPassword);Assert.True(d.AutomaticCertificate);Assert.False(d.AcmeStaging);Assert.Empty(d.KeyPassphrase);Assert.Equal(draft.Node.Id,d.BuildLocalDraft().Node.Id);
        var serverEdit=new ServerEditorViewModel(draft.Server,true);Assert.Equal(SshAuthenticationKind.Password,serverEdit.Build()!.Authentication);
    }
    [Fact]public void CurveGapsNeverBridgeOrProduceInventedZeroesAndHoverUsesOriginalSamples()
    {
        var t=DateTimeOffset.UtcNow;var rows=new[]{new MonitoringSample(t,Snapshot(1),10),new MonitoringSample(t.AddSeconds(10),Snapshot(90),10),new MonitoringSample(t.AddSeconds(20),ServiceSnapshot.Disconnected,10),new MonitoringSample(t.AddSeconds(60),Snapshot(2),10)};
        var segments=ChartSamples.Segments(rows,MonitorMetric.Cpu);Assert.Equal(2,segments.Count);Assert.Equal(2,segments[0].Count);Assert.Single(segments[1]);
        Assert.Null(ChartSamples.Nearest(rows,t.AddSeconds(30),MonitorMetric.Cpu));Assert.Same(rows[1],ChartSamples.Nearest(rows,t.AddSeconds(9),MonitorMetric.Cpu));
        Assert.Null(ChartSamples.Nearest(rows,t.AddSeconds(-1),MonitorMetric.Cpu));Assert.Null(ChartSamples.Value(rows[2],MonitorMetric.Memory));Assert.Null(ChartSamples.Nearest(rows,t.AddMinutes(3),MonitorMetric.Cpu));
    }
    [Fact]public void DownsamplingPreservesExtremaAndImmutableRawValues()
    {
        var time=DateTimeOffset.UtcNow;var rows=Enumerable.Range(0,1000).Select(i=>new MonitoringSample(time.AddMilliseconds(i),Snapshot(i==501?99:i==502?0:25),10)).ToArray();
        var points=ChartSamples.Segments(rows,MonitorMetric.Cpu).Single();var reduced=ChartSamples.Reduce(points,time,time.AddSeconds(1),20);
        Assert.Contains(reduced,p=>p.Value==99);Assert.Contains(reduced,p=>p.Value==0);Assert.True(reduced.Count<=80);Assert.Equal(99,rows[501].Snapshot.CpuPercent);
    }
    [Fact]public async Task LocalHistoryKeepsGapsAndPrunesOldRowsWithoutWritingFreeFormMessages()
    {
        var dir=Temp();var id=Guid.NewGuid();var store=new MonitoringHistoryStore(dir);var now=DateTimeOffset.UtcNow;
        await store.AppendAsync(id,new(now.AddHours(-25),Snapshot()));await store.AppendAsync(id,new(now,Snapshot() with{Message="secret-in-error"}));await store.AppendAsync(id,new(now.AddSeconds(1),ServiceSnapshot.Disconnected));
        var rows=await store.LoadAsync(id);Assert.Equal(2,rows.Count);Assert.False(rows[1].Snapshot.Success);Assert.DoesNotContain("secret-in-error",await File.ReadAllTextAsync(Path.Combine(dir,"history",id.ToString("N")+".json")));
        var path=Path.Combine(dir,"history",id.ToString("N")+".json");await File.WriteAllTextAsync(path,"corrupt-but-preserve-me");await Assert.ThrowsAsync<JsonException>(()=>store.AppendAsync(id,new(now,Snapshot())));Assert.Equal("corrupt-but-preserve-me",await File.ReadAllTextAsync(path));
    }
    [Fact]public async Task LeavingPageCancelsManualSamplingWithoutPublishingLateValues()
    {
        var source=new BlockingMonitor();var vm=new MonitoringViewModel(source,new MemoryHistory(),()=>new());await vm.SelectAsync(Server(),null,true);
        var sample=vm.RefreshAsync();await source.Started.Task;vm.SetVisible(false);await sample.WaitAsync(TimeSpan.FromSeconds(2));
        await vm.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(2)); // entry now also starts the automatic sample
        Assert.Empty(vm.Samples);Assert.False(vm.IsSampling);Assert.False(vm.Snapshot.Success);await vm.DisposeAsync();
    }
    [Fact]public async Task MultipleMonitorsLimitConcurrentSshSessionsAndDisposeCleanly()
    {
        var source=new BlockingMonitor();var vms=Enumerable.Range(0,4).Select(_=>new MonitoringViewModel(source,new MemoryHistory(),()=>new())).ToArray();
        foreach(var vm in vms)await vm.SelectAsync(Server(),null,true);
        var tasks=vms.Select(v=>v.RefreshAsync()).ToArray();await source.TwoStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));Assert.Equal(2,source.Active);
        foreach(var vm in vms)vm.Stop();await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2));foreach(var vm in vms)await vm.DisposeAsync();Assert.Equal(0,source.Active);
    }
    [Fact]public void TerminalProcessesFragmentedAnsiCarriageReturnBackspaceAndBoundedOutput()
    {
        var buffer=new TerminalBuffer();buffer.Append("loading 10%\rloaded\u001b[");buffer.Append("K\r\nabc\b \bd\r\n\u001b]0;ignored title");buffer.Append("\a中文");
        Assert.Equal("loaded\nab d\n中文".Replace("ab d","abd"),buffer.Text);Assert.DoesNotContain("ignored",buffer.Text);
        buffer.Append(new string('x',100000));Assert.True(buffer.Text.Length<=64000);
    }
    [Fact]public void RenewalSummaryReflectsActualEventsInsteadOfClaimingRenewalAfterFirstIssuance()
    {
        Assert.Contains("等待续期",CertificateInspector.RenewalSummary("2026-10-14T11:00:00+00:00 obtain: certificate obtained successfully"));
        Assert.Contains("失败",CertificateInspector.RenewalSummary("2026-10-14T12:00:00+00:00 ERROR renewal failed secret=hidden"));
        Assert.DoesNotContain("hidden",CertificateInspector.RenewalSummary("2026-10-14T12:00:00+00:00 ERROR renewal failed secret=hidden"));
        Assert.Contains("尚未",CertificateInspector.RenewalSummary(""));
    }
    [Fact]public void MemoryPressureIsMeasuredAsFullUsageWhenAvailableMemoryIsZero()
    {
        var c=SshServerMonitor.Parse("MemTotal: 100 kB\nMemAvailable: 0 kB\n");Assert.Equal(102400,c.Memory);Assert.Equal(102400,c.MemoryTotal);
    }
    [Theory][InlineData("Permission")][InlineData("Ownership")][InlineData("Configuration")][InlineData("Service")][InlineData("Authentication")][InlineData("HostChanged")]
    public async Task RemoteAdministrationKeepsFailureCategoriesDistinct(string kind)
    {
        var server=Server();var id=Guid.NewGuid();var node=new ProxyNode(id,"test",ProxyProtocol.TrojanTls,443){ServiceUnit=new RemotePaths(id).Unit};
        var result=await new RemoteAdministration(new FailureFactory(kind)).RunAsync(server,node,RemoteServiceAction.CheckConfiguration,TimeSpan.FromSeconds(2));
        Assert.False(result.Success);Assert.Equal(kind,result.Kind);
    }
    [Fact]public async Task LoadingMonitorPreferencesDoesNotWriteAndManualChoicePersists()
    {
        var vm=new MonitoringViewModel(new BlockingMonitor(),new MemoryHistory(),()=>new());var events=new List<int>();vm.IntervalChanged+=events.Add;
        vm.LoadPreferences(new(){MonitorIntervalSeconds=60});Assert.Equal(3,vm.IntervalIndex);Assert.Empty(events);vm.IntervalIndex=4;Assert.Equal(new[]{0},events);await vm.DisposeAsync();
    }
    private sealed class FailureFactory(string kind):ISshConnectionFactory
    {
        public Task<ISshSession> ConnectAsync(ServerProfile server,TimeSpan timeout,CancellationToken token=default)
        {
            if(kind is "Authentication" or "HostChanged")throw new SshConnectionException(Enum.Parse<SshFailureKind>(kind),"connection rejected");
            return Task.FromResult<ISshSession>(new FailureSession(kind));
        }
    }
    private sealed class FailureSession(string kind):ISshSession
    {
        public Task<string> ExecuteAsync(string command,CancellationToken token=default)=>Task.FromResult(command.Contains("PERMISSION=1")?(kind=="Permission"?"PERMISSION=0":"PERMISSION=1"):"NEKOPROXY_FAILURE="+kind);
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class MemoryHistory:IMonitoringHistoryStore
    {
        public Task<IReadOnlyList<MonitoringSample>> LoadAsync(Guid id,CancellationToken token=default)=>Task.FromResult<IReadOnlyList<MonitoringSample>>([]);
        public Task AppendAsync(Guid id,MonitoringSample row,int retentionHours=24,CancellationToken token=default)=>Task.CompletedTask;
    }
    private sealed class BlockingMonitor:IServerMonitor
    {
        public int Active;public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),TwoStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ServiceSnapshot> RefreshAsync(ServerProfile server,AppSettings settings,CancellationToken token=default)
        {var n=Interlocked.Increment(ref Active);Started.TrySetResult();if(n==2)TwoStarted.TrySetResult();try{await Task.Delay(Timeout.Infinite,token);return Snapshot();}finally{Interlocked.Decrement(ref Active);}}
    }
}
