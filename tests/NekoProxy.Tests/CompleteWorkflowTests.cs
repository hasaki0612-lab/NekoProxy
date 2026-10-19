using System.Text.Json;
using NekoProxy.Core;
using NekoProxy.Deployment;
using NekoProxy.Exporters;
using NekoProxy.Infrastructure;
using NekoProxy.Desktop.ViewModels;
using Xunit;
namespace NekoProxy.Tests;

public sealed class CompleteWorkflowTests
{
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)]
    public async Task WizardSaveRestartEditServerGenerateAndExplicitClientExport(int index)
    {
        var dir=Temp();var store=new JsonSettingsStore(dir);await store.SaveAsync(new(){ExportDirectory=Path.Combine(dir,"exports")});
        var repo=new JsonServerRepository(dir);var vault=new CredentialVault(dir,TestCredentialStore.Create(dir));
        var vm=Create(repo,store,vault);await vm.InitializeAsync();var d=vm.Deployment;
        d.ProtocolIndex=index;d.Host="192.0.2.11";d.ServerName="local-test-server";d.NodeName="node-"+index;d.Sni="example.com";
        d.AutomaticCertificate=false;d.CertificatePath="/etc/neko/cert.pem";d.CertificateKeyPath="/etc/neko/key.pem";
        if(d.IsReality)d.GenerateKeysCommand.Execute(null);else d.GenerateCredentialCommand.Execute(null);
        var draft=d.BuildLocalDraft();var secret=draft.Secrets;
        await d.SaveLocalNodeCommand.ExecuteAsync(null);Assert.Single(vm.Servers);Assert.True(vm.SelectedNode!.ParametersConfigured);
        await d.SaveLocalNodeCommand.ExecuteAsync(null);Assert.Single(vm.Servers);Assert.Single(vm.SelectedServer!.Nodes);
        Assert.False(Directory.Exists(Path.Combine(dir,"exports")));Assert.Null(vm.Exporter);
        var meta=await File.ReadAllTextAsync(Path.Combine(dir,"servers.json"));var encrypted=await File.ReadAllTextAsync(Path.Combine(dir,"credentials.json"));
        Assert.DoesNotContain(secret.Credential,meta);Assert.DoesNotContain(secret.Credential,encrypted);
        if(secret.RealityPrivateKey is {} priv){Assert.DoesNotContain(priv,meta);Assert.DoesNotContain(priv,encrypted);Assert.DoesNotContain(priv,d.Preview);}
        var restarted=Create(repo,store,vault);await restarted.InitializeAsync();await restarted.EditNodeCommand.ExecuteAsync(null);
        var editor=restarted.NodeEditor!;Assert.False(editor.MetadataOnly);Assert.Equal(secret.Credential,editor.Credential);Assert.Equal(secret.RealityPrivateKey??"",editor.RealityPrivateKey);
        Assert.Equal(d.Protocol,editor.Protocol);editor.Port="9443";editor.Name="re-edited";
        restarted.PickServerFileAsync=()=>Task.FromResult<string?>(Path.Combine(dir,"server.json"));
        restarted.CancelNodeEditorCommand.Execute(null);await restarted.EditNodeCommand.ExecuteAsync(null);
        editor=restarted.NodeEditor!;editor.Port="9443";editor.Name="re-edited";
        await editor.SaveServerFileCommand.ExecuteAsync(null);Assert.True(File.Exists(Path.Combine(dir,"server.json")));
        var server=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dir,"server.json"))).RootElement.GetProperty("inbounds")[0];Assert.Equal(9443,server.GetProperty("listen_port").GetInt32());
        await restarted.SaveNodeCommand.ExecuteAsync(null);Assert.Null(restarted.NodeEditor);Assert.Equal("",editor.Credential);Assert.Equal("",editor.RealityPrivateKey);
        await restarted.OpenExporterCommand.ExecuteAsync(null);var export=restarted.Exporter!;Assert.Equal(secret.Credential,export.Credential);
        Assert.Null(export.SelectedClient);await export.GenerateCommand.ExecuteAsync(null);Assert.False(Directory.Exists(Path.Combine(dir,"exports")));
        foreach(var client in export.Clients.Where(c=>d.Protocol!=ProxyProtocol.Shadowsocks2022||c.Type!=ClientType.Shadowrocket))
        {export.SelectedClient=client;export.ExportScope=client.Type==ClientType.Shadowrocket?1:0;await export.GenerateCommand.ExecuteAsync(null);Assert.Contains("已生成",export.Message);}
        foreach(var file in Directory.GetFiles(Path.Combine(dir,"exports"),"*",SearchOption.AllDirectories))
        {var text=await File.ReadAllTextAsync(file);Assert.DoesNotContain("private_key",text);Assert.DoesNotContain("certificate_path",text);if(secret.RealityPrivateKey is {} privateKey)Assert.DoesNotContain(privateKey,text);}
        restarted.CloseExporterCommand.Execute(null);Assert.Equal("",export.Credential);vm.Settings.Updates.Dispose();restarted.Settings.Updates.Dispose();
    }
    [Fact] public async Task UpdatingClientCredentialPreservesEncryptedRealityPrivateKeyAndBackupRoundTrips()
    {
        var dir=Temp();var id=Guid.NewGuid();var vault=new CredentialVault(dir,TestCredentialStore.Create(dir));var k=new RealityKeyGenerator().Generate();
        await vault.SetNodeSecretsAsync(id,new(k.Uuid,k.PrivateKey));await vault.SetClientCredentialAsync(id,"changed-test-only");
        Assert.Equal(new NodeSecrets("changed-test-only",k.PrivateKey),await vault.GetNodeSecretsAsync(id));
        var repo=new JsonServerRepository(dir);
        await repo.SaveAsync([new(Guid.NewGuid(),"referenced node","192.0.2.1","","",22,"root",null,[new(id,"reality",ProxyProtocol.VlessReality)])]);
        var service=new LocalPrivacyService(dir,new JsonSettingsStore(dir),repo,vault);var backup=Path.Combine(dir,"test.nekobak");
        await service.BackupAsync(backup,"long-test-password");await vault.RemoveAsync(id);await service.RestoreAsync(backup,"long-test-password",true);
        Assert.Equal(new NodeSecrets("changed-test-only",k.PrivateKey),await vault.GetNodeSecretsAsync(id));Assert.DoesNotContain(k.PrivateKey,await File.ReadAllTextAsync(backup));
        await vault.SetAsync(id,"old-password");Assert.Equal(new NodeSecrets("old-password"),await vault.GetNodeSecretsAsync(id));
    }
    [Fact] public async Task FailedMetadataSaveRollsBackNewEncryptedSecrets()
    {
        var dir=Temp();var repo=new FailingRepository();var vault=new CredentialVault(dir,TestCredentialStore.Create(dir));var vm=Create(repo,new JsonSettingsStore(dir),vault);await vm.InitializeAsync();
        var d=vm.Deployment;d.Host="192.0.2.4";d.ProtocolIndex=3;d.GenerateCredentialCommand.Execute(null);var draft=d.BuildLocalDraft();
        await d.SaveLocalNodeCommand.ExecuteAsync(null);Assert.Empty(vm.Servers);Assert.Null(await vault.GetAsync(draft.Node.Id));Assert.Contains("失败",d.Message);vm.Settings.Updates.Dispose();
    }
    [Theory][InlineData(ProxyProtocol.TrojanTls)][InlineData(ProxyProtocol.Hysteria2)][InlineData(ProxyProtocol.Shadowsocks2022)]
    public void RejectsIncompatibleSecurityFields(ProxyProtocol protocol)
    {
        var node=new ClientNodeConfiguration("s","n","192.0.2.1",443,protocol,protocol==ProxyProtocol.Shadowsocks2022?ShadowsocksKeys.Generate(ShadowsocksKeys.DefaultMethod):"test-password",protocol==ProxyProtocol.Shadowsocks2022?"":"example.com",RealityPublicKey:"unexpected");
        Assert.Throws<ArgumentException>(()=>new SingBoxExporter().Generate(node));Assert.Throws<ArgumentException>(()=>new MihomoExporter().Generate(node));Assert.Throws<ArgumentException>(()=>new ServerConfigurationGenerator().Generate(new(node)));
    }
    [Fact] public void RejectsWrongRealityKeyPairAndNoncanonicalKey()
    {
        var a=new RealityKeyGenerator().Generate();var b=new RealityKeyGenerator().Generate();var n=new ClientNodeConfiguration("s","n","192.0.2.1",443,ProxyProtocol.VlessReality,a.Uuid,"example.com",a.PublicKey,a.ShortId);
        Assert.Throws<ArgumentException>(()=>new ServerConfigurationGenerator().Generate(new(n,b.PrivateKey)));
        Assert.False(ProtocolParameters.Key32(new string('A',42)+"B"));Assert.False(ProtocolParameters.Key32(new string('猫',43)));Assert.False(ProtocolParameters.Key32(a.PublicKey+"="));
        Assert.Throws<ArgumentException>(()=>new SingBoxExporter().Generate(n with{ShortId="abc"}));
        Assert.Throws<ArgumentException>(()=>new ServerConfigurationGenerator().Generate(new(n,a.PrivateKey,"/cert.pem","/key.pem")));
    }
    [Fact] public void LegacyMetadataCanBeEditedWithoutInventingAuthentication()
    {
        var server=SampleProfiles.Create()[0];var node=server.Nodes[0];var editor=new NodeEditorViewModel(server,node);Assert.True(editor.MetadataOnly);editor.Name="renamed";editor.Port="8443";
        var updated=editor.Build()!;Assert.Equal(node.Id,updated.Id);Assert.Equal("renamed",updated.Name);Assert.False(updated.ParametersConfigured);Assert.Equal("",editor.Credential);
        editor.MetadataOnly=false;Assert.Null(editor.Build());
    }
    [Fact] public async Task WizardExportEntrySavesOneNodeButDoesNotAutomaticallyExport()
    {
        var dir=Temp();var store=new JsonSettingsStore(dir);await store.SaveAsync(new(){ExportDirectory=Path.Combine(dir,"exports")});var vm=Create(new JsonServerRepository(dir),store,new(dir,TestCredentialStore.Create(dir)));await vm.InitializeAsync();
        var d=vm.Deployment;d.ProtocolIndex=2;d.Host="192.0.2.7";d.Sni="example.com";d.AutomaticCertificate=false;d.CertificatePath="/cert.pem";d.CertificateKeyPath="/key.pem";d.GenerateCredentialCommand.Execute(null);
        await d.ExportClientCommand.ExecuteAsync(null);Assert.Equal("管理服务",vm.Page);Assert.NotNull(vm.Exporter);Assert.Null(vm.Exporter.SelectedClient);Assert.Single(vm.Servers);Assert.False(Directory.Exists(Path.Combine(dir,"exports")));vm.CloseExporterCommand.Execute(null);vm.Settings.Updates.Dispose();
    }
    private static MainViewModel Create(IServerRepository repo,ISettingsStore store,CredentialVault vault)=>new(repo,store,new DisconnectedServerMonitor(),new NullPlatform(),new ExportService([new SingBoxExporter(),new MihomoExporter(),new ShadowrocketExporter()]),new RealityKeyGenerator(),new PendingDeploymentService(),vault:vault);
    private static string Temp(){var d=Path.Combine(Path.GetTempPath(),"NekoProxy-v031",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(d);return d;}
    private sealed class NullPlatform:IPlatformActions{public Task CopyAsync(string text)=>Task.CompletedTask;public Task OpenDirectoryAsync(string path)=>Task.CompletedTask;}
    private sealed class FailingRepository:IServerRepository{public Task<IReadOnlyList<ServerProfile>> LoadAsync(CancellationToken t=default)=>Task.FromResult<IReadOnlyList<ServerProfile>>([]);public Task SaveAsync(IReadOnlyList<ServerProfile> s,CancellationToken t=default)=>throw new IOException("Test failure");}
}
