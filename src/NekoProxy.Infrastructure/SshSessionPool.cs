using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using NekoProxy.Core;
namespace NekoProxy.Infrastructure;
public interface IReusableSshSession {bool TransportConnected {get;} string HostFingerprint {get;}}
public interface ISshSessionControl {Task DisconnectAsync(Guid serverId,CancellationToken token=default);}
// One serialized lease per server. A successful login can serve preflight, SNI
// and deployment without repeatedly negotiating WAN connections. Terminal remains
// an independent raw session. Idle transports expire and never send keepalives.
public sealed class SshSessionPool(SshConnectionFactory raw,ISettingsStore settings,ISshSecretSource secrets):ISshConnectionFactory,ISshSessionControl,IDisposable
{
    private sealed class Entry {public readonly SemaphoreSlim Gate=new(1,1);public ISshSession? Session;public CancellationTokenSource? Idle;public string Identity="";public long SecretRevision;public DateTimeOffset Used=DateTimeOffset.MinValue;}
    private readonly ConcurrentDictionary<Guid,Entry> _entries=new();
    private bool _disposed;
    public Task<ISshSession> ConnectAsync(ServerProfile server,TimeSpan timeout,CancellationToken token=default)=>ConnectWithKeyAsync(server,timeout,null,token);
    public async Task<ISshSession> ConnectWithKeyAsync(ServerProfile server,TimeSpan timeout,string? passphrase,CancellationToken token=default)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);var entry=_entries.GetOrAdd(server.Id,_=>new());await entry.Gate.WaitAsync(token);
        try
        {
            entry.Idle?.Cancel();entry.Idle?.Dispose();entry.Idle=null;
            var identity=SshConnectionFactory.Endpoint(server)+"|"+server.Username+"|"+server.Authentication+"|"+server.PrivateKeyPath+"|"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passphrase??"")));
            var pin=(await settings.LoadAsync(token)).KnownHosts.GetValueOrDefault(SshConnectionFactory.Endpoint(server));
            var reusable=entry.Session is IReusableSshSession {TransportConnected:true} transport&&transport.HostFingerprint==pin&&entry.Identity==identity&&entry.SecretRevision==secrets.Revision&&DateTimeOffset.UtcNow-entry.Used<TimeSpan.FromSeconds(45);
            if(!reusable)
            {
                if(entry.Session is {} old)await old.DisposeAsync();entry.Session=null;
                entry.Session=await raw.ConnectWithKeyAsync(server,timeout,passphrase,token);entry.Identity=identity;entry.SecretRevision=secrets.Revision;
            }
            entry.Used=DateTimeOffset.UtcNow;
            // Reuse has its own attempt; no invented new handshake duration.
            var attempt=reusable?raw.Journal.Start(server.Id):entry.Session!.Attempt;
            if(reusable&&attempt is not null){attempt.Begin(ConnectionStage.Session);attempt.End(detail:"复用已认证、指纹仍匹配的 SSH 会话；未重复握手");attempt.Finish(AttemptState.Succeeded);}
            return new Lease(entry,reusable,attempt);
        }
        catch{entry.Gate.Release();throw;}
    }
    public async Task DisconnectAsync(Guid serverId,CancellationToken token=default)
    {
        if(!_entries.TryGetValue(serverId,out var entry))return;await entry.Gate.WaitAsync(token);
        try{entry.Idle?.Cancel();if(entry.Session is {} session)await session.DisposeAsync();entry.Session=null;}finally{entry.Gate.Release();}
    }
    private static async Task ExpireAsync(Entry entry,CancellationToken token)
    {
        try{await Task.Delay(TimeSpan.FromSeconds(45),token);await entry.Gate.WaitAsync(token);try{if(!token.IsCancellationRequested&&entry.Session is {} session){await session.DisposeAsync();entry.Session=null;}}finally{entry.Gate.Release();}}
        catch(OperationCanceledException){}
    }
    private sealed class Lease(Entry entry,bool reused,ConnectionAttempt? attempt):ISshSession
    {
        private bool _disposed;
        public TimeSpan? ConnectionElapsed=>reused?null:entry.Session?.ConnectionElapsed;
        public ConnectionAttempt? Attempt=>attempt;
        public Task<string> ExecuteAsync(string command,CancellationToken token=default)=>entry.Session!.ExecuteAsync(command,token);
        public async Task<string> ExecuteEnvironmentAsync(string command,CancellationToken token=default)
        {
            if(!reused)return await entry.Session!.ExecuteEnvironmentAsync(command,token);
            attempt?.Begin(ConnectionStage.Environment);
            try{var result=await entry.Session!.ExecuteEnvironmentAsync(command,token);attempt?.End(detail:"只读环境命令退出码 0",exitCode:0);return result;}
            catch(Exception e){attempt?.End(e is OperationCanceledException?AttemptState.Cancelled:AttemptState.Failed,e is SshConnectionException safe?safe.Message:"环境命令未完成");attempt?.Finish(AttemptState.Failed);throw;}
        }
        public Task UploadAsync(Stream data,string path,CancellationToken token=default)=>entry.Session!.UploadAsync(data,path,token);
        public ValueTask DisposeAsync(){if(!_disposed){_disposed=true;entry.Used=DateTimeOffset.UtcNow;var idle=new CancellationTokenSource();entry.Idle=idle;entry.Gate.Release();_=ExpireAsync(entry,idle.Token);}return ValueTask.CompletedTask;}
    }
    public void Dispose(){if(_disposed)return;_disposed=true;foreach(var entry in _entries.Values){entry.Idle?.Cancel();entry.Idle?.Dispose();if(entry.Session is {} session)session.DisposeAsync().AsTask().GetAwaiter().GetResult();}}
}
