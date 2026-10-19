using System.Text.Json;
using NekoProxy.Core;
using NekoProxy.Deployment;
using NekoProxy.Desktop.ViewModels;
using NekoProxy.Infrastructure;
using Xunit;
namespace NekoProxy.Tests;

public sealed class FinalPolishTests
{
    [Fact] public void NewInstallFollowsSystemWhileLegacyExplicitThemeIsPreserved()
    {
        Assert.Equal("system",new AppSettings().Theme);
        Assert.Equal(0,ThemePreference.Selection(new AppSettings().Theme));Assert.Equal(1,ThemePreference.Selection("浅色"));Assert.Equal(2,ThemePreference.Selection("深色"));
        Assert.Equal("system",ThemePreference.FromSelection(0));Assert.Equal("light",ThemePreference.FromSelection(1));Assert.Equal("dark",ThemePreference.FromSelection(2));
    }
    [Theory][InlineData("浅色",false)][InlineData("light",false)][InlineData("dark",true)][InlineData("深色",true)][InlineData(null,false)]
    public void LegacyThemePreferenceRemainsCompatible(string? saved,bool dark)=>Assert.Equal(dark,ThemePreference.IsDark(saved));

    [Fact] public async Task ThemePersistsWithoutLosingOtherPreferences()
    {
        var directory=Path.Combine(Path.GetTempPath(),"NekoProxy-v052-settings",Guid.NewGuid().ToString("N"));
        var store=new JsonSettingsStore(directory);
        await store.SaveAsync(new(){Theme="浅色",Language="ja",KnownHosts=new(){["example.com|22"]="SHA256:test"}});
        var vm=new SettingsViewModel(store,new NoPlatform());vm.Load(await store.LoadAsync());vm.ThemeIndex=2;await vm.SaveCommand.ExecuteAsync(null);
        var loaded=await store.LoadAsync();Assert.Equal("dark",loaded.Theme);Assert.Equal("ja",loaded.Language);Assert.Single(loaded.KnownHosts);
        var restored=new SettingsViewModel(store,new NoPlatform());restored.Load(loaded);Assert.Equal(2,restored.ThemeIndex);
    }
    [Fact] public void PasswordInputIsEphemeralAndAuthChangesClearIt()
    {
        var vm=new ServerEditorViewModel(){Name="test",Host="192.0.2.1",PrivateKeyPath="old-key",AuthenticationIndex=1,LoginSecret="LOCAL-TEST-ONLY"};
        var profile=vm.Build()!;Assert.Null(profile.PrivateKeyPath);Assert.Equal(SshAuthenticationKind.Password,profile.Authentication);
        Assert.DoesNotContain(vm.LoginSecret,JsonSerializer.Serialize(profile));
        vm.AuthenticationIndex=0;Assert.Empty(vm.LoginSecret);vm.LoginSecret="temporary";vm.ClearSecret();Assert.Empty(vm.LoginSecret);
        Assert.Empty(new ServerEditorViewModel(profile).LoginSecret);
    }
    [Fact] public async Task SessionSecretsAreBoundToEndpointAndCanBeForgotten()
    {
        var vm=new SshSecretViewModel();var server=new ServerProfile(Guid.NewGuid(),"test","192.0.2.1","","",22,"test",null,[]){Authentication=SshAuthenticationKind.Password};
        vm.SetSessionSecret(server,"test-ephemeral");Assert.Equal("test-ephemeral",await vm.RequestAsync(server,CancellationToken.None));var before=vm.Revision;vm.Forget(server);Assert.True(vm.Revision>before);
    }
    [Fact] public async Task AuthenticationFailureClearsOnlyTheWrongSecretAndPreservesDraft()
    {
        var vm=new DeploymentViewModel(new RealityKeyGenerator(),new RejectedPassword()){Host="192.0.2.1",AuthenticationIndex=1,KeyPassphrase="local-test-wrong"};var uuid=vm.Uuid;
        await vm.ConnectCommand.ExecuteAsync(null);Assert.Empty(vm.KeyPassphrase);Assert.Equal("192.0.2.1",vm.Host);Assert.Equal(uuid,vm.Uuid);Assert.False(vm.IsConnected);Assert.Contains("认证",vm.Message);
    }
    [Fact] public void OldDeploymentDoesNotImplyCoreOrClientVerification()
    {
        var node=JsonSerializer.Deserialize<ProxyNode>("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Name\":\"old\",\"Protocol\":0,\"Port\":443,\"ServiceUnit\":\"owned\",\"LastDeployment\":\"2026-10-14T00:00:00Z\"}")!;
        Assert.Null(node.Verification);Assert.Equal("客户端尚未验证",new NodeVerification().ClientLabel);
        var started=new NodeVerification{GeneratedAt=DateTimeOffset.UtcNow,CoreCheckedAt=DateTimeOffset.UtcNow,DeployedAt=DateTimeOffset.UtcNow,ServiceStartedAt=DateTimeOffset.UtcNow};
        Assert.Contains("客户端尚未验证",started.Summary);Assert.DoesNotContain("客户端连接验证成功",started.Summary);
        Assert.Equal("客户端连接验证失败",(started with{ClientState=ClientVerificationState.Failed}).ClientLabel);
    }
    [Fact] public void EditingConfigurationInvalidatesPriorVerification()
    {
        var keys=new RealityKeyGenerator().Generate();var node=new ProxyNode(Guid.NewGuid(),"old",ProxyProtocol.VlessReality){Sni="www.microsoft.com",ParametersConfigured=true,RealityPublicKey=keys.PublicKey,ShortId=keys.ShortId,ServiceUnit="owned",Verification=new(){ClientState=ClientVerificationState.Passed}};
        var server=new ServerProfile(Guid.NewGuid(),"test","192.0.2.1","","",22,"test",null,[node]);var draft=new LocalNodeDraft(server,node,new(keys.Uuid,keys.PrivateKey));
        var vm=new DeploymentViewModel(new RealityKeyGenerator(),new PendingDeploymentService());vm.LoadSavedDraft(draft);Assert.Equal(ClientVerificationState.Passed,vm.BuildLocalDraft().Node.Verification!.ClientState);
        vm.ProxyPort="8443";var changed=vm.BuildLocalDraft().Node;Assert.True(changed.NeedsDeployment);Assert.Equal(ClientVerificationState.Unverified,changed.Verification!.ClientState);Assert.Null(changed.Verification.CoreCheckedAt);
        vm.LoadSavedDraft(draft);vm.Host="192.0.2.2";Assert.Equal(ClientVerificationState.Unverified,vm.BuildLocalDraft().Node.Verification!.ClientState);
        var editor=new NodeEditorViewModel(server,node){MetadataOnly=true,Port="8443"};Assert.Null(editor.Build()!.Verification);Assert.True(editor.Build()!.NeedsDeployment);
    }
    [Theory][InlineData(0,"0 B/s")][InlineData(0.000001,"1 B/s")][InlineData(0.001953125,"2 KiB/s")][InlineData(2,"2 MiB/s")]
    public void SmallMeasuredRatesAreNotRoundedToZero(double mib,string display)=>Assert.Equal(display,NetworkRateText.FormatMiB(mib));
    private sealed class NoPlatform:IPlatformActions{public Task CopyAsync(string text)=>Task.CompletedTask;public Task OpenDirectoryAsync(string path)=>Task.CompletedTask;}
    private sealed class RejectedPassword:IRemoteDeploymentService
    {
        public Task<DeploymentResult> DeployAsync(DeploymentPlan plan,CancellationToken token=default)=>throw new NotSupportedException();
        public Task<RemoteInspection> InspectHostAsync(ServerProfile s,ProxyNode n,TimeSpan t,string? passphrase=null,CancellationToken token=default)=>throw new SshConnectionException(SshFailureKind.Authentication,"SSH 认证失败，请重新输入密码。");
        public Task<RemoteInspection> InspectAsync(RemoteDeploymentRequest r,CancellationToken token=default)=>throw new NotSupportedException();
        public Task<RemoteDeploymentOutcome> DeployRemoteAsync(RemoteDeploymentRequest r,IProgress<DeploymentProgress>? progress=null,CancellationToken token=default)=>throw new NotSupportedException();
    }
}
