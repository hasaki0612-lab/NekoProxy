using System.Diagnostics;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using NekoProxy.Core;
namespace NekoProxy.Deployment;

public sealed class RemoteAdministration(ISshConnectionFactory connections):IRemoteAdministration
{
    public async Task<RemoteToolResult> TestSshAsync(ServerProfile server,TimeSpan timeout,CancellationToken token=default)
    {
            try{await using var session=await connections.ConnectAsync(server,timeout,token);return new(true,"SSH 连接已验证",session.ConnectionElapsed is {} elapsed?$"{elapsed.TotalMilliseconds:F0} ms · SSH 握手与认证耗时，不是代理协议延迟。":"复用已认证 SSH 会话，本次没有新的握手耗时。","SSH");}
        catch(OperationCanceledException){throw;}catch(SshConnectionException e){return new(false,e.Message,"",e.Kind.ToString());}
    }
    public async Task<RemoteToolResult> TestTcpAsync(ServerProfile server,int port,CancellationToken token=default)
    {
        if(port is <1 or >65535)throw new ArgumentException("请输入有效的 TCP 端口。");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try{using var client=new TcpClient();var watch=Stopwatch.StartNew();await client.ConnectAsync(server.Host,port,deadline.Token);return new(true,"TCP 端口可以连接",$"{watch.Elapsed.TotalMilliseconds:F0} ms · 只验证 TCP 建连，不证明代理认证或 UDP 可用。","TCP");}
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return new(false,"TCP 连接超时","检查服务器防火墙与监听端口。","Timeout");}
        catch(SocketException){return new(false,"TCP 端口无法连接","检查网络、监听服务与防火墙。","Network");}
    }
    public async Task<RemoteToolResult> RunAsync(ServerProfile server,ProxyNode? node,RemoteServiceAction action,TimeSpan timeout,CancellationToken token=default)
    {
        try
        {
            await using var session=await connections.ConnectAsync(server,timeout,token);
            if(action==RemoteServiceAction.Environment)
            {
                var text=await session.ExecuteAsync(". /etc/os-release; printf 'OS=%s\\nARCH=%s\\n' \"$PRETTY_NAME\" \"$(uname -m)\"; if [ -d /run/systemd/system ]; then printf 'systemd=available\\n'; fi; command -v sing-box >/dev/null && sing-box version | head -n1 || true; ss -lntup 2>/dev/null; systemctl list-units --type=service --state=running --no-pager --no-legend",token);
                return new(true,"环境信息已读取",Redact(text),"Environment");
            }
            if(node is null||node.ServiceUnit is null||node.ServiceUnit!=new RemotePaths(node.Id).Unit)return new(false,"只能管理本机已记录的 NekoProxy 节点服务。","请先部署此节点，或核实原来的服务记录。","Ownership");
            var paths=new RemotePaths(node.Id);var q=RemoteCommands.Quote;
            var permission=await session.ExecuteAsync("if [ \"$(id -u)\" = 0 ] || sudo -n true 2>/dev/null; then printf 'PERMISSION=1'; else printf 'PERMISSION=0'; fi",token);
            if(!permission.Contains("PERMISSION=1",StringComparison.Ordinal))return new(false,"此操作需要 root 或免密 sudo 权限。","","Permission");
            var guard="set -eu; if ! { test ! -L "+q(paths.Directory)+" && test ! -L "+q(paths.UnitFile)+" && grep -qx "+q(paths.Owner)+" "+q(paths.Directory+"/.owner")+" && grep -qx "+q("# "+paths.Owner)+" "+q(paths.UnitFile)+"; }; then printf 'NEKOPROXY_FAILURE=Ownership'; exit 0; fi; ";
            string command=action switch
            {
                RemoteServiceAction.Start=>"systemctl start "+q(paths.Unit)+" && systemctl is-active --quiet "+q(paths.Unit)+" && printf 'SERVICE=active\\n'",
                RemoteServiceAction.Stop=>"systemctl stop "+q(paths.Unit)+" && state=$(systemctl is-active "+q(paths.Unit)+" || true) && printf 'SERVICE=%s\\n' \"$state\" && test \"$state\" = inactive",
                RemoteServiceAction.Restart=>"systemctl restart "+q(paths.Unit)+" && sleep 2 && systemctl is-active --quiet "+q(paths.Unit)+" && printf 'SERVICE=active\\n'",
                RemoteServiceAction.CheckConfiguration=>q(paths.Binary)+" check -c "+q(paths.Configuration)+" >/dev/null 2>&1 && printf 'CONFIGURATION=valid\\n'",
                RemoteServiceAction.RecentLogs=>"journalctl -u "+q(paths.Unit)+" -n 80 --no-pager -o short-iso",
                RemoteServiceAction.Certificate=>"journalctl -u "+q(paths.Unit)+" -n 100 --no-pager -o short-iso | grep -Ei 'certificate|renew|acme|tls' || true",
                _=>throw new ArgumentException("操作无效。")
            };
            var failure=action==RemoteServiceAction.CheckConfiguration?"Configuration":"Service";
            var result=await session.ExecuteAsync(RemoteCommands.Privileged(guard+"( "+command+" ) || printf 'NEKOPROXY_FAILURE="+failure+"'"),token);
            if(result.Contains("NEKOPROXY_FAILURE=Ownership",StringComparison.Ordinal))return new(false,"远程服务归属校验失败，操作已停止。","","Ownership");
            if(result.Contains("NEKOPROXY_FAILURE=Configuration",StringComparison.Ordinal))return new(false,"服务端配置检查未通过。","请检查节点参数与 TLS 证书文件。","Configuration");
            if(result.Contains("NEKOPROXY_FAILURE=Service",StringComparison.Ordinal))return new(false,"systemd 服务未达到预期状态。","请查看此节点的最近日志。","Service");
            return new(true,action==RemoteServiceAction.CheckConfiguration?"服务端配置检查通过":"远程操作完成，结果已读取。",Redact(result),action.ToString());
        }
        catch(OperationCanceledException){throw;}
        catch(SshConnectionException e){return new(false,e.Message,"请检查认证、权限以及目标节点的服务状态。",e.Kind.ToString());}
        catch(ArgumentException e){return new(false,e.Message,"","Validation");}
    }
    public static string Redact(string text)
    {
        text=Regex.Replace(text,@"-----BEGIN [^-]*PRIVATE KEY-----[\s\S]*?-----END [^-]*PRIVATE KEY-----","[private key redacted]",RegexOptions.IgnoreCase);
        text=Regex.Replace(text,@"(?i)(password|passwd|token|secret|private_key|uuid|authorization)([\s""':=]+)[^\s,}""']+","$1$2[redacted]");
        text=Regex.Replace(text,@"\b[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}\b","[uuid redacted]");
        text=Regex.Replace(text,@"\b[A-Za-z0-9+/]{32,}={0,2}","[token redacted]");
        return text.Length>32768?text[^32768..]:text;
    }
}
