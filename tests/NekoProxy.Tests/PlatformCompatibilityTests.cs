using System.Security.Cryptography;
using System.Text;
using NekoProxy.Core;
using NekoProxy.Infrastructure;
using Xunit;

namespace NekoProxy.Tests;

internal static class TestCredentialStore
{
    // Uses the real OS provider; Windows tests still exercise CurrentUser DPAPI.
    public static ISecretProtector Create(string directory) => PlatformSecretProtector.Create(directory);
}
internal static class TestCorePaths
{
    public static string SingBox(string root) => Environment.GetEnvironmentVariable("NEKOPROXY_SINGBOX_PATH")
        ?? Path.Combine(root, OperatingSystem.IsWindows() ? "sing-box/sing-box-1.14.3-windows-amd64/sing-box.exe" : "sing-box/sing-box");
    public static string Mihomo(string root) => Environment.GetEnvironmentVariable("NEKOPROXY_MIHOMO_PATH")
        ?? Path.Combine(root, OperatingSystem.IsWindows() ? "mihomo/mihomo-windows-amd64.exe" : "mihomo/mihomo");
}

public sealed class PlatformCompatibilityTests
{
    private static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "NekoProxy-platform-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    [Fact] public void LinuxXdgPathsSeparateConfigurationStateDataAndCache()
    {
        var root = Temp();
        var paths = ApplicationDirectories.Linux(root, name => name == "XDG_DATA_HOME" ? Path.Combine(root, "data") : "relative-invalid");
        Assert.Equal(Path.Combine(root,"data","NekoProxy"),paths.Data);
        Assert.Equal(Path.Combine(root,".config","NekoProxy"),paths.Configuration);
        Assert.Equal(Path.Combine(root,".local","state","NekoProxy"),paths.State);
        Assert.Equal(Path.Combine(root,".cache","NekoProxy"),paths.Cache);
    }
    [Fact] public void ExplicitDataDirectoryKeepsIsolationOnEveryPlatform()
    {
        var root=Temp();var paths=ApplicationDirectories.Current(root);
        Assert.Equal(root,paths.Data);Assert.Equal(root,paths.Configuration);Assert.Equal(root,paths.State);
        Assert.Equal(Path.Combine(root,"cache"),paths.Cache);
        Assert.True(Path.IsPathFullyQualified(ApplicationDirectories.DefaultExportDirectory));
    }
    [Fact] public async Task ActualOsVaultSurvivesProviderRecreationAndTamperIsRejected()
    {
        var dir=Temp();var id=Guid.NewGuid();var secret="isolated-native-store-秘密";
        var protector=TestCredentialStore.Create(dir);
        try { await new CredentialVault(dir,protector).SetAsync(id,secret); }
        finally { (protector as IDisposable)?.Dispose(); }
        var text=await File.ReadAllTextAsync(Path.Combine(dir,"credentials.json"));
        Assert.DoesNotContain(secret,text);Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)),text);
        var next=TestCredentialStore.Create(dir);
        try
        {
            Assert.Equal(secret,await new CredentialVault(dir,next).GetAsync(id));
            var cipher=next.Protect(Encoding.UTF8.GetBytes(secret));cipher[^1]^=1;
            Assert.ThrowsAny<Exception>(()=>next.Unprotect(cipher));
            if(OperatingSystem.IsWindows())
            {
                Assert.IsType<WindowsSecretProtector>(next);
                var legacy=new WindowsSecretProtector();
                Assert.Equal(secret,Encoding.UTF8.GetString(next.Unprotect(legacy.Protect(Encoding.UTF8.GetBytes(secret)))));
                Assert.Equal(secret,Encoding.UTF8.GetString(legacy.Unprotect(next.Protect(Encoding.UTF8.GetBytes(secret)))));
            }
        }
        finally { (next as IDisposable)?.Dispose(); }
    }
    [Fact] public async Task BackupRestoreUsesActualSplitConfigurationPathAndPreservesLocalPaths()
    {
        var root=Temp();var data=Path.Combine(root,"data");var config=Path.Combine(root,"config");
        var settings=new JsonSettingsStore(config);var servers=new JsonServerRepository(data);
        var protector=TestCredentialStore.Create(data);
        try
        {
            var vault=new CredentialVault(data,protector);var server=SampleProfiles.Create()[0];
            await servers.SaveAsync([server]);await settings.SaveAsync(new(){Language="ja",ExportDirectory=Path.Combine(root,"exports")});
            await vault.SetAsync(server.Nodes[0].Id,"split-backup-secret");
            var service=new LocalPrivacyService(data,settings,servers,vault,config,Path.Combine(root,"state"),Path.Combine(root,"cache"));
            var backup=Path.Combine(root,"backup.nekobak");await service.BackupAsync(backup,"cross-platform-test-password");
            await settings.SaveAsync(new(){Language="en",ExportDirectory=Path.Combine(root,"local-exports")});
            await service.RestoreAsync(backup,"cross-platform-test-password",false);
            Assert.Equal("ja",(await settings.LoadAsync()).Language);
            Assert.Equal(Path.Combine(root,"local-exports"),(await settings.LoadAsync()).ExportDirectory);
            Assert.False(File.Exists(Path.Combine(data,"settings.json")));
            var checkpoint=Directory.GetDirectories(Path.Combine(data,"checkpoints")).Single();
            Assert.Contains("en",await File.ReadAllTextAsync(Path.Combine(checkpoint,"settings.json")));
            Assert.Equal("split-backup-secret",await vault.GetAsync(server.Nodes[0].Id));
        }
        finally { (protector as IDisposable)?.Dispose(); }
    }
}
