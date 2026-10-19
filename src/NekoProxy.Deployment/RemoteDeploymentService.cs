using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NekoProxy.Core;
namespace NekoProxy.Deployment;

public sealed class PinnedKernelReleaseCatalog:IKernelReleaseCatalog
{
    public KernelReleaseAsset Get(string architecture)
    {
        var (arch,hash)=architecture switch
        {
            "x86_64"=>("amd64","e2bdf179c15a3652955dc44867e33ca0965c9c9b91b34267dcc5a5d639a5feee"),
            "aarch64"=>("arm64","29dae24d76bea3bc62d07dd3e60cfc12003de7debbd6b768df9be9c68360174e"),
            _=>throw new ArgumentException("目前支持 x86_64 和 aarch64 服务器。")
        };
        var v=ServerConfigurationGenerator.TargetKernelVersion;
        return new(v,arch,$"https://github.com/SagerNet/sing-box/releases/download/v{v}/sing-box-{v}-linux-{arch}.tar.gz",hash);
    }
}
public sealed record RemotePaths(Guid NodeId)
{
    public string Unit=>$"nekoproxy-{NodeId:N}.service";
    public string Directory=>$"/etc/nekoproxy/nodes/{NodeId:N}";
    public string Configuration=>Directory+"/config.json";
    public string UnitFile=>"/etc/systemd/system/"+Unit;
    public string BinaryDirectory=>"/opt/nekoproxy/sing-box/"+ServerConfigurationGenerator.TargetKernelVersion;
    public string Binary=>BinaryDirectory+"/sing-box";
    public string Owner=>"NekoProxy.Node.v1:"+NodeId.ToString("N");
}
public static class RemoteCommands
{
    internal static readonly IDistributionAdapter[] Distributions=[new AptDistributionAdapter(),new PacmanDistributionAdapter()];
    public static string Quote(string value)
    {
        if(value.Contains('\0')||value.Contains('\r')||value.Contains('\n'))throw new ArgumentException("路径不能包含换行或控制字符。");
        return "'"+value.Replace("'","'\"'\"'")+"'";
    }
    public static string Privileged(string script)=>"if [ \"$(id -u)\" = 0 ]; then sh -c "+QuoteScript(script)+"; else sudo -n sh -c "+QuoteScript(script)+"; fi";
    private static string QuoteScript(string script)=>"'"+script.Replace("'","'\"'\"'")+"'";
    public static string Probe(RemoteDeploymentRequest request,bool checkCertificates=true)
    {
        var p=new RemotePaths(request.Draft.Node.Id);var c=request.Configuration.Client;
        var cert=checkCertificates&&request.Configuration.AutomaticCertificate is null&&(request.Draft.Node.Protocol is ProxyProtocol.TrojanTls or ProxyProtocol.Hysteria2);
        if(cert && (request.Configuration.CertificatePath is not { } cp||!cp.StartsWith('/')||request.Configuration.CertificateKeyPath is not { } kp||!kp.StartsWith('/')))throw new ArgumentException("部署时请填写 Linux 服务器上的证书和私钥绝对路径。");
        var script=$$"""
            # NekoProxy: readonly preflight
            . /etc/os-release
            printf 'DISTRO=%s\nLIKE=%s\nARCH=%s\n' "$ID" "${ID_LIKE:-}" "$(uname -m)"
            if [ "$(id -u)" = 0 ] || [ "$(sudo -n sh -c 'id -u' 2>/dev/null)" = 0 ]; then printf 'ELEVATE=1\n'; else printf 'ELEVATE=0\n'; fi
            if [ -d /run/systemd/system ] && command -v systemctl >/dev/null; then printf 'SYSTEMD=1\n'; else printf 'SYSTEMD=0\n'; fi
            if command -v sing-box >/dev/null || [ -x {{Quote(p.Binary)}} ]; then printf 'INSTALLED=1\n'; else printf 'INSTALLED=0\n'; fi
            if command -v pacman >/dev/null; then printf 'PACMAN=1\n'; fi
            if [ -d /etc/nekoproxy ]; then printf 'CONFIGROOT=1\n'; fi
            if [ -d /opt/nekoproxy ]; then printf 'BINARYROOT=1\n'; fi
            """;
        script+="\n"+Privileged($$"""
            # NekoProxy: ownership and socket inspection
            foreign=0; owned=0
            for root in /etc/nekoproxy /opt/nekoproxy /etc/nekoproxy/nodes /opt/nekoproxy/sing-box; do
              if [ -L "$root" ]; then foreign=1; elif { [ "$root" = /etc/nekoproxy ] || [ "$root" = /opt/nekoproxy ]; } && [ -e "$root" ] && ! grep -qx 'NekoProxy.Root.v1' "$root/.owner" 2>/dev/null; then foreign=1; fi
            done
            if [ -e {{Quote(p.Directory)}} ]; then
              if [ ! -L {{Quote(p.Directory)}} ] && grep -qx {{Quote(p.Owner)}} {{Quote(p.Directory+"/.owner")}} 2>/dev/null && [ ! -L {{Quote(p.Configuration)}} ]; then if [ -f {{Quote(p.Configuration)}} ]; then owned=1; fi; else foreign=1; fi
            fi
            if [ -e {{Quote(p.UnitFile)}} ] && { [ -L {{Quote(p.UnitFile)}} ] || ! grep -qx {{Quote("# "+p.Owner)}} {{Quote(p.UnitFile)}}; }; then foreign=1; fi
            printf 'FOREIGN=%s\nOWNED=%s\n' "$foreign" "$owned"
            printf 'STATE=%s\n' "$(systemctl is-active {{Quote(p.Unit)}} 2>/dev/null || true)"
            printf 'PID=%s\n' "$(systemctl show {{Quote(p.Unit)}} -p MainPID --value 2>/dev/null || true)"
            printf 'ENABLED=%s\n' "$(systemctl is-enabled {{Quote(p.Unit)}} 2>/dev/null || true)"
            if command -v ss >/dev/null; then
              ss -H -lntup {{Quote("sport = :"+c.Port.ToString(CultureInfo.InvariantCulture))}} | sed 's/^/SOCKET=/'
            else
              awk '$2 ~ /:{{c.Port.ToString("X4",CultureInfo.InvariantCulture)}}$/ && ($4 == "0A" || FILENAME ~ /udp/) {print "PROC=" FILENAME}' /proc/net/tcp /proc/net/tcp6 /proc/net/udp /proc/net/udp6
            fi
            """)+" || true\n";
        script+=Privileged(cert?$"if [ -r {Quote(request.Configuration.CertificatePath!)} ] && [ -r {Quote(request.Configuration.CertificateKeyPath!)} ]; then printf 'CERT=1\\n'; else printf 'CERT=0\\n'; fi":"printf 'CERT=1\\n'")+" || true";
        // sudo failure yields ELEVATE=0, not a fabricated successful inventory.
        if(checkCertificates&&request.Configuration.AutomaticCertificate is not null)script+="\n"+CertificateAutomation.Probe(request);
        return script;
    }
    public static string UnitFile(RemotePaths p,string? certificateStorage=null)=>$$"""
        # {{p.Owner}}
        [Unit]
        Description=NekoProxy sing-box node {{p.NodeId:N}}
        After=network-online.target
        Wants=network-online.target
        StartLimitIntervalSec=60
        StartLimitBurst=3
        [Service]
        Type=simple
        ExecStart={{p.Binary}} run -c {{p.Configuration}}
        Restart=on-failure
        RestartSec=3
        NoNewPrivileges=true
        ProtectSystem=full
        ProtectHome=read-only
        {{(certificateStorage is null?"":"ReadWritePaths="+certificateStorage)}}
        UMask=0077
        [Install]
        WantedBy=multi-user.target
        """+"\n";
    public static string InstallKernel(RemotePaths p,KernelReleaseAsset asset,string stage,string manager)
    {
        if(!Regex.IsMatch(asset.Sha256,"^[a-f0-9]{64}$")||asset.Version!=ServerConfigurationGenerator.TargetKernelVersion||asset.Url!=$"https://github.com/SagerNet/sing-box/releases/download/v{asset.Version}/sing-box-{asset.Version}-linux-{asset.Architecture}.tar.gz")throw new InvalidDataException("内核发行信息不可信，已停止下载。");
        var pkg=Distributions.SingleOrDefault(a=>a.Name==manager)?.InstallPrerequisitesCommand??throw new ArgumentException("发行版暂不支持自动安装依赖。");
        return Privileged($$"""
            # NekoProxy: install pinned verified kernel
            set -eu
            if ! command -v curl >/dev/null || ! command -v tar >/dev/null || ! command -v openssl >/dev/null || [ ! -s /etc/ssl/certs/ca-certificates.crt ]; then {{pkg}}; fi
            if [ -d {{Quote(p.BinaryDirectory)}} ]; then
              test ! -L {{Quote(p.BinaryDirectory)}}
              grep -qx {{Quote(asset.Sha256)}} {{Quote(p.BinaryDirectory+"/.archive-sha256")}}
              cd {{Quote(p.BinaryDirectory)}}; sha256sum -c binary.sha256 >/dev/null
              printf 'KERNEL=verified-cache\n'; exit 0
            fi
            curl -fL --proto '=https' --proto-redir '=https' --tlsv1.2 --connect-timeout 20 --max-time 180 --max-filesize 150000000 {{Quote(asset.Url)}} -o {{Quote(stage+"/core.tar.gz")}}
            printf '%s  %s\n' {{Quote(asset.Sha256)}} {{Quote(stage+"/core.tar.gz")}} | sha256sum -c - >/dev/null
            tar -xzf {{Quote(stage+"/core.tar.gz")}} --no-same-owner -C {{Quote(stage)}}
            test -f {{Quote(stage+$"/sing-box-{asset.Version}-linux-{asset.Architecture}/sing-box")}}
            test ! -L {{Quote(stage+$"/sing-box-{asset.Version}-linux-{asset.Architecture}/sing-box")}}
            for root in /etc/nekoproxy /opt/nekoproxy /etc/nekoproxy/nodes /opt/nekoproxy/sing-box; do
              if [ ! -d "$root" ]; then install -d -m 700 "$root"; printf 'NekoProxy.Root.v1\n' > "$root/.owner"; chmod 600 "$root/.owner"; fi
            done
            install -d -m 700 {{Quote("/opt/nekoproxy/sing-box")}}
            install -d -m 700 {{Quote(stage+"/installed-core")}}
            install -m 755 {{Quote(stage+$"/sing-box-{asset.Version}-linux-{asset.Architecture}/sing-box")}} {{Quote(stage+"/installed-core/sing-box")}}
            cd {{Quote(stage+"/installed-core")}}; sha256sum sing-box > binary.sha256
            printf '%s\n' {{Quote(asset.Sha256)}} > .archive-sha256; chmod 600 binary.sha256 .archive-sha256
            mv {{Quote(stage+"/installed-core")}} {{Quote(p.BinaryDirectory)}}
            printf 'KERNEL=installed-verified\n'
            """);
    }
    public static string Rollback(RemotePaths p,string checkpoint,bool hadConfig,bool wasActive,bool wasEnabled)=>$$"""
        # NekoProxy: restore this owned node only
        set -eu
        test -f {{Quote(checkpoint+"/.ready")}}
        if [ -f {{Quote(checkpoint+"/.had-config")}} ]; then test -f {{Quote(checkpoint+"/config.json")}}; fi
        if [ -f {{Quote(checkpoint+"/.had-unit")}} ]; then test -f {{Quote(checkpoint+"/unit.service")}}; fi
        systemctl stop {{Quote(p.Unit)}} 2>/dev/null || true
        systemctl disable {{Quote(p.Unit)}} 2>/dev/null || true
        if [ -f {{Quote(checkpoint+"/config.json")}} ]; then install -m 600 {{Quote(checkpoint+"/config.json")}} {{Quote(p.Configuration)}}; else {{(hadConfig?"false":"rm -f "+Quote(p.Configuration))}}; fi
        if [ -f {{Quote(checkpoint+"/unit.service")}} ]; then install -m 644 {{Quote(checkpoint+"/unit.service")}} {{Quote(p.UnitFile)}}; else rm -f {{Quote(p.UnitFile)}}; fi
        systemctl daemon-reload
        {{(wasEnabled?"systemctl enable ":"systemctl disable ")+Quote(p.Unit)}} 2>/dev/null || true
        {{(wasActive?"systemctl start "+Quote(p.Unit)+" && systemctl is-active --quiet "+Quote(p.Unit):"true")}}
        """;
    public static string Commit(RemotePaths p,string stage,string checkpoint,bool hadConfig,bool wasActive,bool wasEnabled,string? readiness=null)
    {
        var rollback=Rollback(p,checkpoint,hadConfig,wasActive,wasEnabled);
        return Privileged($$"""
            # NekoProxy: transactional activate
            set -eu
            install -d -m 700 {{Quote(p.Directory)}} {{Quote(checkpoint)}}
            if [ -f {{Quote(p.Configuration)}} ]; then cp -p {{Quote(p.Configuration)}} {{Quote(checkpoint+"/config.json")}}; touch {{Quote(checkpoint+"/.had-config")}}; fi
            if [ -f {{Quote(p.UnitFile)}} ]; then cp -p {{Quote(p.UnitFile)}} {{Quote(checkpoint+"/unit.service")}}; touch {{Quote(checkpoint+"/.had-unit")}}; fi
            touch {{Quote(checkpoint+"/.ready")}}
            committed=0
            restore_node() { if [ "$committed" = 0 ]; then {{rollback}}; fi; }
            trap restore_node EXIT HUP INT TERM
            printf '%s\n' {{Quote(p.Owner)}} > {{Quote(p.Directory+"/.owner")}}; chmod 600 {{Quote(p.Directory+"/.owner")}}
            install -m 600 {{Quote(stage+"/server.json")}} {{Quote(p.Configuration+".new")}}
            mv -f {{Quote(p.Configuration+".new")}} {{Quote(p.Configuration)}}
            install -m 644 {{Quote(stage+"/unit.service")}} {{Quote(p.UnitFile)}}
            systemctl daemon-reload
            systemctl restart {{Quote(p.Unit)}}
            sleep 2
            {{readiness??""}}
            systemctl is-active --quiet {{Quote(p.Unit)}}
            test "$(systemctl show {{Quote(p.Unit)}} -p MainPID --value)" -gt 0
            systemctl enable {{Quote(p.Unit)}} >/dev/null
            committed=1; trap - EXIT HUP INT TERM
            printf 'ACTIVE=1\n'
            """);
    }
}
public sealed class RemoteDeploymentService(ISshConnectionFactory connections,IServerConfigurationGenerator generator,IKernelReleaseCatalog catalog):IRemoteDeploymentService
{
    public async Task<RemoteInspection> InspectHostAsync(ServerProfile server,ProxyNode node,TimeSpan timeout,string? passphrase=null,CancellationToken token=default)
    {
        var request=new RemoteDeploymentRequest(new(server,node,new("")),new(new(server.Name,node.Name,server.Host,node.Port,node.Protocol,"","")),timeout,KeyPassphrase:passphrase);
        await using var session=await connections.ConnectWithKeyAsync(server,timeout,passphrase,token);
        return ParseInspection(await session.ExecuteEnvironmentAsync(RemoteCommands.Probe(request,false),token),request);
    }
    public Task<DeploymentResult> DeployAsync(DeploymentPlan plan,CancellationToken token=default)=>Task.FromResult(new DeploymentResult(false,"请在部署向导中检查连接并确认部署预览。"));
    public async Task<RemoteInspection> InspectAsync(RemoteDeploymentRequest request,CancellationToken token=default)
    {
        _=generator.Generate(request.Configuration);
        await using var session=await connections.ConnectWithKeyAsync(request.Draft.Server,request.Timeout,request.KeyPassphrase,token);
        return await InspectSessionAsync(session,request,token);
    }
    public static RemoteInspection ParseInspection(string text,RemoteDeploymentRequest request)
    {
        var lines=text.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(s=>s.TrimEnd('\r')).ToArray();
        string V(string key)=>lines.LastOrDefault(l=>l.StartsWith(key+"=",StringComparison.Ordinal))?[(key.Length+1)..]??"";
        bool B(string key)=>V(key)=="1";
        var distro=V("DISTRO");var like=V("LIKE").Split(' ');
        var adapter=RemoteCommands.Distributions.FirstOrDefault(a=>a.Supports(distro)||a.Name=="Pacman"&&like.Any(a.Supports));
        var manager=adapter is not null&&(adapter.Name!="Pacman"||B("PACMAN"))?adapter.Name:"";
        var sockets=lines.Where(l=>l.StartsWith("SOCKET=")).Select(l=>l[7..]).Where(l=>request.Draft.Node.Protocol==ProxyProtocol.Shadowsocks2022||l.StartsWith(request.Draft.Node.Protocol==ProxyProtocol.Hysteria2?"udp":"tcp")).ToArray();
        var procBusy=lines.Any(l=>l.StartsWith("PROC=")&&(request.Draft.Node.Protocol==ProxyProtocol.Shadowsocks2022||l.Contains(request.Draft.Node.Protocol==ProxyProtocol.Hysteria2?"/udp":"/tcp")));
        var available=sockets.Length==0&&!procBusy;
        if(!available&&B("OWNED")&&long.TryParse(V("PID"),out var pid)&&pid>0&&sockets.Length>0&&!procBusy)available=sockets.All(l=>l.Contains("pid="+pid+",",StringComparison.Ordinal));
        return new(distro,V("ARCH"),B("SYSTEMD"),B("ELEVATE"),B("INSTALLED"),B("OWNED"),B("FOREIGN"),available,B("CERT"),V("STATE"),manager,DateTimeOffset.UtcNow){ServiceEnabled=V("ENABLED")=="enabled",ConfigRootExists=B("CONFIGROOT"),BinaryRootExists=B("BINARYROOT"),AcmeDnsMatches=request.Configuration.AutomaticCertificate is null||B("ACME_DNS"),AcmePortAvailable=request.Configuration.AutomaticCertificate is null||B("ACME_PORT")};
    }
    private static async Task<RemoteInspection> InspectSessionAsync(ISshSession session,RemoteDeploymentRequest request,CancellationToken token)=>ParseInspection(await session.ExecuteAsync(RemoteCommands.Probe(request),token),request);
    public async Task<RemoteDeploymentOutcome> DeployRemoteAsync(RemoteDeploymentRequest request,IProgress<DeploymentProgress>? progress=null,CancellationToken token=default)
    {
        var steps=new List<DeploymentProgress>();void Report(string step,string state,string detail){var item=new DeploymentProgress(step,state,detail);var index=steps.FindIndex(p=>p.Step==step);if(index<0)steps.Add(item);else steps[index]=item;progress?.Report(item);}
        ISshSession? session=null;string? stage=null;RemoteInspection? inspection=null;var committing=false;var rollbackAttempted=false;var rollbackSucceeded=false;
        var generated=false;var coreChecked=false;
        var paths=new RemotePaths(request.Draft.Node.Id);var checkpoint=paths.Directory+"/backups/"+Guid.NewGuid().ToString("N");
        try
        {
            var configuration=generator.Generate(request.Configuration);
            generated=true;
            Report("连接服务器","进行中","正在核验 SSH 主机指纹。");
            session=await connections.ConnectWithKeyAsync(request.Draft.Server,request.Timeout,request.KeyPassphrase,token);
            Report("连接服务器","已完成","SSH 连接已建立。");
            Report("检查部署环境","进行中","正在读取系统、权限、端口和配置归属。");
            inspection=await InspectSessionAsync(session,request,token);
            if(!inspection.CanElevate)throw new SshConnectionException(SshFailureKind.Permission,"部署需要 root 或免密 sudo 权限。");
            if(!inspection.AcmeDnsMatches)throw new InvalidOperationException("域名解析未完全指向目标服务器，证书申请已停止。");
            if(!inspection.AcmePortAvailable)throw new InvalidOperationException("ACME 验证端口已占用或无法检查。不会自动停止现有服务。");
            if(!inspection.CanDeploy)throw new InvalidOperationException(!inspection.PortAvailable?"端口已占用，或无法确认占用进程属于这个节点。":inspection.ForeignConfiguration?"目标目录或服务不属于 NekoProxy，已停止部署。":!inspection.CertificatesReadable?"服务器上找不到可读的证书或私钥文件。":"这台服务器的发行版、架构或 systemd 暂不满足部署要求。");
            if(inspection.OwnedConfiguration&&request.ExistingPolicy!=ExistingDeploymentPolicy.BackupAndReplace)throw new InvalidOperationException("已有节点配置。请选择保留备份后更新，或取消此次部署。");
            Report("检查部署环境","已完成","系统、权限、端口与配置归属已检查。");
            stage=(await session.ExecuteAsync("umask 077; mktemp -d /tmp/nekoproxy.XXXXXXXXXX",token)).Trim();
            if(!Regex.IsMatch(stage,@"^/tmp/nekoproxy\.[A-Za-z0-9]{8,}$"))throw new InvalidDataException("无法创建安全的临时目录。");
            var automatic=request.Configuration.AutomaticCertificate;
            await UploadText(session,stage+"/server.json",configuration,token);await UploadText(session,stage+"/unit.service",RemoteCommands.UnitFile(paths,automatic is null?null:CertificatePolicy.Storage(request.Configuration.Client.Sni)),token);
            Report("准备 sing-box","进行中","下载固定版本并校验官方 SHA-256。");
            await session.ExecuteAsync(RemoteCommands.InstallKernel(paths,catalog.Get(inspection.Architecture),stage,inspection.PackageManager),token);
            Report("准备 sing-box","已完成","内核文件校验通过。");
            if(automatic is not null)
            {
                Report("准备自动证书","进行中","检查 with_acme 构建能力并创建受保护的证书存储。服务器将独立执行签发和续期。");
                await session.ExecuteAsync(CertificateAutomation.Prepare(paths,request),token);
                Report("准备自动证书","已完成","自动证书已配置，服务启动时执行实际签发。");
            }
            else if(request.Draft.Node.Protocol is ProxyProtocol.TrojanTls or ProxyProtocol.Hysteria2)
            {
                Report("检查 TLS 证书","进行中","校验证书有效期、域名以及证书与私钥是否匹配。");
                var cert=RemoteCommands.Quote(request.Configuration.CertificatePath!);var key=RemoteCommands.Quote(request.Configuration.CertificateKeyPath!);var domain=RemoteCommands.Quote(request.Configuration.Client.Sni);
                await session.ExecuteAsync(RemoteCommands.Privileged($"set -eu; openssl x509 -in {cert} -checkend 0 -noout >/dev/null; openssl x509 -in {cert} -checkhost {domain} -noout | grep -Fq 'does match certificate'; openssl x509 -in {cert} -pubkey -noout | openssl pkey -pubin -outform DER > {RemoteCommands.Quote(stage+"/cert-public.der")}; openssl pkey -in {key} -passin pass: -pubout -outform DER > {RemoteCommands.Quote(stage+"/key-public.der")}; cmp -s {RemoteCommands.Quote(stage+"/cert-public.der")} {RemoteCommands.Quote(stage+"/key-public.der")}"),token);
                Report("检查 TLS 证书","已完成","证书域名、有效期和密钥匹配检查通过。");
            }
            else if(request.Draft.Node.Protocol==ProxyProtocol.VlessReality)
            {
                Report("检查 REALITY 目标","进行中","检查目标站点的 TLS 1.3 / X25519 握手。");
                var domain=request.Configuration.Client.Sni;
                await session.ExecuteAsync(RemoteCommands.Privileged("timeout 15 openssl s_client -connect "+RemoteCommands.Quote(domain+":443")+" -servername "+RemoteCommands.Quote(domain)+" -verify_hostname "+RemoteCommands.Quote(domain)+" -tls1_3 -groups X25519 -verify_return_error -brief </dev/null >/dev/null 2>&1"),token);
                Report("检查 REALITY 目标","已完成","目标站点的基础握手检查通过，客户端连通性仍需实际验证。");
            }
            Report("校验服务端配置","进行中","使用目标服务器上的固定版本内核检查配置。");
            await session.ExecuteAsync(RemoteCommands.Privileged(RemoteCommands.Quote(paths.Binary)+" check -c "+RemoteCommands.Quote(stage+"/server.json")),token);
            Report("校验服务端配置","已完成","目标服务器上的 sing-box 已接受配置。");
            coreChecked=true;
            committing=true;Report("写入并启动服务","进行中","仅写入此节点的 NekoProxy 目录和服务。");
            var activated=await session.ExecuteAsync(RemoteCommands.Commit(paths,stage,checkpoint,inspection.OwnedConfiguration,inspection.ServiceState=="active",inspection.ServiceEnabled,automatic is null?null:CertificateAutomation.Readiness(request)),token);
            if(!activated.Contains("ACTIVE=1"))throw new InvalidOperationException("没有收到服务启动确认，请检查服务器状态。");
            if(automatic is not null&&!activated.Contains("CERT_READY=1"))throw new InvalidOperationException("尚未确认自动证书与服务监听，不能报告部署成功。");
            Report("写入并启动服务","已完成","systemd 已确认服务运行，并启用开机启动。");
            return new(true,"服务已部署并启动；客户端连接尚未验证。",paths.Unit,inspection,steps){ConfigurationGenerated=true,CoreChecked=true,ServiceStarted=true};
        }
        catch(Exception e)
        {
            if(committing&&session is not null&&inspection is not null)
            {
                rollbackAttempted=true;
                try{using var recovery=new CancellationTokenSource(TimeSpan.FromSeconds(20));await session.ExecuteAsync(RemoteCommands.Privileged(RemoteCommands.Rollback(paths,checkpoint,inspection.OwnedConfiguration,inspection.ServiceState=="active",inspection.ServiceEnabled)),recovery.Token);rollbackSucceeded=true;}
                catch{ }
            }
            var message=e is OperationCanceledException?"部署已取消。":e is SshConnectionException or ArgumentException or InvalidOperationException or InvalidDataException?e.Message:"部署未完成，请检查服务器连接和当前步骤。";
            if(rollbackAttempted)message+=rollbackSucceeded?" 已恢复这次操作前的节点配置。":" 未能确认回滚结果，请检查服务器上的备份与服务状态。";
            if(steps.LastOrDefault() is {State:"进行中"} last)Report(last.Step,"失败",message);
            Report("部署结果","未完成",message);
            return new(false,message,null,inspection,steps,rollbackAttempted,rollbackSucceeded){ConfigurationGenerated=generated,CoreChecked=coreChecked};
        }
        finally
        {
            if(session is not null)
            {
                if(stage is not null&&Regex.IsMatch(stage,@"^/tmp/nekoproxy\.[A-Za-z0-9]{8,}$"))
                    try{using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(15));await session.ExecuteAsync(RemoteCommands.Privileged("rm -rf -- "+RemoteCommands.Quote(stage)),cleanup.Token);}catch{ }
                await session.DisposeAsync();
            }
        }
    }
    private static async Task UploadText(ISshSession session,string path,string text,CancellationToken token)
    {
        var bytes=Encoding.UTF8.GetBytes(text.Replace("\r\n","\n"));
        try{using var stream=new MemoryStream(bytes,false);await session.UploadAsync(stream,path,token);}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
}
