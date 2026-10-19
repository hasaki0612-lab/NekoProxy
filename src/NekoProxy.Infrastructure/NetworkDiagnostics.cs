using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NekoProxy.Core;
namespace NekoProxy.Infrastructure;

public sealed class NetworkDiagnostics(ISshConnectionFactory connections):INetworkDiagnostics
{
    private static readonly ProbeSource Local=new("local","本机","地区未配置","运营商未识别");
    private static readonly ProbeSource Remote=new("vps","VPS 本机","服务器所在地未配置","运营商未识别");
    private readonly SemaphoreSlim _run=new(1,1);
    public async Task<NetworkDiagnosticReport> RunAsync(ServerProfile server,string? domain,TimeSpan sshTimeout,IProgress<NetworkProbeResult>? progress=null,CancellationToken token=default)
    {
        if(ProfileValidation.Validate(server) is {} error)throw new ArgumentException(error);
        await _run.WaitAsync(token);
        try
        {
            var rows=new List<NetworkProbeResult>();void Add(NetworkProbeResult result){rows.Add(result);progress?.Report(result);}
            var hosts=new List<string>{server.Host};
            if(!string.IsNullOrEmpty(server.Ipv6Address)&&IPAddress.TryParse(server.Ipv6Address,out var v6)&&v6.AddressFamily==AddressFamily.InterNetworkV6)hosts.Add(v6.ToString());
            if(!hosts.Any(h=>IPAddress.TryParse(h,out var ip)&&ip.AddressFamily==AddressFamily.InterNetworkV6))
                Add(Result(Local,ProbeDirection.Inbound,"IPv6","",null,"TCP",ProbeState.Unsupported,"本地服务器资料未登记 IPv6 地址；未用 IPv4 代替 IPv6 测量。"));
            var endpoints=server.Nodes.SelectMany(n=>n.Protocol==ProxyProtocol.Hysteria2?new[]{(n.Port,"UDP")}:n.Protocol==ProxyProtocol.Shadowsocks2022?new[]{(n.Port,"TCP"),(n.Port,"UDP")}:new[]{(n.Port,"TCP")}).Prepend((server.SshPort,"TCP")).Distinct().ToArray();
            foreach(var host in hosts)
            {
                var family=IPAddress.TryParse(host,out var ip)?ip.AddressFamily==AddressFamily.InterNetworkV6?"IPv6":"IPv4":"DNS";
                if(family=="IPv6"&&!HasLocalIpv6())
                {Add(Result(Local,ProbeDirection.Inbound,family,host,null,"TCP",ProbeState.Unsupported,"本机未发现全局 IPv6 地址与默认网关，未执行 IPv6 入站探测。"));continue;}
                foreach(var endpoint in endpoints)
                {
                    token.ThrowIfCancellationRequested();
                    if(endpoint.Item2=="UDP")Add(Result(Local,ProbeDirection.Inbound,family,host,endpoint.Item1,"UDP",ProbeState.InsufficientEvidence,"UDP 无可靠通用握手；本机未将单向发送或无回包判为可达。请用对应协议客户端验证。"));
                    else Add(await TcpAsync(host,endpoint.Item1,family,token));
                }
            }
            if(!string.IsNullOrWhiteSpace(domain))
            {
                try
                {
                    if(Uri.CheckHostName(domain)!=UriHostNameType.Dns||domain.TrimEnd('.').EndsWith(".invalid",StringComparison.OrdinalIgnoreCase))throw new ArgumentException();
                    using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(6));
                    var addresses=await Dns.GetHostAddressesAsync(domain,deadline.Token);
                    var expected=hosts.Where(h=>IPAddress.TryParse(h,out _)).Select(IPAddress.Parse).ToArray();
                    var consistent=addresses.Length>0&&addresses.All(a=>expected.Contains(a));
                    var mapped=addresses.Any(IsSyntheticDnsAddress);
                    Add(Result(Local,ProbeDirection.ServerConfiguration,"IPv4 / IPv6",domain,null,"DNS",consistent?ProbeState.Reachable:mapped?ProbeState.InsufficientEvidence:ProbeState.DnsError,"系统 DNS 结果："+string.Join(", ",addresses.Select(a=>a.ToString()))+(consistent?"；全部匹配已记录服务器地址。":mapped?"；系统返回基准/测试地址段，可能有本地 DNS 映射；需要对照 VPS 解析，不能直接判为域名配置错误。":"；未全部匹配已记录服务器地址，请核对 DNS 与 IPv6 资料。")));
                }
                catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                catch{Add(Result(Local,ProbeDirection.ServerConfiguration,"IPv4 / IPv6",domain,null,"DNS",ProbeState.DnsError,"DNS 未完成或域名无效；没有推断 IP 被限制。"));}
            }
            try
            {
                await using var session=await connections.ConnectAsync(server,sshTimeout,token);
                Add(Result(Remote,ProbeDirection.ServerConfiguration,"SSH",server.Host,server.SshPort,"SSH",ProbeState.Reachable,"已按既有指纹完成 SSH 身份认证。"));
                if(!string.IsNullOrWhiteSpace(domain)&&Uri.CheckHostName(domain)==UriHostNameType.Dns)
                {
                    var quoted="'"+domain.Replace("'","'\"'\"'")+"'";
                    var resolved=await session.ExecuteAsync("getent ahosts "+quoted+" 2>/dev/null | awk '{print $1}' | sort -u",token);
                    var ips=resolved.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).Where(s=>IPAddress.TryParse(s,out _)).Distinct().ToArray();
                    var expected=hosts.Where(h=>IPAddress.TryParse(h,out _)).Select(IPAddress.Parse).ToArray();var matches=ips.Length>0&&expected.Length>0&&ips.All(ip=>expected.Contains(IPAddress.Parse(ip)));
                    Add(Result(Remote,ProbeDirection.ServerConfiguration,"IPv4 / IPv6",domain,null,"DNS",matches?ProbeState.Reachable:expected.Length==0?ProbeState.InsufficientEvidence:ProbeState.DnsError,"VPS 系统 DNS："+string.Join(", ",ips)+(matches?"；匹配服务器记录。":"；未全部匹配服务器记录或没有可比较的地址。")));
                }
                var info=await session.ExecuteEnvironmentAsync("printf 'ADDR\\n'; ip -o addr show scope global; printf 'ROUTE4\\n'; ip -4 route show default; printf 'ROUTE6\\n'; ip -6 route show default; printf 'SOCKETS\\n'; if command -v ss >/dev/null; then ss -H -lntup || printf 'LISTENER_CHECK_UNAVAILABLE\\n'; else printf 'LISTENER_CHECK_UNAVAILABLE\\n'; fi; printf 'FIREWALL\\n'; if [ \"$(id -u)\" = 0 ]; then if command -v nft >/dev/null; then nft list ruleset 2>/dev/null | grep -E 'policy|hook|dport|drop|reject' | head -n 40; elif command -v iptables >/dev/null; then iptables -S 2>/dev/null | head -n 40; else printf 'NO_TOOLS\\n'; fi; else printf 'NO_PERMISSION\\n'; fi",token);
                var socketText=Section(info,"SOCKETS","FIREWALL");
                foreach(var endpoint in endpoints)
                {
                    var listening=socketText.Split('\n').Any(l=>l.StartsWith(endpoint.Item2.ToLowerInvariant()+" ")&&l.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(4)?.EndsWith(":"+endpoint.Item1,StringComparison.Ordinal)==true);
                    Add(Result(Remote,ProbeDirection.ServerConfiguration,"服务器接口",server.Host,endpoint.Item1,endpoint.Item2,socketText.Contains("LISTENER_CHECK_UNAVAILABLE")?ProbeState.InsufficientEvidence:listening?ProbeState.Reachable:ProbeState.NotListening,socketText.Contains("LISTENER_CHECK_UNAVAILABLE")?"监听状态读取不可用，不能判定服务未监听。":listening?"ss 观察到本地监听；不能证明公网入站可达。":"ss 未观察到该端口监听；未自动修改服务或防火墙。"));
                }
                foreach(var family in new[]{"IPv4","IPv6"})
                {
                    var addressPresent=family=="IPv4"?Section(info,"ADDR","ROUTE4").Contains(" inet "):Section(info,"ADDR","ROUTE4").Contains(" inet6 ");
                    var routePresent=Section(info,family=="IPv4"?"ROUTE4":"ROUTE6",family=="IPv4"?"ROUTE6":"SOCKETS").Contains("default");
                    var addressLines=Section(info,"ADDR","ROUTE4").Split('\n').Where(l=>l.Contains(family=="IPv4"?" inet ":" inet6 ")).Select(l=>l.Trim());
                    Add(Result(Remote,ProbeDirection.ServerConfiguration,family,server.Host,null,"地址 / 路由",addressPresent&&routePresent?ProbeState.Reachable:ProbeState.Unsupported,$"全局接口地址：{addressPresent}；默认路由：{routePresent}。\n"+string.Join("\n",addressLines)));
                    if(!addressPresent||!routePresent){Add(Result(Remote,ProbeDirection.Outbound,family,"example.com",443,"HTTPS",ProbeState.Unsupported,"该地址族缺少全局地址或默认路由，未执行出站检查。"));continue;}
                    var curl=await session.ExecuteAsync("if ! command -v curl >/dev/null; then printf 'MISSING'; else curl "+(family=="IPv4"?"-4":"-6")+" --noproxy '*' --proto '=https' --max-time 8 --silent --show-error -o /dev/null -w 'HTTP=%{http_code} TIME=%{time_total}' https://example.com 2>/dev/null; code=$?; printf ' EXIT=%s' \"$code\"; fi",token);
                    var success=curl.Contains("EXIT=0")&&!curl.Contains("HTTP=000");
                    Add(Result(Remote,ProbeDirection.Outbound,family,"example.com",443,"HTTPS",success?ProbeState.Reachable:ProbeState.Failed,success?"VPS 使用指定地址族完成 HTTPS 请求，TLS 正常校验；这不能证明用户入站可达。":"VPS HTTPS 出站未完成；没有关闭 TLS 验证。"));
                }
                var fw=Section(info,"FIREWALL",null).Trim();
                Add(Result(Remote,ProbeDirection.ServerConfiguration,"防火墙",server.Host,null,"只读规则",ProbeState.InsufficientEvidence,fw.Length==0?"未读到主机拦截规则；云安全组未接入，不能据此排除云侧限制。":fw.Contains("NO_")?"缺少读取权限或工具；云安全组未接入。":"主机规则摘要：\n"+fw+"\n规则需结合端口与链次序分析；云安全组未接入。"));
            }
            catch(OperationCanceledException){throw;}
            catch(SshConnectionException e){Add(Result(Remote,ProbeDirection.ServerConfiguration,"SSH",server.Host,server.SshPort,"SSH",ProbeState.Failed,e.Message));}
            var findings=rows.Where(r=>r.Direction==ProbeDirection.Inbound&&r.State==ProbeState.Reachable&&r.Port is not null)
                .Where(r=>rows.Any(s=>s.Direction==ProbeDirection.ServerConfiguration&&s.Protocol==r.Protocol&&s.Port==r.Port&&s.State==ProbeState.NotListening))
                .Select(r=>$"{r.Protocol} :{r.Port}：本机返回建连成功，但 VPS 未监听。证据不一致，可能有中间层提前应答；不能据此判定真实服务可达或 IP 被限制。").Distinct().ToArray();
            return new(server.Id,DateTimeOffset.UtcNow,rows,"缺少中国大陆网络测量数据；没有接入外部探测点，也未上传服务器地址。"){Findings=findings};
        }
        finally{_run.Release();}
    }
    private static bool HasLocalIpv6()=>NetworkInterface.GetAllNetworkInterfaces().Any(n=>n.OperationalStatus==OperationalStatus.Up&&n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.AddressFamily==AddressFamily.InterNetworkV6&&!a.Address.IsIPv6LinkLocal&&!IPAddress.IsLoopback(a.Address))&&n.GetIPProperties().GatewayAddresses.Any(a=>a.Address.AddressFamily==AddressFamily.InterNetworkV6));
    public static bool IsSyntheticDnsAddress(IPAddress address){var bytes=address.GetAddressBytes();return bytes.Length==4&&bytes[0]==198&&(bytes[1]==18||bytes[1]==19);}
    public static string Section(string text,string start,string? end){var index=text.IndexOf(start+"\n",StringComparison.Ordinal);if(index<0)return "";index+=start.Length+1;var limit=end is null?text.Length:text.IndexOf(end+"\n",index,StringComparison.Ordinal);return text[index..(limit<0?text.Length:limit)];}
    private static NetworkProbeResult Result(ProbeSource source,ProbeDirection direction,string family,string target,int? port,string protocol,ProbeState state,string evidence)=>new(source,direction,family,target,port,protocol,state,0,0,null,null,DateTimeOffset.UtcNow,evidence);
    private static async Task<NetworkProbeResult> TcpAsync(string host,int port,string family,CancellationToken token)
    {
        var timings=new List<double>();var states=new List<ProbeState>();
        for(var i=0;i<3;i++)
        {
            using var client=new TcpClient();using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(4));var watch=Stopwatch.StartNew();
            try{await client.ConnectAsync(host,port,deadline.Token);timings.Add(watch.Elapsed.TotalMilliseconds);states.Add(ProbeState.Reachable);}
            catch(OperationCanceledException)when(!token.IsCancellationRequested){states.Add(ProbeState.Timeout);}
            catch(SocketException e){states.Add(e.SocketErrorCode switch{SocketError.ConnectionRefused=>ProbeState.Refused,SocketError.TimedOut=>ProbeState.Timeout,SocketError.HostNotFound or SocketError.NoData=>ProbeState.DnsError,_=>ProbeState.Failed});}
            if(i<2)await Task.Delay(300,token);
        }
        var statistics=ProbeStatistics.Summarize(timings);var state=timings.Count==3?ProbeState.Reachable:timings.Count>0||states.Distinct().Count()>1?ProbeState.Partial:states[0];
        return new(Local,ProbeDirection.Inbound,family,host,port,"TCP",state,3,timings.Count,statistics.Mean,statistics.P95,DateTimeOffset.UtcNow,"独立 TCP 建连 3 次；只检测已配置端口。未执行节点认证，也不代表 UDP 或代理协议可用。");
    }
}
