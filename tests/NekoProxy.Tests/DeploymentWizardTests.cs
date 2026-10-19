using NekoProxy.Core;
using NekoProxy.Deployment;
using NekoProxy.Desktop.ViewModels;
using NekoProxy.Exporters;
using NekoProxy.Infrastructure;
using Xunit;
namespace NekoProxy.Tests;
public sealed class DeploymentWizardTests
{
    [Fact]public async Task RealModeRequiresConnectionPreflightAndExplicitConfirmationThenPersistsUnit()
    {
        var dir=Temp();var store=new JsonSettingsStore(dir);var repo=new JsonServerRepository(dir);var remote=new Remote();
        var vm=new MainViewModel(repo,store,new DisconnectedServerMonitor(),new Platform(),new ExportService([new SingBoxExporter()]),new RealityKeyGenerator(),remote,vault:new(dir,TestCredentialStore.Create(dir)));await vm.InitializeAsync();
        var d=vm.Deployment;d.ProtocolIndex=3;d.Host="127.0.0.1";d.PrivateKeyPath=Path.Combine(dir,"dummy-test-key");await File.WriteAllTextAsync(d.PrivateKeyPath,"fixture-only");d.Step=2;
        await d.NextCommand.ExecuteAsync(null);Assert.Equal(2,d.Step);Assert.Contains("先连接",d.Message);
        await d.ConnectCommand.ExecuteAsync(null);Assert.True(d.IsConnected);await d.NextCommand.ExecuteAsync(null);Assert.Equal(1,d.Step);await d.NextCommand.ExecuteAsync(null);Assert.Equal(3,d.Step);
        d.GenerateCredentialCommand.Execute(null);await d.NextCommand.ExecuteAsync(null);Assert.Equal(4,d.Step);Assert.False(d.CanExecuteDeployment);Assert.DoesNotContain("环境与配置所有权：待检测",d.Preview);Assert.Contains("已检查",d.Preview);
        await d.ExecuteCommand.ExecuteAsync(null);Assert.Equal(0,remote.DeployCalls);Assert.Empty(vm.Servers);
        d.DeploymentConfirmed=true;Assert.True(d.CanExecuteDeployment);await d.NextCommand.ExecuteAsync(null);Assert.Equal(5,d.Step);Assert.True(d.DeploymentSucceeded);Assert.Equal(1,remote.DeployCalls);
        var saved=(await repo.LoadAsync()).Single().Nodes.Single();Assert.Equal(remote.Unit,saved.ServiceUnit);Assert.NotNull(saved.LastDeployment);Assert.False(saved.NeedsDeployment);
        await vm.PrepareDeploymentCommand.ExecuteAsync(null);Assert.Equal(2,d.Step);Assert.False(d.IsConnected);Assert.Equal(saved.Id,d.BuildLocalDraft().Node.Id);Assert.Equal("部署代理",vm.Page);vm.Settings.Updates.Dispose();
    }
    [Fact]public async Task CancellationDoesNotClaimSuccessAndReleasesPendingOperation()
    {
        var dir=Temp();var remote=new Remote{Wait=true};var d=new DeploymentViewModel(new RealityKeyGenerator(),remote){Host="127.0.0.1",ProtocolIndex=3,SaveLocalAsync=(_,_)=>Task.CompletedTask};d.GenerateCredentialCommand.Execute(null);d.Step=3;
        await d.NextCommand.ExecuteAsync(null);d.DeploymentConfirmed=true;var execution=d.ExecuteCommand.ExecuteAsync(null);await remote.Started.Task;
        Assert.True(d.IsOperating);await d.CancelAndWaitAsync();await execution;Assert.False(d.IsOperating);Assert.False(d.DeploymentSucceeded);Assert.True(d.PendingOperation.IsCompleted);
    }
    [Fact]public async Task TrustCacheResetRequiresConfirmationAndPreservesCredentialsAndServers()
    {
        var dir=Temp();var store=new JsonSettingsStore(dir);await store.SaveAsync(new(){KnownHosts=new(){["127.0.0.1|22"]="SHA256:fixture"}});var repo=new JsonServerRepository(dir);await repo.SaveAsync(SampleProfiles.Create());var vault=new CredentialVault(dir,TestCredentialStore.Create(dir));var id=Guid.NewGuid();await vault.SetAsync(id,"fixture-secret");
        var vm=new PrivacyViewModel(new LocalPrivacyService(dir,store,repo,vault),store,new Platform(),()=>Task.CompletedTask);
        vm.ResetHostTrustCommand.Execute(null);Assert.True(vm.HasConfirmation);Assert.Single((await store.LoadAsync()).KnownHosts);vm.CancelConfirmationCommand.Execute(null);Assert.Single((await store.LoadAsync()).KnownHosts);
        vm.ResetHostTrustCommand.Execute(null);await vm.ConfirmCommand.ExecuteAsync(null);Assert.Empty((await store.LoadAsync()).KnownHosts);Assert.Equal(3,(await repo.LoadAsync()).Count);Assert.Equal("fixture-secret",await vault.GetAsync(id));
        vm.RemoteOperationBusy=()=>true;vm.ClearServers=true;vm.RequestClearCommand.Execute(null);Assert.False(vm.HasConfirmation);Assert.Equal(3,(await repo.LoadAsync()).Count);
    }
    private static string Temp(){var d=Path.Combine(Path.GetTempPath(),"NekoProxy-wizard",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(d);return d;}
    private sealed class Platform:IPlatformActions{public Task CopyAsync(string s)=>Task.CompletedTask;public Task OpenDirectoryAsync(string d)=>Task.CompletedTask;}
    private sealed class Remote:IRemoteDeploymentService
    {
        public bool Wait;public int DeployCalls;public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);public string Unit="";
        private static RemoteInspection Info=>new("ubuntu","x86_64",true,true,false,false,false,true,true,"inactive","APT",DateTimeOffset.UtcNow);
        public Task<RemoteInspection> InspectHostAsync(ServerProfile s,ProxyNode n,TimeSpan t,string? p=null,CancellationToken token=default)=>Task.FromResult(Info);
        public Task<RemoteInspection> InspectAsync(RemoteDeploymentRequest r,CancellationToken token=default)=>Task.FromResult(Info);
        public Task<DeploymentResult> DeployAsync(DeploymentPlan p,CancellationToken token=default)=>Task.FromResult(new DeploymentResult(false,"Use remote request"));
        public async Task<RemoteDeploymentOutcome> DeployRemoteAsync(RemoteDeploymentRequest r,IProgress<DeploymentProgress>? p=null,CancellationToken token=default)
        {DeployCalls++;Started.TrySetResult();if(Wait)await Task.Delay(Timeout.Infinite,token);Unit=new RemotePaths(r.Draft.Node.Id).Unit;return new(true,"Controlled workflow fixture only",Unit,Info,[]);}
    }
}
