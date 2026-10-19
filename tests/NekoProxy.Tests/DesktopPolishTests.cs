using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NekoProxy.Core;
using NekoProxy.Infrastructure;
using NekoProxy.Desktop.Localization;
using NekoProxy.Desktop.Onboarding;
using NekoProxy.Desktop.ViewModels;
using Xunit;
namespace NekoProxy.Tests;
public sealed class DesktopPolishTests
{
    [Fact] public void PastedPrivateKeyMaterialCannotBeSavedAsMetadata()
    {
        var editor=new ServerEditorViewModel(){Name="test",Host="203.0.113.10",PrivateKeyPath="-----BEGIN OPENSSH PRIVATE KEY-----\nnot-real-key\n-----END OPENSSH PRIVATE KEY-----"};
        Assert.Null(editor.Build()); Assert.Contains("文件路径",editor.Error); Assert.DoesNotContain("not-real-key",editor.Error);
    }
    [Fact] public void EveryMarkupTranslationKeyExists()
    {
        var directory = new DirectoryInfo(Environment.GetEnvironmentVariable("NEKOPROXY_REPOSITORY_ROOT") ?? AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName,"NekoProxy.sln"))) directory=directory.Parent;
        Assert.NotNull(directory); var hub=new LocalizationHub();
        foreach (var file in Directory.GetFiles(Path.Combine(directory.FullName,"src","NekoProxy.Desktop"),"*.axaml",SearchOption.AllDirectories))
        foreach (Match match in Regex.Matches(File.ReadAllText(file),@"\{l:Tr ([^}]+)\}")) Assert.True(hub.Catalog["zh-Hans"].ContainsKey(match.Groups[1].Value),file+": "+match.Value);
    }
    [Fact] public void EveryApplicationLanguageHasCompleteTextAndMatchingPlaceholders()
    {
        var hub = new LocalizationHub(); var source = hub.Catalog["zh-Hans"];
        Assert.Equal(6, hub.Catalog.Count); Assert.True(source.Count >= 309);
        foreach (var language in hub.Catalog)
        {
            Assert.Equal(source.Keys.Order(), language.Value.Keys.Order());
            foreach (var pair in language.Value) { Assert.False(string.IsNullOrWhiteSpace(pair.Value)); Assert.Equal(Regex.Matches(source[pair.Key], @"\{\d+\}").Select(m => m.Value).Order(), Regex.Matches(pair.Value, @"\{\d+\}").Select(m => m.Value).Order()); }
        }
        Assert.DoesNotContain(hub.Catalog["zh-Hant"].Values, s => s.Contains('你'));
    }
    [Theory][InlineData("en","3 servers")][InlineData("ja","3 サーバー")][InlineData("ru","Серверов: 3")]
    public void DynamicMessagesTranslateAndUserDataStaysIntact(string language,string expected)
    {
        var hub = new LocalizationHub(); hub.Change(language); Assert.Equal(expected, hub.Translate("3 台服务器"));
        Assert.DoesNotContain("步骤", hub.Translate("步骤 2 / 5"));
        Assert.Equal("羽咲的服务器", hub.Translate("羽咲的服务器"));
        Assert.Equal("香港 · 203.0.113.10",hub.TranslatePlaceholder("香港 · 203.0.113.10"));
        Assert.DoesNotContain("位置未设置",hub.TranslatePlaceholder("位置未设置 · 203.0.113.10"));
        Assert.Contains("C:\\my-config.json", hub.Translate("已生成 sing-box 配置：\nC:\\my-config.json"));
    }
    [Fact] public async Task AtomicPreferenceUpdatesDoNotLoseLanguageOrWindowState()
    {
        var dir = Temp(); ISettingsStore store = new JsonSettingsStore(dir);
        await Task.WhenAll(store.UpdateAsync(s => s with { Language = "ja" }), store.UpdateAsync(s => s with { WindowWidth = 960, WindowHeight = 640 }));
        var result = await store.LoadAsync(); Assert.Equal("ja", result.Language); Assert.Equal(960, result.WindowWidth);
    }
    [Fact] public async Task WindowsVaultContainsCiphertextAndRoundTrips()
    {
        var dir = Temp(); var vault = new CredentialVault(dir, TestCredentialStore.Create(dir)); var id = Guid.NewGuid();
        await vault.SetAsync(id, "test-only-credential-秘密");
        Assert.Equal("test-only-credential-秘密", await vault.GetAsync(id));
        Assert.DoesNotContain("test-only-credential", await File.ReadAllTextAsync(Path.Combine(dir,"credentials.json")));
    }
    [Fact] public void BackupAuthenticatesPasswordAndCiphertext()
    {
        var payload = new BackupPayload(1, DateTimeOffset.UtcNow, new(), SampleProfiles.Create(), new() { [Guid.NewGuid().ToString()] = "test-only-client-secret" });
        var bytes = BackupCrypto.Encrypt(payload,"test-password-12345"); Assert.DoesNotContain("test-only-client-secret", Encoding.UTF8.GetString(bytes));
        Assert.Equal(payload.Servers.Count, BackupCrypto.Decrypt(bytes,"test-password-12345").Servers.Count);
        Assert.ThrowsAny<CryptographicException>(() => BackupCrypto.Decrypt(bytes,"wrong-password"));
        var envelope = JsonSerializer.Deserialize<BackupEnvelope>(bytes)!; var cipher = Convert.FromBase64String(envelope.Ciphertext); cipher[0]^=1;
        Assert.ThrowsAny<CryptographicException>(() => BackupCrypto.Decrypt(JsonSerializer.SerializeToUtf8Bytes(envelope with { Ciphertext=Convert.ToBase64String(cipher) }),"test-password-12345"));
    }
    [Fact] public async Task RestoreVerifiesBeforeMutationAndCreatesCheckpointForMergeAndReplace()
    {
        var dir = Temp(); var settings = new JsonSettingsStore(dir); var repo = new JsonServerRepository(dir); var vault = new CredentialVault(dir,TestCredentialStore.Create(dir));
        var service = new LocalPrivacyService(dir,settings,repo,vault); var initial = SampleProfiles.Create(); await repo.SaveAsync(initial); await settings.SaveAsync(new() { Language="ja", ExportDirectory=Path.Combine(dir,"exports") });
        await vault.SetAsync(initial[0].Nodes[0].Id,"backup-test-secret"); var backup = Path.Combine(dir,"test.nekobak"); await service.BackupAsync(backup,"long-test-password");
        var extra = initial[0] with { Id=Guid.NewGuid(), Name="extra" }; await repo.SaveAsync([extra]);
        var original = await File.ReadAllBytesAsync(Path.Combine(dir,"servers.json"));
        await Assert.ThrowsAnyAsync<CryptographicException>(()=>service.RestoreAsync(backup,"bad-password",true));
        Assert.Equal(original,await File.ReadAllBytesAsync(Path.Combine(dir,"servers.json")));
        await service.RestoreAsync(backup,"long-test-password",false); Assert.Equal(4,(await repo.LoadAsync()).Count); Assert.True(Directory.GetDirectories(Path.Combine(dir,"checkpoints")).Length>0);
        await service.RestoreAsync(backup,"long-test-password",true); Assert.Equal(3,(await repo.LoadAsync()).Count); Assert.Equal("backup-test-secret",await vault.GetAsync(initial[0].Nodes[0].Id));
        Assert.Equal(Path.Combine(dir,"exports"),(await settings.LoadAsync()).ExportDirectory);
    }
    [Fact] public async Task ExportDeletionIsLimitedToNekoProxyFilenamesAndDiagnosticsAreAllowlisted()
    {
        var dir = Temp(); var export = Path.Combine(dir,"exports"); Directory.CreateDirectory(export); await File.WriteAllTextAsync(Path.Combine(export,"keep-private.txt"),"unrelated");
        var owned = Path.Combine(export,"s__n__Hysteria2__SingBox__p443__abcdef12.json"); await File.WriteAllTextAsync(owned,"dummy-export");
        var repo = new JsonServerRepository(dir); await repo.SaveAsync(SampleProfiles.Create()); var service = new LocalPrivacyService(dir,new JsonSettingsStore(dir),repo,new(dir,TestCredentialStore.Create(dir)));
        Assert.Single(service.ListExports(export)); Assert.Throws<InvalidOperationException>(()=>service.DeleteExport(export,new("keep",Path.Combine(export,"keep-private.txt"),0)));
        var report = Path.Combine(dir,"diagnostic.json"); await service.ExportDiagnosticsAsync(report); var text = await File.ReadAllTextAsync(report); Assert.DoesNotContain("203.0.113.10",text); Assert.DoesNotContain("PrivateKey",text); Assert.DoesNotContain(dir,text);
        await service.ClearAsync(new(false,true,false,false),export); Assert.True(File.Exists(Path.Combine(export,"keep-private.txt"))); Assert.False(File.Exists(owned)); Assert.Equal(3,(await repo.LoadAsync()).Count);
    }
    [Fact] public async Task DestructiveCommandsRequireConfirmationAndCancelPreservesRecords()
    {
        var dir = Temp(); var repo = new JsonServerRepository(dir); await repo.SaveAsync(SampleProfiles.Create()); var settings=new JsonSettingsStore(dir);
        var service = new LocalPrivacyService(dir,settings,repo,new(dir,TestCredentialStore.Create(dir))); var vm=new PrivacyViewModel(service,settings,new NoPlatform(),()=>Task.CompletedTask) { ClearServers=true };
        vm.RequestClearCommand.Execute(null); Assert.True(vm.HasConfirmation); Assert.Equal(3,(await repo.LoadAsync()).Count);
        vm.CancelConfirmationCommand.Execute(null); await vm.ConfirmCommand.ExecuteAsync(null); Assert.Equal(3,(await repo.LoadAsync()).Count);
        vm.RequestClearCommand.Execute(null); await vm.ConfirmCommand.ExecuteAsync(null); Assert.Empty(await repo.LoadAsync());
    }
    [Fact] public async Task SuccessfulRestoreDoesNotClaimRollbackWhenOnlyUiRefreshFails()
    {
        var dir=Temp(); var repo=new JsonServerRepository(dir); var settings=new JsonSettingsStore(dir);
        await settings.SaveAsync(new(){ExportDirectory=Path.Combine(dir,"exports")}); await repo.SaveAsync(SampleProfiles.Create());
        var service=new LocalPrivacyService(dir,settings,repo,new(dir,TestCredentialStore.Create(dir))); var backup=Path.Combine(dir,"backup.nekobak");
        await service.BackupAsync(backup,"long-test-password"); await repo.SaveAsync([]);
        var vm=new PrivacyViewModel(service,settings,new NoPlatform(),()=>throw new IOException("test refresh failure")){BackupPassword="long-test-password",PickFileAsync=_=>Task.FromResult<string?>(backup)};
        await vm.RequestRestoreCommand.ExecuteAsync(null); Assert.True(vm.HasConfirmation); await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(3,(await repo.LoadAsync()).Count); Assert.Contains("界面刷新失败",vm.Message); Assert.Equal("",vm.BackupPassword);
    }
    private static string Temp() { var dir=Path.Combine(Path.GetTempPath(),"NekoProxy-tests",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); return dir; }
    private sealed class NoPlatform:IPlatformActions { public Task CopyAsync(string text)=>Task.CompletedTask; public Task OpenDirectoryAsync(string path)=>Task.CompletedTask; }
}
