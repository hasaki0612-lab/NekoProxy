using System.Security.Cryptography;
using NekoProxy.Core;
using NekoProxy.Deployment;
using NekoProxy.Desktop.ViewModels;
using NekoProxy.Exporters;
using NekoProxy.Infrastructure;
using Xunit;
namespace NekoProxy.Tests;
public sealed class V060LocalWorkflowTests
{
    // Exercises the storage abstraction, not Windows DPAPI. DPAPI-specific tests remain in the full suite.
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)]
    public async Task SavedNodePolicyExportBackupAndLegacyFilesRemainUsable(int protocol)
    {
        var dir=Path.Combine(Path.GetTempPath(),"NekoProxy-workflow-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        MainViewModel? main=null;using var protector=new IsolatedProtector();
        try
        {
            var store=new JsonSettingsStore(dir);var repo=new JsonServerRepository(dir);var vault=new CredentialVault(dir,protector);var privacy=new LocalPrivacyService(dir,store,repo,vault);
            await store.SaveAsync(new(){ExportDirectory=Path.Combine(dir,"exports")});
            main=new(repo,store,new DisconnectedServerMonitor(),new Platform(),new ExportService([new SingBoxExporter(),new MihomoExporter(),new ShadowrocketExporter()]),new RealityKeyGenerator(),new PendingDeploymentService(),vault:vault);
            await main.InitializeAsync();var d=main.Deployment;d.ProtocolIndex=protocol;d.Host="192.0.2.11";d.Sni="example.com";d.AutomaticCertificate=false;d.CertificatePath="/fixture/cert.pem";d.CertificateKeyPath="/fixture/key.pem";
            var draft=d.BuildLocalDraft();await d.SaveLocalNodeCommand.ExecuteAsync(null);Assert.Single(main.Servers);Assert.Equal(draft.Secrets.Credential,(await vault.GetNodeSecretsAsync(main.SelectedNode!.Id)).Credential);
            await main.OpenExporterCommand.ExecuteAsync(null);var export=main.Exporter!;export.SelectedClient=export.Clients.Single(x=>x.Type==ClientType.SingBox);await export.GenerateCommand.ExecuteAsync(null);Assert.Contains("已生成",export.Message);
            var first=Assert.Single(privacy.ListExports(Path.Combine(dir,"exports")));Assert.Contains("policy-Smart",first.Name);
            export.Policy.ModeIndex=2;export.Policy.AllowLan=false;await export.Policy.SaveDefaultCommand.ExecuteAsync(null);main.CloseExporterCommand.Execute(null);await main.OpenExporterCommand.ExecuteAsync(null);Assert.False(main.Exporter!.Policy.AllowLan);Assert.Equal(2,main.Exporter.Policy.ModeIndex);
            var backup=Path.Combine(dir,"test.nekobak");await privacy.BackupAsync(backup,"fixture-backup-long-password");await store.UpdateAsync(s=>s with{ClientPolicy=new()});await privacy.RestoreAsync(backup,"fixture-backup-long-password",true);Assert.Equal(PrivacyMode.Custom,(await store.LoadAsync()).ClientPolicy.Mode);
            var legacy=Path.Combine(dir,"exports","fixture__node__TrojanTls__SingBox__p443__1234abcd.json");await File.WriteAllTextAsync(legacy,"{}");Assert.Equal(2,privacy.ListExports(Path.Combine(dir,"exports")).Count);privacy.DeleteExport(Path.Combine(dir,"exports"),first);Assert.Single(privacy.ListExports(Path.Combine(dir,"exports")));
        }
        finally{main?.CloseExporterCommand.Execute(null);main?.Settings.Updates.Dispose();Directory.Delete(dir,true);}
    }
    private sealed class Platform:IPlatformActions{public Task CopyAsync(string text)=>Task.CompletedTask;public Task OpenDirectoryAsync(string path)=>Task.CompletedTask;}
    private sealed class IsolatedProtector:ISecretProtector,IDisposable
    {
        private readonly byte[] key=RandomNumberGenerator.GetBytes(32);public string Name=>"isolated test AES, not DPAPI";
        public byte[] Protect(byte[] value){var nonce=RandomNumberGenerator.GetBytes(12);var cipher=new byte[value.Length];var tag=new byte[16];using var aes=new AesGcm(key,16);aes.Encrypt(nonce,value,cipher,tag);return [..nonce,..tag,..cipher];}
        public byte[] Unprotect(byte[] value){var plain=new byte[value.Length-28];using var aes=new AesGcm(key,16);aes.Decrypt(value.AsSpan(0,12),value.AsSpan(28),value.AsSpan(12,16),plain);return plain;}
        public void Dispose()=>CryptographicOperations.ZeroMemory(key);
    }
}
