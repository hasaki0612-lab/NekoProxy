using System.Net;
using System.Text.Json;
using NekoProxy.Core;
using NekoProxy.Deployment;
using NekoProxy.Exporters;
using NekoProxy.Infrastructure;
using NekoProxy.Desktop.ViewModels;
using Xunit;
namespace NekoProxy.Tests;
public sealed class ProtocolUpdateTests
{
    [Theory][InlineData("2022-blake3-aes-128-gcm",16)][InlineData("2022-blake3-aes-256-gcm",32)][InlineData("2022-blake3-chacha20-poly1305",32)]
    public void ShadowsocksKeysAndConfigurationsMatch(string method,int size)
    {
        var key=ShadowsocksKeys.Generate(method);Assert.Equal(size,Convert.FromBase64String(key).Length);Assert.NotEqual(key,ShadowsocksKeys.Generate(method));
        var n=Node(ProxyProtocol.Shadowsocks2022) with {Credential=key,Method=method,Sni=""};
        var inbound=JsonDocument.Parse(new ServerConfigurationGenerator().Generate(new(n))).RootElement.GetProperty("inbounds")[0];
        var outbound=JsonDocument.Parse(new SingBoxExporter().Generate(n)).RootElement.GetProperty("outbounds")[0];
        Assert.Equal("shadowsocks",inbound.GetProperty("type").GetString());Assert.Equal(key,inbound.GetProperty("password").GetString());
        Assert.Equal(method,outbound.GetProperty("method").GetString());Assert.False(outbound.TryGetProperty("tls",out _));
        var yaml=new MihomoExporter().Generate(n);Assert.Contains(method,yaml);Assert.Contains("type: ss",yaml);
        var uri=ExportValidation.Link(n);Assert.StartsWith("ss://"+method+":",uri);Assert.Contains(Uri.EscapeDataString(key),uri);Assert.Contains("[2001:db8::1]",uri);
        Assert.Throws<ArgumentException>(()=>new ShadowrocketExporter().Generate(n));
    }
    [Theory][InlineData("bad")][InlineData("YWJj")][InlineData("AAAAAAAAAAAAAAAAAAAAAA== ")][InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void RejectInvalidShadowsocksKeys(string key)=>Assert.Throws<ArgumentException>(()=>ShadowsocksKeys.Validate(ShadowsocksKeys.DefaultMethod,key));
    [Fact] public void TrojanTlsIsConsistentAndNeverSkipsVerification()
    {
        var n=Node(ProxyProtocol.TrojanTls);var options=new ServerNodeConfiguration(n,CertificatePath:"/etc/neko/cert.pem",CertificateKeyPath:"/etc/neko/key.pem");
        var server=JsonDocument.Parse(new ServerConfigurationGenerator().Generate(options)).RootElement.GetProperty("inbounds")[0];
        Assert.Equal("trojan",server.GetProperty("type").GetString());Assert.Equal(n.Credential,server.GetProperty("users")[0].GetProperty("password").GetString());
        Assert.False(server.GetProperty("tls").TryGetProperty("acme",out _));
        var client=JsonDocument.Parse(new SingBoxExporter().Generate(n)).RootElement.GetProperty("outbounds")[0];
        Assert.Equal("trojan",client.GetProperty("type").GetString());Assert.False(client.GetProperty("tls").TryGetProperty("insecure",out _));
        Assert.Contains("skip-cert-verify: false",new MihomoExporter().Generate(n));Assert.StartsWith("trojan://",new ShadowrocketExporter().Generate(n));
        Assert.Throws<ArgumentException>(()=>new ServerConfigurationGenerator().Generate(new(n)));
    }
    [Fact] public void RealityUsesTcpVisionAndTheSameKeysInBothDirections()
    {
        var k=new RealityKeyGenerator().Generate();var n=Node(ProxyProtocol.VlessReality) with{Credential=k.Uuid,RealityPublicKey=k.PublicKey,ShortId=k.ShortId};
        var server=JsonDocument.Parse(new ServerConfigurationGenerator().Generate(new(n,k.PrivateKey))).RootElement.GetProperty("inbounds")[0];
        var client=JsonDocument.Parse(new SingBoxExporter().Generate(n)).RootElement.GetProperty("outbounds")[0];
        Assert.Equal("xtls-rprx-vision",server.GetProperty("users")[0].GetProperty("flow").GetString());Assert.Equal("xtls-rprx-vision",client.GetProperty("flow").GetString());
        Assert.False(server.TryGetProperty("transport",out _));Assert.False(client.TryGetProperty("transport",out _));
        Assert.DoesNotContain(k.PrivateKey,new SingBoxExporter().Generate(n));Assert.Contains("type=tcp",ExportValidation.Link(n));
    }
    [Fact] public void LegacyProtocolNumbersAndMetadataStillLoad()
    {
        Assert.Equal(0,(int)ProxyProtocol.VlessReality);Assert.Equal(1,(int)ProxyProtocol.Hysteria2);Assert.Equal(2,(int)ProxyProtocol.Shadowsocks2022);
        var node=JsonSerializer.Deserialize<ProxyNode>("{\"Id\":\"12345678-1234-1234-1234-123456789012\",\"Name\":\"old\",\"Protocol\":1,\"Port\":443}")!;
        Assert.Equal(ProxyProtocol.Hysteria2,node.Protocol);Assert.Equal("",node.Sni);Assert.Equal(ShadowsocksKeys.DefaultMethod,node.Method);
    }
    [Fact] public async Task NewNodePersistsPublicParametersAndEncryptedCredentialThenReopens()
    {
        var dir=Temp();var store=new JsonSettingsStore(dir);var repo=new JsonServerRepository(dir);var server=SampleProfiles.Create()[0] with{Nodes=[]};await repo.SaveAsync([server]);
        var vault=new CredentialVault(dir,TestCredentialStore.Create(dir));var vm=new MainViewModel(repo,store,new DisconnectedServerMonitor(),new NullPlatform(),new ExportService([new SingBoxExporter()]),new RealityKeyGenerator(),new PendingDeploymentService(),vault:vault);
        await vm.InitializeAsync();vm.PickServerFileAsync=()=>Task.FromResult<string?>(Path.Combine(dir,"local-server.json"));vm.AddNodeCommand.Execute(null);var e=vm.NodeEditor!;e.Name="local-ss";e.ProtocolIndex=3;e.Method=ShadowsocksKeys.Methods[1];e.GenerateCredentialCommand.Execute(null);var secret=e.Credential;
        await e.SaveServerFileCommand.ExecuteAsync(null);Assert.True(File.Exists(Path.Combine(dir,"local-server.json")));
        Assert.Equal(secret,JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dir,"local-server.json"))).RootElement.GetProperty("inbounds")[0].GetProperty("password").GetString());
        await vm.SaveNodeCommand.ExecuteAsync(null);Assert.Null(vm.NodeEditor);var saved=(await repo.LoadAsync())[0].Nodes[0];Assert.Equal(ProxyProtocol.Shadowsocks2022,saved.Protocol);Assert.Equal(e.Method,saved.Method);
        Assert.DoesNotContain(secret,await File.ReadAllTextAsync(Path.Combine(dir,"servers.json")));Assert.DoesNotContain(secret,await File.ReadAllTextAsync(Path.Combine(dir,"credentials.json")));
        await vm.EditNodeCommand.ExecuteAsync(null);Assert.Equal(secret,vm.NodeEditor!.Credential);vm.CancelNodeEditorCommand.Execute(null);Assert.Equal("",e.Credential);vm.Settings.Updates.Dispose();
    }
    [Theory][InlineData("1.10.0","1.9.9",true)][InlineData("v0.3.0","0.3.0",false)][InlineData("1.2.0","1.3.0",false)]
    public void StableVersionsCompareNumerically(string latest,string current,bool newer)=>Assert.Equal(newer,ReleaseVersions.IsNewer(latest,current));
    [Fact] public void PrereleasesAreNotStableVersions()=>Assert.Throws<InvalidDataException>(()=>ReleaseVersions.Parse("1.3.0-beta.1"));
    [Fact] public async Task ScheduledChecksThrottleFailuresDisableAndAllowManualRetry()
    {
        var store=new JsonSettingsStore(Temp());var source=new FakeSource();var clock=new FakeTime();var coordinator=new UpdateCoordinator(store,source,"0.3.0",clock);
        Assert.Equal(UpdateCheckState.Available,(await coordinator.CheckAsync(false)).State);Assert.Equal(1,source.Calls);
        Assert.Equal(UpdateCheckState.NotDue,(await coordinator.CheckAsync(false)).State);clock.Now+=TimeSpan.FromHours(23.99);Assert.Equal(UpdateCheckState.NotDue,(await coordinator.CheckAsync(false)).State);
        clock.Now+=TimeSpan.FromMinutes(1);source.Fail=true;Assert.Equal(UpdateCheckState.Failed,(await coordinator.CheckAsync(false)).State);Assert.Equal(UpdateCheckState.NotDue,(await coordinator.CheckAsync(false)).State);
        Assert.Equal(UpdateCheckState.Failed,(await coordinator.CheckAsync(true)).State);await store.UpdateAsync(s=>s with{AutoCheckUpdates=false});clock.Now+=TimeSpan.FromDays(2);Assert.Equal(UpdateCheckState.Disabled,(await coordinator.CheckAsync(false)).State);
        source.Fail=false;await store.UpdateAsync(s=>s with{IgnoredVersion="0.4.0"});Assert.Equal(UpdateCheckState.Ignored,(await coordinator.CheckAsync(true)).State);
        var saved=await store.LoadAsync();Assert.False(saved.AutoCheckUpdates);Assert.Equal("0.4.0",saved.LatestKnownVersion);Assert.NotNull(saved.LastUpdateSuccess);
    }
    [Fact] public async Task UnconfiguredChannelMakesNoRequestsEvenOnManualCheck()
    {
        var source=new FakeSource{IsConfigured=false};var coordinator=new UpdateCoordinator(new JsonSettingsStore(Temp()),source,"0.3.0");
        Assert.Equal(UpdateCheckState.Unconfigured,(await coordinator.CheckAsync(false)).State);Assert.Equal(UpdateCheckState.Unconfigured,(await coordinator.CheckAsync(true)).State);Assert.Equal(0,source.Calls);
    }
    [Fact] public async Task ATimeoutIsAFailedCheckAndRemainsThrottled()
    {
        var store=new JsonSettingsStore(Temp());var source=new TimeoutSource();var c=new UpdateCoordinator(store,source,"0.3.0");
        Assert.Equal(UpdateCheckState.Failed,(await c.CheckAsync(false)).State);Assert.Equal(UpdateCheckState.NotDue,(await c.CheckAsync(false)).State);
    }
    [Fact] public async Task CurrentVersionAndDisabledInFlightCheckAreHandled()
    {
        var store=new JsonSettingsStore(Temp());var source=new FakeSource{Version="0.3.0"};var c=new UpdateCoordinator(store,source,"0.3.0");
        Assert.Equal(UpdateCheckState.Current,(await c.CheckAsync(true)).State);
        var blocked=new BlockingSource();var active=new UpdateCoordinator(store,blocked,"0.3.0");using var cancel=new CancellationTokenSource();
        var checking=active.CheckAsync(true,cancel.Token);await blocked.Started.Task;await store.UpdateAsync(s=>s with{AutoCheckUpdates=false});cancel.Cancel();
        Assert.Equal(UpdateCheckState.Cancelled,(await checking).State);Assert.Equal(UpdateCheckState.Disabled,(await active.CheckAsync(false)).State);
    }
    [Theory][InlineData("https://github.com/fixture/neko/releases/tag/v0.4.0",false,false,true)][InlineData("https://evil.example/download",false,false,false)][InlineData("https://github.com/fixture/neko/releases/tag/v0.4.0",true,false,false)][InlineData("https://github.com/fixture/neko/releases/tag/v0.4.0",false,true,false)]
    public async Task GitHubSourceAcceptsOnlyTrustedStablePages(string url,bool prerelease,bool limited,bool accepted)
    {
        var handler=new FixtureHandler(url,prerelease,limited);using var http=new HttpClient(handler);var source=new GitHubReleaseSource(http,"fixture/neko");
        if(accepted)Assert.Equal("0.4.0",(await source.QueryAsync(default)).Version);else await Assert.ThrowsAnyAsync<Exception>(()=>source.QueryAsync(default));
        Assert.Equal("GET",handler.Method);Assert.Equal("https://api.github.com/repos/fixture/neko/releases/latest",handler.Uri);Assert.Null(handler.Body);
    }
    private static ClientNodeConfiguration Node(ProxyProtocol p)=>new("fixture","node","2001:db8::1",443,p,"test-only-password","example.com");
    private static string Temp(){var d=Path.Combine(Path.GetTempPath(),"NekoProxy-v03-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(d);return d;}
    private sealed class NullPlatform:IPlatformActions {public Task CopyAsync(string text)=>Task.CompletedTask;public Task OpenDirectoryAsync(string directory)=>Task.CompletedTask;}
    private sealed class FakeTime:TimeProvider {public DateTimeOffset Now=new(2026,10,13,0,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Now;}
    private sealed class FakeSource:IReleaseSource {public bool IsConfigured{get;set;}=true;public bool Fail;public int Calls;public string Version="0.4.0";public Task<ReleaseInfo> QueryAsync(CancellationToken token){Calls++;return Fail?Task.FromException<ReleaseInfo>(new HttpRequestException()):Task.FromResult(new ReleaseInfo(Version,new("https://github.com/fixture/neko/releases/tag/v0.4.0")));}}
    private sealed class TimeoutSource:IReleaseSource{public bool IsConfigured=>true;public Task<ReleaseInfo> QueryAsync(CancellationToken token)=>Task.FromException<ReleaseInfo>(new TaskCanceledException("Timeout"));}
    private sealed class BlockingSource:IReleaseSource
    {
        public bool IsConfigured=>true;public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ReleaseInfo> QueryAsync(CancellationToken token){Started.SetResult();await Task.Delay(Timeout.Infinite,token);throw new InvalidOperationException();}
    }
    private sealed class FixtureHandler(string page,bool pre,bool limited):HttpMessageHandler
    {
        public string? Method,Uri,Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Method=request.Method.Method;Uri=request.RequestUri!.AbsoluteUri;Body=request.Content is null?null:await request.Content.ReadAsStringAsync(token);return new(limited?HttpStatusCode.TooManyRequests:HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{tag_name="v0.4.0",draft=false,prerelease=pre,html_url=page}))};}
    }
}
