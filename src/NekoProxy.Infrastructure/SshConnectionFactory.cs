using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using NekoProxy.Core;
using Renci.SshNet;
using Renci.SshNet.Common;
using SshConnectionException=NekoProxy.Core.SshConnectionException;
namespace NekoProxy.Infrastructure;

public sealed class SshConnectionFactory : ISshConnectionFactory,IInteractiveSshFactory
{
    private readonly ISettingsStore? _settings;
    private readonly IHostKeyApproval? _approval;
    private readonly string? _fixedFingerprint;
    private readonly ISshSecretSource? _secrets;
    private readonly SemaphoreSlim _connectGate=new(2,2);
    public ConnectionJournal Journal {get;}=new();
    public SshConnectionFactory(ISettingsStore settings,IHostKeyApproval approval){_settings=settings;_approval=approval;}
    public SshConnectionFactory(ISettingsStore settings,IHostKeyApproval approval,ISshSecretSource secrets):this(settings,approval)=>_secrets=secrets;
    public SshConnectionFactory(string expectedSha256Fingerprint)=>_fixedFingerprint=expectedSha256Fingerprint;
    public SshConnectionFactory(string expectedSha256Fingerprint,ISshSecretSource secrets):this(expectedSha256Fingerprint)=>_secrets=secrets;
    public static string Endpoint(ServerProfile server)=>(IPAddress.TryParse(server.Host,out var ip)?ip.ToString():server.Host.Trim().TrimEnd('.').ToLowerInvariant())+"|"+server.SshPort;
    public Task<ISshSession> ConnectAsync(ServerProfile server,TimeSpan timeout,CancellationToken token=default)=>ConnectWithKeyAsync(server,timeout,null,token);
    public Task<ISshSession> ConnectWithKeyAsync(ServerProfile server,TimeSpan timeout,string? passphrase,CancellationToken token=default)=>
        Task.Run(()=>ConnectWithKeyCoreAsync(server,timeout,passphrase,token),token);
    private async Task<ISshSession> ConnectWithKeyCoreAsync(ServerProfile server,TimeSpan timeout,string? passphrase,CancellationToken token)
    {
        try{return await ConnectAttemptAsync(server,timeout,passphrase,token);}
        catch(SshConnectionException e) when(e.Kind is SshFailureKind.Timeout or SshFailureKind.Network && !token.IsCancellationRequested)
        {
            // Only a transport failure is eligible. Each attempt stays in the
            // journal; never retry credentials, rejected trust or a changed key.
            await Task.Delay(600,token);
            return await ConnectAttemptAsync(server,timeout,passphrase,token);
        }
    }
    private async Task<ISshSession> ConnectAttemptAsync(ServerProfile server,TimeSpan timeout,string? passphrase,CancellationToken token)
    {
        var attempt=Journal.Start(server.Id);PrivateKeyFile? key=null;SshClient? client=null;var entered=false;
        try
        {
            attempt.Begin(ConnectionStage.Input);
            var validation=ProfileValidation.Validate(server);if(validation is not null)throw new ArgumentException(validation);
            if(timeout<=TimeSpan.Zero)throw new ArgumentException("连接超时必须大于零。");
            if(_settings is null&&string.IsNullOrWhiteSpace(_fixedFingerprint))throw new SshConnectionException(SshFailureKind.HostRejected,"请先验证服务器主机密钥指纹。");
            AuthenticationMethod authentication;
            if(server.Authentication==SshAuthenticationKind.Password)
            {
                var secret=!string.IsNullOrEmpty(passphrase)?passphrase:(_secrets is null?null:await _secrets.RequestAsync(server,token));
                if(string.IsNullOrEmpty(secret))throw new SshConnectionException(SshFailureKind.Authentication,"请输入本次连接的 SSH 登录密码。");
                authentication=new PasswordAuthenticationMethod(server.Username,secret);
            }
            else
            {
                ValidateKeyPath(server.PrivateKeyPath);
                try{key=string.IsNullOrEmpty(passphrase)?new PrivateKeyFile(server.PrivateKeyPath!):new PrivateKeyFile(server.PrivateKeyPath!,passphrase);}
                catch(SshPassPhraseNullOrEmptyException)
                {
                    var secret=_secrets is null?null:await _secrets.RequestAsync(server,token);
                    if(string.IsNullOrEmpty(secret))throw new SshConnectionException(SshFailureKind.KeyFile,"这份私钥需要口令，请填写私钥口令。");
                    try{key=new PrivateKeyFile(server.PrivateKeyPath!,secret);}catch{_secrets?.Forget(server);throw new SshConnectionException(SshFailureKind.KeyFile,"无法读取私钥，请检查格式、口令和文件权限。");}
                }
                catch{throw new SshConnectionException(SshFailureKind.KeyFile,"无法读取私钥，请检查格式、口令和文件权限。");}
                // SSH.NET exposes RSA's legacy SHA-1 algorithm first. Some
                // servers disconnect on that first unsupported offer, before
                // SHA-2 can be tried. Prefer the existing SHA-2 signers without
                // changing the key, host pin, KEX or server authentication policy.
                authentication=new PrivateKeyAuthenticationMethod(server.Username,new PreferredKeySource(key));
            }
            attempt.End(detail:"输入有效；认证资料仅在会话内使用");
            var endpoint=Endpoint(server);
            var cached=_fixedFingerprint??(_settings is null?null:(await _settings.LoadAsync(token)).KnownHosts.GetValueOrDefault(endpoint));
            if(!IPAddress.TryParse(server.Host,out _))
            {
                attempt.Begin(ConnectionStage.Dns);using var dnsDeadline=CancellationTokenSource.CreateLinkedTokenSource(token);dnsDeadline.CancelAfter(timeout);
                var addresses=await Dns.GetHostAddressesAsync(server.Host,dnsDeadline.Token);
                if(addresses.Length==0)throw new SocketException((int)SocketError.HostNotFound);
                attempt.End(detail:$"获得 {addresses.Length} 个地址；SSH 将按系统解析连接");
            }
            else{attempt.Begin(ConnectionStage.Dns);attempt.End(AttemptState.Skipped,"目标为 IP，无需 DNS");}
            await _connectGate.WaitAsync(token);entered=true;
            attempt.Begin(ConnectionStage.Tcp);
            using(var tcp=new TcpClient())using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))
            {deadline.CancelAfter(timeout);await tcp.ConnectAsync(server.Host,server.SshPort,deadline.Token);}
            attempt.End(detail:"独立 TCP 预连接成功；不代表 SSH 认证成功");
            if(string.IsNullOrEmpty(cached))
            {
                // Fetch only a host key; reject it before any authentication. The
                // asynchronous UI decision runs after this transport is disposed.
                attempt.Begin(ConnectionStage.ServerBanner,"首次获取主机密钥，不发送认证资料");
                using var probe=new SshClient(new ConnectionInfo(server.Host,server.SshPort,server.Username,new PasswordAuthenticationMethod(server.Username,"")){Timeout=timeout});
                probe.ServerIdentificationReceived+=(_,_)=>{attempt.End(detail:"收到 SSH 服务版本 banner");attempt.Begin(ConnectionStage.Negotiation,"协商密钥交换");};
                HostKeyCandidate? candidate=null;
                probe.HostKeyReceived+=(_,args)=>{candidate=new(server.Host,server.SshPort,args.HostKeyName,Fingerprint(args.HostKey));args.CanTrust=false;};
                using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    deadline.CancelAfter(timeout);
                    try{await Task.Run(()=>probe.ConnectAsync(deadline.Token),deadline.Token);}
                    catch when(candidate is not null&&!token.IsCancellationRequested){}
                }
                if(candidate is null)throw new SshConnectionException(SshFailureKind.Network,"没有收到服务器主机密钥。");
                probe.Dispose();
                attempt.End(detail:"已收到主机密钥，认证尚未开始");
                attempt.Begin(ConnectionStage.HostKey,"等待用户核实；不占用网络超时预算");
                // Do not monopolize the connection gate while the user reads.
                _connectGate.Release();entered=false;
                if(_approval is null||!await _approval.ConfirmAsync(candidate,token))throw new SshConnectionException(SshFailureKind.HostRejected,"没有确认服务器指纹，连接已取消。");
                cached=candidate.Fingerprint;attempt.End(detail:"用户确认完成；下一次握手仍需精确匹配");
                if(_settings is not null)await _settings.UpdateAsync(s=>s with{KnownHosts=new(s.KnownHosts){[endpoint]=cached}});
                await _connectGate.WaitAsync(token);entered=true;
            }
            var connection=new ConnectionInfo(server.Host,server.SshPort,server.Username,authentication){Timeout=timeout};
            client=new SshClient(connection);string? observed=null;var changed=false;
            attempt.Begin(ConnectionStage.ServerBanner,"SSH.NET 实际连接；等待服务版本 banner");
            client.ServerIdentificationReceived+=(_,_)=>{attempt.End(detail:"收到 SSH 服务版本 banner");attempt.Begin(ConnectionStage.Negotiation,"SSH.NET 密钥交换；边界取自协议事件");};
            client.HostKeyReceived+=(_,args)=>
            {
                attempt.End(detail:"到达主机密钥事件 · KEX="+connection.CurrentKeyExchangeAlgorithm);attempt.Begin(ConnectionStage.HostKey);
                observed=Fingerprint(args.HostKey);args.CanTrust=observed==cached;changed=!args.CanTrust;
                attempt.End(args.CanTrust?AttemptState.Succeeded:AttemptState.Failed,args.CanTrust?"主机指纹精确匹配":"主机指纹发生变化");
                if(args.CanTrust)attempt.Begin(ConnectionStage.Authentication,"指纹通过；等待身份认证与会话就绪");
            };
            var timing=System.Diagnostics.Stopwatch.StartNew();
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))
            {deadline.CancelAfter(timeout);try{await Task.Run(()=>client.ConnectAsync(deadline.Token),deadline.Token);}catch when(changed){throw new SshConnectionException(SshFailureKind.HostChanged,"服务器指纹与上次不同，已停止连接。请先核实服务器是否更换了密钥。");}}
            timing.Stop();attempt.End(detail:"身份认证通过");attempt.Begin(ConnectionStage.Session);attempt.End(detail:"SSH 会话已建立");attempt.Finish(AttemptState.Succeeded);
            if(_settings is not null&&observed is not null&&!(await _settings.LoadAsync(token)).KnownHosts.ContainsKey(endpoint))
                await _settings.UpdateAsync(s=>s with{KnownHosts=new(s.KnownHosts){[endpoint]=observed}});
            return new Session(client,key,observed!,timeout,timing.Elapsed,attempt);
        }
        catch(Exception e)
        {
            client?.Dispose();key?.Dispose();
            if(token.IsCancellationRequested){attempt.End(AttemptState.Cancelled,"用户取消");attempt.Finish(AttemptState.Cancelled);throw new OperationCanceledException(token);}
            var kind=e is SshConnectionException known?known.Kind:e is SshAuthenticationException or Renci.SshNet.Common.SshConnectionException{DisconnectReason:Renci.SshNet.Messages.Transport.DisconnectReason.NoMoreAuthenticationMethodsAvailable}?SshFailureKind.Authentication:e is OperationCanceledException or SshOperationTimeoutException or TimeoutException or SocketException{SocketErrorCode:SocketError.TimedOut}?SshFailureKind.Timeout:SshFailureKind.Network;
            if(kind==SshFailureKind.Authentication)_secrets?.Forget(server);
            var underlying=e;while(underlying.InnerException is {} inner)underlying=inner;
            var facts=underlying.GetType().Name+(underlying is SocketException socket?$" · SocketError={socket.SocketErrorCode}":"");
            var reason=e is SshConnectionException safe?safe.Message:kind==SshFailureKind.Authentication?"SSH 认证失败，请检查用户名和认证资料。":kind==SshFailureKind.Timeout?"连接超时，可直接重试。":underlying is SocketException {SocketErrorCode:SocketError.ConnectionRefused}?"服务器拒绝连接，请检查监听服务。":underlying is SocketException {SocketErrorCode:SocketError.HostNotFound}?"DNS 解析失败，请核对域名。":"连接未完成，请查看脱敏阶段记录。";
            attempt.End(AttemptState.Failed,reason,facts);attempt.Finish(AttemptState.Failed);
            var label=attempt.Records.LastOrDefault()?.Label??"输入检查";
            throw new SshConnectionException(kind,$"{label}：{reason}（尝试 {attempt.Id.ToString("N")[..8]}）"){Attempt=attempt};
        }
        finally{if(entered)_connectGate.Release();}
    }
    private sealed class PreferredKeySource(IPrivateKeySource key) : IPrivateKeySource
    {
        public IReadOnlyCollection<Renci.SshNet.Security.HostAlgorithm> HostKeyAlgorithms {get;} = key.HostKeyAlgorithms
            .OrderBy(a=>a.Name switch {"rsa-sha2-512"=>0,"rsa-sha2-256"=>1,"ssh-rsa"=>3,_=>2}).ToArray();
    }
    public static void ValidateKeyPath(string? path)
    {
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))throw new SshConnectionException(SshFailureKind.KeyFile,"请选择存在的 SSH 私钥文件。");
        if(new FileInfo(path).Length is 0 or >1048576)throw new SshConnectionException(SshFailureKind.KeyFile,"私钥文件大小不正常，请重新选择。");
    }
    private static string Fingerprint(byte[] bytes)=>"SHA256:"+Convert.ToBase64String(SHA256.HashData(bytes)).TrimEnd('=');
    public static string NormalizeShellCommand(string command)=>command.Replace("\r\n","\n");
    public async Task<IInteractiveSshSession> OpenTerminalAsync(ServerProfile server,TimeSpan timeout,CancellationToken token=default)
    {
        var owned=(Session)await ConnectAsync(server,timeout,token);
        try{return new Terminal(owned,owned.Client.CreateShellStream("xterm",80,24,0,0,65536));}
        catch{await owned.DisposeAsync();throw new SshConnectionException(SshFailureKind.Command,"无法打开 SSH 终端，请检查服务器是否允许交互会话。");}
    }
    private sealed class Terminal(Session owned,ShellStream stream):IInteractiveSshSession
    {
        private readonly System.Text.Decoder _decoder=System.Text.Encoding.UTF8.GetDecoder();
        private bool _disposed;
        private bool _reading;
        public event Action<string>? Output;
        public event Action? Closed;
        private int _closed;
        private readonly object _sync=new();
        public async Task WriteAsync(string input,CancellationToken token=default)
        {
            if(_disposed)throw new ObjectDisposedException(nameof(Terminal));
            await Task.Run(()=>{token.ThrowIfCancellationRequested();stream.Write(input);stream.Flush();},token);
        }
        public void Resize(uint columns,uint rows){if(!_disposed)stream.ChangeWindowSize(Math.Clamp(columns,20,240),Math.Clamp(rows,8,100),0,0);}
        public void StartReading()
        {
            lock(_sync){if(_reading||_disposed)return;_reading=true;stream.DataReceived+=Received;stream.Closed+=StreamClosed;stream.ErrorOccurred+=StreamClosed;owned.Client.ErrorOccurred+=StreamClosed;Drain();}
        }
        private void StreamClosed(object? sender,EventArgs e){if(Interlocked.Exchange(ref _closed,1)==0)Closed?.Invoke();}
        private void Received(object? sender,Renci.SshNet.Common.ShellDataEventArgs e)
        {lock(_sync){if(!_disposed)Drain();}}
        private void Drain()
        {
            var bytes=new byte[4096];var chars=new char[4098];
            while(stream.DataAvailable){var n=stream.Read(bytes,0,bytes.Length);if(n==0)break;var count=_decoder.GetChars(bytes,0,n,chars,0,false);if(count>0)Output?.Invoke(new string(chars,0,count));}
        }
        public async ValueTask DisposeAsync(){lock(_sync){if(_disposed)return;_disposed=true;stream.DataReceived-=Received;stream.Closed-=StreamClosed;stream.ErrorOccurred-=StreamClosed;owned.Client.ErrorOccurred-=StreamClosed;stream.Dispose();}await owned.DisposeAsync();}
    }
    private static string SafeError(string text)
    {
        // Arbitrary remote output can include secrets even without a key label.
        // Expose only a short allowlist of error lines, never command/config text.
        var allowed=new[]{"Permission denied","command not found","No such file or directory","Address already in use","Operation not permitted","Connection refused","Connection timed out"};
        var found=allowed.Where(x=>text.Contains(x,StringComparison.OrdinalIgnoreCase));
        return string.Join(" · ",found.DefaultIfEmpty("标准错误包含非结构化输出，分享诊断已隐藏；可在终端检查。"));
    }
    private sealed class Session(SshClient client,PrivateKeyFile? key,string fingerprint,TimeSpan timeout,TimeSpan elapsed,ConnectionAttempt attempt):ISshSession,IReusableSshSession
    {
        public bool TransportConnected=>client.IsConnected;
        public string HostFingerprint=>fingerprint;
        public TimeSpan? ConnectionElapsed=>elapsed;
        public ConnectionAttempt Attempt=>attempt;
        public async Task<string> ExecuteEnvironmentAsync(string command,CancellationToken token=default)
        {
            attempt.Begin(ConnectionStage.Environment);try{var text=await ExecuteBoundedAsync(command,timeout,token);attempt.End(detail:"只读环境命令退出码 0",exitCode:0);return text;}catch(SshConnectionException e){attempt.End(AttemptState.Failed,e.SafeStandardError??e.Message,e.Kind.ToString(),e.ExitCode);attempt.Finish(AttemptState.Failed);throw;}catch(OperationCanceledException){attempt.End(AttemptState.Cancelled,"环境检查取消");attempt.Finish(AttemptState.Cancelled);throw;}
        }
        internal SshClient Client=>client;
        private SftpClient? _sftp;
        public Task<string> ExecuteAsync(string command,CancellationToken token=default)=>ExecuteBoundedAsync(command,TimeSpan.FromMinutes(4),token);
        private async Task<string> ExecuteBoundedAsync(string command,TimeSpan budget,CancellationToken token)
        {
            using var operation=client.CreateCommand(NormalizeShellCommand(command));operation.CommandTimeout=budget;
            try{await operation.ExecuteAsync(token);}
            catch(SshOperationTimeoutException){throw new SshConnectionException(SshFailureKind.Timeout,"远程命令超时，SSH 登录已完成。"){Attempt=attempt};}
            catch(Exception e)when(e is Renci.SshNet.Common.SshException or SocketException or IOException){throw new SshConnectionException(SshFailureKind.Network,"远程 SSH 会话中断；登录曾完成，可重新连接后重试。"){Attempt=attempt};}
            if(operation.ExitStatus!=0)throw new SshConnectionException(SshFailureKind.Command,$"远程命令失败，退出码 {operation.ExitStatus}。请查看脱敏诊断详情。"){Attempt=attempt,ExitCode=operation.ExitStatus,SafeStandardError=SafeError(operation.Error)};
            return operation.Result.Length>65536?operation.Result[..65536]:operation.Result;
        }
        public async Task UploadAsync(Stream data,string path,CancellationToken token=default)
        {
            if(_sftp is null)
            {
                _sftp=new(client.ConnectionInfo);_sftp.OperationTimeout=timeout;
                _sftp.HostKeyReceived+=(_,args)=>args.CanTrust=Fingerprint(args.HostKey)==fingerprint;
                await _sftp.ConnectAsync(token);
            }
            await _sftp.UploadFileAsync(data,path,token);
        }
        public ValueTask DisposeAsync(){_sftp?.Dispose();client.Dispose();key?.Dispose();return ValueTask.CompletedTask;}
    }
}
