using System.Text;
using System.Text.Json;
using NekoProxy.Core;
using NekoProxy.Infrastructure;
using Xunit;
namespace NekoProxy.Tests;
public sealed class LocalSshFactAttribute:FactAttribute
{
    public LocalSshFactAttribute(){if(Environment.GetEnvironmentVariable("NEKOPROXY_SSH_FIXTURE") is null)Skip="Start the isolated localhost SSH fixture; see tests/fixtures.";}
}
public sealed class SshIntegrationTests
{
    private static JsonElement Info=>JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("NEKOPROXY_SSH_FIXTURE")!)).RootElement;
    private static ServerProfile Server(bool encrypted=false,string? user=null)=>new(Guid.NewGuid(),"LOCAL SSH TEST",Info.GetProperty("host").GetString()!,"","",Info.GetProperty("port").GetInt32(),user??Info.GetProperty("username").GetString()!,Info.GetProperty(encrypted?"encryptedKeyPath":"keyPath").GetString(),[]);
    private static JsonSettingsStore Store()=>new(Path.Combine(Path.GetTempPath(),"NekoProxy-SSH-tests",Guid.NewGuid().ToString("N")));
    [LocalSshFact]public async Task ActualSshWireNormalizesWindowsNewlinesBeforePosixExecution()
    {
        var factory=new SshConnectionFactory(Info.GetProperty("fingerprint").GetString()!);
        await using var session=await factory.ConnectAsync(Server(),TimeSpan.FromSeconds(3));
        Assert.Equal("LINE1LINE2",await session.ExecuteAsync("printf 'LINE1'\r\nprintf 'LINE2'"));
    }
    [LocalSshFact]public async Task PasswordAuthenticationAndFragmentedInteractiveUtf8UseIndependentSessions()
    {
        var server=Server() with{Authentication=SshAuthenticationKind.Password,PrivateKeyPath=null};
        var factory=new SshConnectionFactory(Info.GetProperty("fingerprint").GetString()!,new Secret());
        await using var terminal=await factory.OpenTerminalAsync(server,TimeSpan.FromSeconds(3));var output=new StringBuilder();var received=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        terminal.Output+=text=>{output.Append(text);if(output.ToString().Contains("NEKOPROXY-TERMINAL-TEST 中文"))received.TrySetResult();};terminal.StartReading();await terminal.WriteAsync("fixture-test\r");await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var independent=await factory.ConnectAsync(server,TimeSpan.FromSeconds(3));await terminal.DisposeAsync();
        Assert.Equal("NEKOPROXY-SSH-TEST",await independent.ExecuteAsync("printf 'NEKOPROXY-SSH-TEST'"));Assert.DoesNotContain('\uFFFD',output.ToString());
    }
    private sealed class Secret:ISshSecretSource
    {
        public Task<string?> RequestAsync(ServerProfile server,CancellationToken token)=>Task.FromResult<string?>("local-fixture-password");
        public void SetSessionSecret(ServerProfile server,string secret){}public void Clear(){}
    }
    [LocalSshFact]public async Task RealAuthenticationCommandAndSftpUseConfirmedCachedFingerprint()
    {
        var store=Store();var approval=new Approval(true);var factory=new SshConnectionFactory(store,approval);var server=Server();
        await using(var session=await factory.ConnectAsync(server,TimeSpan.FromSeconds(3)))
        {
            Assert.Equal("NEKOPROXY-SSH-TEST",await session.ExecuteAsync("printf 'NEKOPROXY-SSH-TEST'"));
            using var content=new MemoryStream(Encoding.UTF8.GetBytes("local-test-only-payload"));await session.UploadAsync(content,"/tmp/nekoproxy.fixturetest/payload.bin");
            Assert.Equal("local-test-only-payload",await File.ReadAllTextAsync(Info.GetProperty("uploadedFile").GetString()!));
        }
        Assert.Equal(1,approval.Calls);Assert.Equal(Info.GetProperty("fingerprint").GetString(),approval.Key!.Fingerprint);Assert.NotEmpty(approval.Key.Algorithm);
        Assert.Equal(approval.Key.Fingerprint,(await store.LoadAsync()).KnownHosts[SshConnectionFactory.Endpoint(server)]);
        await using var second=await factory.ConnectAsync(server,TimeSpan.FromSeconds(3));Assert.Equal(1,approval.Calls);
        Assert.DoesNotContain("PRIVATE KEY",JsonSerializer.Serialize(await store.LoadAsync()));
    }
    [LocalSshFact]public async Task RejectedAndChangedKeysFailBeforeAuthenticationAndDoNotUpdateTrust()
    {
        var store=Store();var rejected=new SshConnectionFactory(store,new Approval(false));
        var denied=await Assert.ThrowsAsync<SshConnectionException>(()=>rejected.ConnectAsync(Server(),TimeSpan.FromSeconds(3)));Assert.Equal(SshFailureKind.HostRejected,denied.Kind);Assert.Empty((await store.LoadAsync()).KnownHosts);
        var server=Server();await store.UpdateAsync(s=>s with{KnownHosts=new(){[SshConnectionFactory.Endpoint(server)]="SHA256:changed-fixture-key"}});
        var approval=new Approval(true);var changed=new SshConnectionFactory(store,approval);var error=await Assert.ThrowsAsync<SshConnectionException>(()=>changed.ConnectAsync(server,TimeSpan.FromSeconds(3)));
        Assert.Equal(SshFailureKind.HostChanged,error.Kind);Assert.Equal(0,approval.Calls);Assert.Equal("SHA256:changed-fixture-key",(await store.LoadAsync()).KnownHosts.Values.Single());
    }
    [LocalSshFact]public async Task EncryptedKeyWorksAndBadAuthenticationIsClassified()
    {
        var store=Store();var factory=new SshConnectionFactory(store,new Approval(true));var server=Server(true);
        var missing=await Assert.ThrowsAsync<SshConnectionException>(()=>factory.ConnectAsync(server,TimeSpan.FromSeconds(3)));Assert.Equal(SshFailureKind.KeyFile,missing.Kind);
        await using var session=await factory.ConnectWithKeyAsync(server,TimeSpan.FromSeconds(3),"local-fixture-passphrase");
        Assert.Equal("NEKOPROXY-SSH-TEST",await session.ExecuteAsync("printf 'NEKOPROXY-SSH-TEST'"));
        Assert.DoesNotContain("local-fixture-passphrase",JsonSerializer.Serialize(await store.LoadAsync()));
        var failed=await Assert.ThrowsAsync<SshConnectionException>(()=>factory.ConnectAsync(Server(user:"wrong-user"),TimeSpan.FromSeconds(3)));Assert.Equal(SshFailureKind.Authentication,failed.Kind);
    }
    [LocalSshFact]public async Task StalledLocalSocketTimesOutAndErrorsNeverContainKeyMaterial()
    {
        var server=Server() with{SshPort=Info.GetProperty("stallPort").GetInt32()};var factory=new SshConnectionFactory(Info.GetProperty("fingerprint").GetString()!);
        var error=await Assert.ThrowsAsync<SshConnectionException>(()=>factory.ConnectAsync(server,TimeSpan.FromSeconds(1)));Assert.Equal(SshFailureKind.Timeout,error.Kind);
        Assert.DoesNotContain("PRIVATE KEY",error.ToString());Assert.DoesNotContain(server.PrivateKeyPath!,error.ToString());
    }
    [LocalSshFact]public async Task EncryptedKeyCanUseTransientPromptSourceWithoutSavingPassphrase()
    {
        var store=Store();var source=new KeySecret();var factory=new SshConnectionFactory(store,new Approval(true),source);
        await using var session=await factory.ConnectAsync(Server(true),TimeSpan.FromSeconds(3));
        Assert.Equal("NEKOPROXY-SSH-TEST",await session.ExecuteAsync("printf 'NEKOPROXY-SSH-TEST'"));Assert.Equal(1,source.Requests);
        Assert.DoesNotContain("local-fixture-passphrase",JsonSerializer.Serialize(await store.LoadAsync()));
    }
    private sealed class KeySecret:ISshSecretSource
    {
        public int Requests;
        public Task<string?> RequestAsync(ServerProfile server,CancellationToken token){Requests++;return Task.FromResult<string?>("local-fixture-passphrase");}
        public void SetSessionSecret(ServerProfile server,string secret){}public void Clear(){}
    }
    private sealed class Approval(bool accept):IHostKeyApproval
    {
        public int Calls;public HostKeyCandidate? Key;
        public Task<bool> ConfirmAsync(HostKeyCandidate key,CancellationToken token){Calls++;Key=key;return Task.FromResult(accept);}
    }
}
