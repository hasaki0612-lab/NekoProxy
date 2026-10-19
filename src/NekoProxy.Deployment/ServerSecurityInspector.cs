using System.Text.RegularExpressions;
using NekoProxy.Core;

namespace NekoProxy.Deployment;

// No mutations: fixed read-only probes, bounded output, allowlisted configuration facts only.
public sealed class ServerSecurityInspector(ISshConnectionFactory connections):IServerSecurityInspector
{
    public async Task<ServerInspectionReport> InspectAsync(ServerProfile server,TimeSpan timeout,CancellationToken token=default)
    {
        var started=DateTimeOffset.UtcNow;var rows=new List<SecurityFinding>();
        await using var session=await connections.ConnectAsync(server,timeout,token);
        async Task Read(string id,string area,string title,string script,Func<string,(InspectionState State,string Recommendation)> classify)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var result=await session.ExecuteAsync(RemoteCommands.Privileged(script),token);
                var (state,recommendation)=classify(result);
                rows.Add(new(id,area,title,state,RemoteAdministration.Redact(result),recommendation,DateTimeOffset.UtcNow));
            }
            catch(OperationCanceledException){throw;}
            catch(SshConnectionException e){rows.Add(new(id,area,title,InspectionState.NotChecked,e.Kind+"："+e.Message,"此检查未完成，请恢复 SSH 后重试；未执行任何修改。",DateTimeOffset.UtcNow));}
        }
        await Read("ssh","SSH","认证与登录权限",SshProbe,EvaluateSsh);
        await Read("listeners","网络","监听端口与主机防火墙",NetworkProbe,s=>(s.Contains("LISTENERS_UNAVAILABLE")?InspectionState.NotChecked:InspectionState.Attention,"核对公开监听是否符合用途。主机规则不代表云安全组；未读取云控制台，也未自动开放端口。"));
        await Read("resources","性能","TCP、出站 RTT 与系统资源",PerformanceProbe,EvaluatePerformance);
        await Read("retention","日志","日志保留策略",LogProbe,s=>(InspectionState.Attention,"journal 的默认配置可能依发行版变化。请按磁盘容量评估日志上限与保留期；当前工具不会读取或上传认证日志正文。"));
        foreach(var node in server.Nodes)
        {
            if(node.ServiceUnit!=new RemotePaths(node.Id).Unit)
            {rows.Add(new("node-"+node.Id,"代理服务",node.Name,InspectionState.NotChecked,"没有可核实的 NekoProxy 部署归属记录。","部署或核实归属后再检查；没有读取未知服务配置。",DateTimeOffset.UtcNow));continue;}
            await Read("node-"+node.Id,"代理服务",node.Name,NodeProbe(node),EvaluateNode);
            if(node.Protocol is ProxyProtocol.TrojanTls or ProxyProtocol.Hysteria2)
                await Read("cert-"+node.Id,"TLS",node.Name+" · 证书",CertificateProbe(node),EvaluateCertificate);
        }
        rows.Add(new("privacy-boundary","隐私边界","入口、出口与 ECH",InspectionState.NotChecked,
            "服务端检查不能证明客户端 DNS / IPv6 已受保护。TLS / REALITY 的 SNI 与流量特征仍可能被观察。",
            "ECH 取决于客户端、DNS HTTPS 记录及目标服务支持，本版本未自动启用或验证 ECH。出口网站通常看到 VPS 地址；入站观察者仍可能看到 VPS 地址、端口与握手特征。",DateTimeOffset.UtcNow));
        return new(server.Id,started,DateTimeOffset.UtcNow,rows);
    }
    public const string SshProbe="""
        # NekoProxy v0.6 readonly SSH audit
        if command -v sshd >/dev/null 2>&1; then sshd -T 2>/dev/null | awk '$1 ~ /^(permitrootlogin|passwordauthentication|pubkeyauthentication|kbdinteractiveauthentication|authenticationmethods|port|listenaddress|allowusers|allowgroups|maxauthtries)$/ {print}'; else printf 'SSHD_UNAVAILABLE\n'; fi
        stat -c 'SSH_CONFIG_MODE=%a OWNER=%U' /etc/ssh/sshd_config 2>/dev/null || true
        """;
    public const string NetworkProbe="""
        # NekoProxy v0.6 readonly network audit
        if command -v ss >/dev/null; then ss -H -lntu | head -n 64; else printf 'LISTENERS_UNAVAILABLE\n'; fi
        if command -v nft >/dev/null; then printf 'NFT_RULES\n'; nft list ruleset 2>/dev/null | head -n 100; elif command -v iptables >/dev/null; then printf 'IPTABLES\n'; iptables -S 2>/dev/null | head -n 80; else printf 'FIREWALL_TOOL_UNAVAILABLE\n'; fi
        printf 'CLOUD_SECURITY_GROUP=not_checked\n'
        """;
    public const string PerformanceProbe="""
        # NekoProxy v0.6 readonly performance audit; three low-rate outbound ICMP probes
        printf 'TCP_CONGESTION='; cat /proc/sys/net/ipv4/tcp_congestion_control 2>/dev/null || true
        printf 'TCP_AVAILABLE='; cat /proc/sys/net/ipv4/tcp_available_congestion_control 2>/dev/null || true
        printf 'LOAD='; cat /proc/loadavg; free -m; df -h / | tail -n 1
        if command -v ping >/dev/null; then ping -n -4 -c 3 -i 1 -W 2 1.1.1.1 2>&1 || true; else printf 'PING_UNAVAILABLE\n'; fi
        printf 'THROUGHPUT=not_load_tested\nHY2=QUIC congestion is independent of Linux TCP congestion control\n'
        """;
    public const string LogProbe="""
        # NekoProxy v0.6 readonly log policy; never read log bodies
        journalctl --disk-usage 2>/dev/null || true
        if command -v systemd-analyze >/dev/null; then systemd-analyze cat-config systemd/journald.conf 2>/dev/null | awk '/^[[:space:]]*(SystemMaxUse|RuntimeMaxUse|MaxRetentionSec|Storage)=/ {print}'; fi
        printf 'APPLICATION_LOG_REDACTION=local exporter and command results use allowlists/redaction; exhaustive content not audited\n'
        """;
    public static string NodeProbe(ProxyNode node)
    {
        var p=new RemotePaths(node.Id);var q=RemoteCommands.Quote;
        return $$"""
            # NekoProxy v0.6 readonly owned-node audit
            if [ -L {{q(p.Directory)}} ] || [ -L {{q(p.Configuration)}} ] || [ -L {{q(p.UnitFile)}} ] || ! grep -qx {{q(p.Owner)}} {{q(p.Directory+"/.owner")}} || ! grep -qx {{q("# "+p.Owner)}} {{q(p.UnitFile)}}; then printf 'OWNERSHIP_UNVERIFIED\n'; exit 0; fi
            stat -c 'CONFIG_MODE=%a OWNER=%U' {{q(p.Configuration)}}
            systemctl show {{q(p.Unit)}} --property=ActiveState,User,NoNewPrivileges,PrivateTmp,ProtectSystem,ProtectHome,MemoryCurrent,CPUUsageNSec --no-pager
            if command -v python3 >/dev/null; then python3 - {{q(p.Configuration)}} <<'NEKO_AUDIT_PY'
            import json,sys,ipaddress
            data=json.load(open(sys.argv[1]))
            targets=['127.0.0.0/8','10.0.0.0/8','172.16.0.0/12','192.168.0.0/16','169.254.0.0/16','::1/128','fc00::/7','fe80::/10']
            rules=data.get('route',{}).get('rules',[])
            # Conservative: only an initial unconditional CIDR reject covers all inbounds.
            protected=[]
            for rule in rules:
                if set(rule)-{'ip_cidr','action','method'} or rule.get('action')!='reject': break
                for cidr in rule.get('ip_cidr',[]):
                    try: protected.append(ipaddress.ip_network(cidr,strict=False))
                    except ValueError: pass
            covered=all(any(ipaddress.ip_network(t).version==p.version and ipaddress.ip_network(t).subnet_of(p) for p in protected) for t in targets)
            print('PRIVATE_DESTINATIONS_BLOCKED='+str(covered).lower())
            print('LOG_LEVEL='+str(data.get('log',{}).get('level','default')))
            NEKO_AUDIT_PY
            else printf 'CONFIG_FACTS_UNAVAILABLE\n'; fi
            """;
    }
    public static string CertificateProbe(ProxyNode node)
    {
        if(string.IsNullOrEmpty(node.CertificatePath)||!node.CertificatePath.StartsWith('/')||!Regex.IsMatch(node.Sni,@"^[A-Za-z0-9.-]+$"))return "printf 'CERTIFICATE_UNAVAILABLE\\n'";
        var cert=RemoteCommands.Quote(node.CertificatePath);var name=RemoteCommands.Quote(node.Sni);
        return $$"""
            # NekoProxy v0.6 readonly public certificate audit (never read private key)
            if [ -L {{cert}} ] || [ ! -f {{cert}} ]; then printf 'CERTIFICATE_UNAVAILABLE\n'; exit 0; fi
            openssl x509 -in {{cert}} -noout -issuer -enddate -checkhost {{name}} 2>&1
            if openssl x509 -in {{cert}} -noout -checkend 2592000 >/dev/null 2>&1; then printf 'EXPIRY_30D=valid\n'; else printf 'EXPIRY_30D=attention\n'; fi
            if openssl verify -verify_hostname {{name}} -untrusted {{cert}} {{cert}} >/dev/null 2>&1; then printf 'PUBLIC_CA_CHAIN=verified\n'; else printf 'PUBLIC_CA_CHAIN=not_verified\n'; fi
            systemctl list-timers --all --no-pager --no-legend 'nekoproxy*' 2>/dev/null | head -n 20
            printf 'RENEWAL_EXECUTION=not_exercised\n'
            """;
    }
    public static (InspectionState,string) EvaluateSsh(string s)
    {
        if(s.Contains("SSHD_UNAVAILABLE")||!s.Contains("permitrootlogin "))return(InspectionState.NotChecked,"无法读取有效 sshd 配置。Match 条件及实际客户端认证需另行验证。");
        var mode=Regex.Match(s,@"SSH_CONFIG_MODE=([0-7]{3,4})");
        if(!mode.Success)return(InspectionState.NotChecked,"认证配置已读取，但未核实 SSH 配置文件写权限。");
        if((Convert.ToInt32(mode.Groups[1].Value,8)&0x12)!=0||Regex.IsMatch(s,@"(?m)^permitrootlogin yes$|^passwordauthentication yes$|^kbdinteractiveauthentication yes$"))
            return(InspectionState.Attention,"检查配置文件写权限、root 或交互密码登录，评估密钥认证与来源限制。先验证备用会话再变更；本检查不会更改 SSH。");
        return(InspectionState.Normal,"已读取默认认证配置与基础文件权限；sshd Match 条件、云防火墙和实际认证仍需单独核实。");
    }
    public static (InspectionState,string) EvaluatePerformance(string s)
    {
        var loss=Regex.Match(s,@"(?<loss>[0-9]+(?:\.[0-9]+)?)% packet loss");
        var state=!loss.Success?InspectionState.NotChecked:double.Parse(loss.Groups["loss"].Value,System.Globalization.CultureInfo.InvariantCulture)>0?InspectionState.Attention:InspectionState.Normal;
        return(state,"结果仅为 VPS → 1.1.1.1 的 IPv4 出站 ICMP 与当前资源快照，不证明入站或代理性能。未压测吞吐量；请结合监测页面的真实接口速率。不要仅因未启用 BBR 就修改内核。");
    }
    public static (InspectionState,string) EvaluateNode(string s)=>s.Contains("OWNERSHIP_UNVERIFIED")||s.Contains("CONFIG_FACTS_UNAVAILABLE")?(InspectionState.NotChecked,"归属或配置读取未完成，没有读取未知配置。"):
        !s.Contains("PRIVATE_DESTINATIONS_BLOCKED=true")||!Regex.IsMatch(s,@"(?m)^User=.+$")||s.Contains("User=root")||!s.Contains("NoNewPrivileges=yes")||!s.Contains("PrivateTmp=yes")||!Regex.IsMatch(s,@"(?m)^ProtectSystem=(full|strict)$")||!s.Contains("CONFIG_MODE=600 ")||!s.Contains("ActiveState=active")?
        (InspectionState.Attention,"检查专用运行账户、systemd 隔离及配置权限；当前配置可能允许节点访问 VPS 内网、回环或云元数据。建议先评估兼容性，再预览、备份并单独确认变更。客户端阻止局域网不能代替服务端限制。"):
        (InspectionState.Normal,"检测到基础限制；规则行为、其他私网段和间接 DNS 重绑定仍需端到端验证。");
    public static (InspectionState,string) EvaluateCertificate(string s)=>s.Contains("CERTIFICATE_UNAVAILABLE")?(InspectionState.NotChecked,"没有可检查的证书文件。"):
        s.Contains("PUBLIC_CA_CHAIN=verified")&&s.Contains("EXPIRY_30D=valid")?(InspectionState.Normal,"当前文件的主机名、CA 链及 30 天有效期已检查；不代表线上握手或自动续期已验证。请核对续期计时器和最近执行状态。"):
        (InspectionState.Attention,"证书有效期或公共 CA 链未通过此只读检查。不得关闭客户端 TLS 验证；请检查完整证书链和域名。自动续期尚未执行验证。");
}
