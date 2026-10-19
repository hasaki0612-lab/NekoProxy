using System.Net;
using System.Net.Sockets;
using NekoProxy.Core;
using NekoProxy.Deployment;
using NekoProxy.Desktop.ViewModels;
using NekoProxy.Infrastructure;
using Xunit;
namespace NekoProxy.Tests;
public sealed class UsabilityDiagnosticsTests
{
    [Fact] public void NewDraftAutomaticallyPreparesIndependentStableProtocolCredentials()
    {
        var a=new DeploymentViewModel(new RealityKeyGenerator(),new PendingDeploymentService());var b=new DeploymentViewModel(new RealityKeyGenerator(),new PendingDeploymentService());
        Assert.True(Guid.TryParse(a.Uuid,out _));Assert.Equal(16,a.ShortId.Length);Assert.NotEqual(a.Uuid,b.Uuid);var uuid=a.Uuid;var publicKey=a.PublicKey;
        a.ProtocolIndex=1;var hy=a.HyPassword;a.ProtocolIndex=2;Assert.NotEqual(hy,a.HyPassword);a.ProtocolIndex=1;Assert.Equal(hy,a.HyPassword);
        a.ProtocolIndex=3;var ss=a.HyPassword;Assert.Equal(16,Convert.FromBase64String(ss).Length);a.Method=ShadowsocksKeys.Methods[1];Assert.Equal(32,Convert.FromBase64String(a.HyPassword).Length);a.Method=ShadowsocksKeys.DefaultMethod;Assert.Equal(ss,a.HyPassword);
        a.ProtocolIndex=0;Assert.Equal(uuid,a.Uuid);Assert.Equal(publicKey,a.PublicKey);a.BackCommand.Execute(null);Assert.Equal(uuid,a.Uuid);a.ResetCommand.Execute(null);Assert.NotEqual(uuid,a.Uuid);Assert.Equal(2,a.Step);
    }
    [Fact] public void ExistingNodeDoesNotRotateUntilExplicitAcknowledgement()
    {
        var k=new RealityKeyGenerator().Generate();var node=new ProxyNode(Guid.NewGuid(),"saved",ProxyProtocol.VlessReality){Sni="www.microsoft.com",ParametersConfigured=true,RealityPublicKey=k.PublicKey,ShortId=k.ShortId,ServiceUnit="owned"};
        var server=new ServerProfile(Guid.NewGuid(),"saved","127.0.0.1","","",22,"root",null,[node]);var a=new DeploymentViewModel(new RealityKeyGenerator(),new PendingDeploymentService());
        a.LoadSavedDraft(new(server,node,new(k.Uuid,k.PrivateKey)));Assert.Equal(k.Uuid,a.Uuid);Assert.Equal(k.PublicKey,a.PublicKey);
        a.GenerateCredentialCommand.Execute(null);Assert.True(a.RotationRequested);Assert.Equal(k.Uuid,a.Uuid);a.CancelRotationCommand.Execute(null);Assert.Equal(k.Uuid,a.Uuid);a.GenerateCredentialCommand.Execute(null);a.ConfirmRotationCommand.Execute(null);Assert.NotEqual(k.Uuid,a.Uuid);
    }
    [Fact] public void CandidateClickImmediatelyFillsTargetWithoutClaimingValidation()
    {var a=new DeploymentViewModel(new RealityKeyGenerator(),new PendingDeploymentService());a.ChooseTargetCommand.Execute("www.apple.com");Assert.Equal("www.apple.com",a.Sni);Assert.Contains("需要",a.TargetStatus);Assert.All(a.TargetResults,x=>Assert.Equal(RealityTargetState.Unverified,x.State));}
    [Fact] public void MeanAndP95UseOnlyFiniteMeasuredSamples()
    {var stats=ProbeStatistics.Summarize([1,3,10,double.NaN,-1,double.PositiveInfinity]);Assert.Equal(14d/3,stats.Mean);Assert.Equal(10,stats.P95);Assert.Null(ProbeStatistics.Summarize([]).Mean);}
    [Theory][InlineData("198.18.0.18",true)][InlineData("198.19.255.254",true)][InlineData("203.0.113.24",false)]
    public void SyntheticDnsAddressesRequireIndependentEvidence(string ip,bool synthetic)
    {Assert.Equal(synthetic,NetworkDiagnostics.IsSyntheticDnsAddress(IPAddress.Parse(ip)));}
    [Fact] public void OutboundSuccessOrSingleTimeoutCannotProveInboundPathRestriction()
    {
        var source=new ProbeSource("local","local","unknown","unknown");var row=new NetworkProbeResult(source,ProbeDirection.Inbound,"IPv4","127.0.0.1",22,"TCP",ProbeState.Timeout,3,0,null,null,DateTimeOffset.UtcNow,"controlled fixture");
        Assert.False(ProbeStatistics.HasPathRestrictionEvidence([row,row with{Direction=ProbeDirection.Outbound,State=ProbeState.Reachable,Successes=3}]));
        Assert.False(ProbeStatistics.HasPathRestrictionEvidence([row,row with{State=ProbeState.Reachable,Successes=3}]));
        Assert.True(ProbeStatistics.HasPathRestrictionEvidence([row,row with{Source=source with{Id="authorized-independent"},State=ProbeState.Reachable,Successes=3}]));
    }
    [Fact] public async Task ClosedLocalPortHasTcpRefusalAndDoesNotInventUdpOrIpv6()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        var server=new ServerProfile(Guid.NewGuid(),"isolated","127.0.0.1","","",port,"test",null,[new(Guid.NewGuid(),"UDP",ProxyProtocol.Hysteria2,port)]);
        var report=await new NetworkDiagnostics(new Refused()).RunAsync(server,"invalid.invalid",TimeSpan.FromSeconds(1));
        Assert.Contains(report.Results,r=>r.Protocol=="TCP"&&r.State==ProbeState.Refused);Assert.Contains(report.Results,r=>r.Protocol=="UDP"&&r.State==ProbeState.InsufficientEvidence);Assert.Contains(report.Results,r=>r.Family=="IPv6"&&r.State==ProbeState.Unsupported);Assert.Contains(report.Results,r=>r.Protocol=="DNS"&&r.State==ProbeState.DnsError);Assert.DoesNotContain(report.Results,r=>r.State==ProbeState.PathRestrictionSuspected);
    }
    [Fact] public async Task CancelledDiagnosticDoesNotReturnFabricatedSuccess()
    {var server=new ServerProfile(Guid.NewGuid(),"test","127.0.0.1","","",22,"test",null,[]);using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new NetworkDiagnostics(new Refused()).RunAsync(server,null,TimeSpan.FromSeconds(1),token:cancel.Token));}
    [Fact] public async Task ClosingDiagnosticsWaitsForCancellationBeforeDisposingConnectionServices()
    {
        var service=new WaitingNetwork();var vm=new NetworkDiagnosticsViewModel(service,null,()=>new(),new NoPlatform());vm.Select(new(Guid.NewGuid(),"fixture","127.0.0.1","","",22,"test",null,[]));
        var run=vm.RunCommand.ExecuteAsync(null);await service.Started.Task;Assert.True(vm.IsBusy);await vm.CancelAndWaitAsync();await run;Assert.False(vm.IsBusy);Assert.Contains("取消",vm.Status);
    }
    private sealed class WaitingNetwork:INetworkDiagnostics
    {
        public TaskCompletionSource Started{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<NetworkDiagnosticReport> RunAsync(ServerProfile server,string? domain,TimeSpan timeout,IProgress<NetworkProbeResult>? progress=null,CancellationToken token=default){Started.TrySetResult();await Task.Delay(Timeout.Infinite,token);return new(server.Id,DateTimeOffset.UtcNow,[],"");}
    }
    private sealed class NoPlatform:IPlatformActions{public Task CopyAsync(string text)=>Task.CompletedTask;public Task OpenDirectoryAsync(string path)=>Task.CompletedTask;}
    [LocalSshFact] public async Task SlowFingerprintDecisionIsOutsideNetworkTimeout()
    {
        if(Environment.GetEnvironmentVariable("NEKOPROXY_SSH_FIXTURE") is not {} file)return;
        using var info=System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(file));var i=info.RootElement;
        var server=new ServerProfile(Guid.NewGuid(),"isolated",i.GetProperty("host").GetString()!,"","",i.GetProperty("port").GetInt32(),i.GetProperty("username").GetString()!,i.GetProperty("keyPath").GetString(),[]);
        var store=new JsonSettingsStore(Path.Combine(Path.GetTempPath(),"NekoProxy-delayed-trust",Guid.NewGuid().ToString("N")));var factory=new SshConnectionFactory(store,new SlowApproval());
        await using var session=await factory.ConnectAsync(server,TimeSpan.FromSeconds(1));Assert.Contains(session.Attempt!.Records,r=>r.Stage==ConnectionStage.HostKey&&r.Milliseconds>1500&&r.State==AttemptState.Succeeded);Assert.Equal(AttemptState.Succeeded,session.Attempt.State);
    }
    private sealed class SlowApproval:IHostKeyApproval{public async Task<bool> ConfirmAsync(HostKeyCandidate key,CancellationToken token){await Task.Delay(1700,token);return true;}}
    [LocalSshFact] public async Task AuthenticatedLeaseReusesHandshakeButDisconnectAndTrustChangeForceRevalidation()
    {
        using var document=System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("NEKOPROXY_SSH_FIXTURE")!));var i=document.RootElement;
        var server=new ServerProfile(Guid.NewGuid(),"pool fixture",i.GetProperty("host").GetString()!,"","",i.GetProperty("port").GetInt32(),i.GetProperty("username").GetString()!,i.GetProperty("keyPath").GetString(),[]);
        var store=new JsonSettingsStore(Path.Combine(Path.GetTempPath(),"NekoProxy-pool",Guid.NewGuid().ToString("N")));await store.UpdateAsync(s=>s with{KnownHosts=new(){[SshConnectionFactory.Endpoint(server)]=i.GetProperty("fingerprint").GetString()!}});
        var raw=new SshConnectionFactory(store,new Deny());using var pool=new SshSessionPool(raw,store,new NoSecret());
        await using(var first=await pool.ConnectAsync(server,TimeSpan.FromSeconds(2))){Assert.NotNull(first.ConnectionElapsed);Assert.Equal("NEKOPROXY-SSH-TEST",await first.ExecuteAsync("printf 'NEKOPROXY-SSH-TEST'"));}
        await using(var second=await pool.ConnectAsync(server,TimeSpan.FromSeconds(2))){Assert.Null(second.ConnectionElapsed);Assert.Single(second.Attempt!.Records);Assert.Contains("复用",second.Attempt.Records[0].Detail);}
        await pool.DisconnectAsync(server.Id);await using(var third=await pool.ConnectAsync(server,TimeSpan.FromSeconds(2)))Assert.NotNull(third.ConnectionElapsed);
        await store.UpdateAsync(s=>s with{KnownHosts=new(){[SshConnectionFactory.Endpoint(server)]="SHA256:changed-key"}});
        var error=await Assert.ThrowsAsync<SshConnectionException>(()=>pool.ConnectAsync(server,TimeSpan.FromSeconds(2)));Assert.Equal(SshFailureKind.HostChanged,error.Kind);
    }
    [LocalSshFact] public async Task EnvironmentFailureRetainsExitCodeWithoutPublishingArbitraryRemoteOutput()
    {
        using var document=System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("NEKOPROXY_SSH_FIXTURE")!));var i=document.RootElement;
        var server=new ServerProfile(Guid.NewGuid(),"fixture",i.GetProperty("host").GetString()!,"","",i.GetProperty("port").GetInt32(),i.GetProperty("username").GetString()!,i.GetProperty("keyPath").GetString(),[]);
        var factory=new SshConnectionFactory(i.GetProperty("fingerprint").GetString()!);await using var session=await factory.ConnectAsync(server,TimeSpan.FromSeconds(3));
        var error=await Assert.ThrowsAsync<SshConnectionException>(()=>session.ExecuteEnvironmentAsync("fixture intentionally rejected environment"));
        Assert.Equal(42,error.ExitCode);Assert.Equal(ConnectionStage.Environment,error.Attempt!.Records.Last().Stage);Assert.Equal(AttemptState.Failed,error.Attempt.State);Assert.DoesNotContain("Fixture rejects",error.SafeStandardError);Assert.DoesNotContain("intentionally",error.Message);
    }
    [Fact] public void NodeEditorKeepsItsOwnCredentialsAcrossMethodAndProtocolSwitches()
    {
        var server=new ServerProfile(Guid.NewGuid(),"fixture","127.0.0.1","","",22,"test",null,[]);var editor=new NodeEditorViewModel(server);var uuid=editor.Credential;Assert.True(Guid.TryParse(uuid,out _));editor.ProtocolIndex=3;var first=editor.Credential;editor.Method=ShadowsocksKeys.Methods[1];Assert.Equal(32,Convert.FromBase64String(editor.Credential).Length);editor.Method=ShadowsocksKeys.DefaultMethod;Assert.Equal(first,editor.Credential);editor.ProtocolIndex=0;Assert.Equal(uuid,editor.Credential);
    }
    [Fact] public void RestartingWizardRestoresProductionCertificateDefaultsAndFreshSecrets()
    {var vm=new DeploymentViewModel(new RealityKeyGenerator(),new PendingDeploymentService());var old=vm.Uuid;vm.ProtocolIndex=2;vm.AutomaticCertificate=false;vm.AcmeStaging=true;vm.ProxyPort="9999";vm.ResetCommand.Execute(null);Assert.True(vm.AutomaticCertificate);Assert.False(vm.AcmeStaging);Assert.Equal(0,vm.ProtocolIndex);Assert.Equal("443",vm.ProxyPort);Assert.NotEqual(old,vm.Uuid);}
    [Theory][InlineData(false,false,"8443")][InlineData(true,false,"443")][InlineData(false,true,"443")]
    public async Task OnlyNewUneditedDefaultPortCanAvoidAnExistingListener(bool saved,bool explicitPort,string expected)
    {
        var vm=new DeploymentViewModel(new RealityKeyGenerator(),new PortInspection());
        var node=new ProxyNode(Guid.NewGuid(),"saved",ProxyProtocol.Shadowsocks2022,443){Method=ShadowsocksKeys.DefaultMethod,ParametersConfigured=true};
        var server=new ServerProfile(Guid.NewGuid(),"fixture","127.0.0.1","","",22,"test",null,[node]){Authentication=SshAuthenticationKind.Password};
        if(saved)vm.LoadSavedDraft(new(server,node,new(ShadowsocksKeys.Generate(node.Method))));else{vm.Host=server.Host;vm.AuthenticationIndex=1;}
        if(explicitPort){vm.ProxyPort="9443";vm.ProxyPort="443";}
        var credential=vm.Uuid;await vm.ConnectCommand.ExecuteAsync(null);Assert.Equal(expected,vm.ProxyPort);Assert.Equal(credential,vm.Uuid);Assert.True(vm.IsConnected);
    }
    private sealed class PortInspection:IRemoteDeploymentService
    {
        public Task<RemoteInspection> InspectHostAsync(ServerProfile server,ProxyNode node,TimeSpan timeout,string? passphrase=null,CancellationToken token=default)=>Task.FromResult(new RemoteInspection("debian","x86_64",true,true,true,false,false,node.Port!=443,true,"inactive","APT",DateTimeOffset.UtcNow));
        public Task<RemoteInspection> InspectAsync(RemoteDeploymentRequest request,CancellationToken token=default)=>throw new NotSupportedException();
        public Task<RemoteDeploymentOutcome> DeployRemoteAsync(RemoteDeploymentRequest request,IProgress<DeploymentProgress>? progress=null,CancellationToken token=default)=>throw new NotSupportedException();
        public Task<DeploymentResult> DeployAsync(DeploymentPlan plan,CancellationToken token=default)=>throw new NotSupportedException();
    }
    private sealed class Deny:IHostKeyApproval{public Task<bool> ConfirmAsync(HostKeyCandidate key,CancellationToken token)=>Task.FromResult(false);}
    private sealed class NoSecret:ISshSecretSource{public Task<string?> RequestAsync(ServerProfile server,CancellationToken token)=>Task.FromResult<string?>(null);public void SetSessionSecret(ServerProfile server,string value){}public void Clear(){}}
    private sealed class Refused:ISshConnectionFactory{public Task<ISshSession> ConnectAsync(ServerProfile s,TimeSpan t,CancellationToken token=default)=>throw new SshConnectionException(SshFailureKind.Network,"isolated fixture refusal");}
}
