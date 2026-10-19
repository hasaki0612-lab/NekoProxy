using System.Text;
using System.Text.Json;
using NekoProxy.Core;
using NekoProxy.Deployment;
using NekoProxy.Infrastructure;
using Xunit;
namespace NekoProxy.Tests;
public sealed class RemoteDeploymentTests
{
    private static RemoteDeploymentRequest Request(ProxyProtocol protocol=ProxyProtocol.Shadowsocks2022)
    {
        var k=new RealityKeyGenerator().Generate();var credential=protocol==ProxyProtocol.VlessReality?k.Uuid:protocol==ProxyProtocol.Shadowsocks2022?ShadowsocksKeys.Generate(ShadowsocksKeys.DefaultMethod):"test-only-secret-never-log";
        var client=new ClientNodeConfiguration("local-test","node","192.0.2.1",443,protocol,credential,protocol==ProxyProtocol.Shadowsocks2022?"":"example.com",protocol==ProxyProtocol.VlessReality?k.PublicKey:null,protocol==ProxyProtocol.VlessReality?k.ShortId:null);
        var node=new ProxyNode(Guid.NewGuid(),"node",protocol){ParametersConfigured=true,Sni=client.Sni,RealityPublicKey=client.RealityPublicKey??"",ShortId=client.ShortId??"",CertificatePath="/etc/cert.pem",CertificateKeyPath="/etc/key.pem"};
        var server=new ServerProfile(Guid.NewGuid(),"local-test",client.Host,"","",22,"root","test-key.pem",[node]);
        var configuration=new ServerNodeConfiguration(client,protocol==ProxyProtocol.VlessReality?k.PrivateKey:null,protocol is ProxyProtocol.Hysteria2 or ProxyProtocol.TrojanTls?node.CertificatePath:null,protocol is ProxyProtocol.Hysteria2 or ProxyProtocol.TrojanTls?node.CertificateKeyPath:null);
        return new(new(server,node,new(credential,configuration.RealityPrivateKey)),configuration,TimeSpan.FromSeconds(3));
    }
    private const string Inventory="DISTRO=ubuntu\nLIKE=debian\nARCH=x86_64\nSYSTEMD=1\nELEVATE=1\nINSTALLED=0\nFOREIGN=0\nOWNED=0\nCERT=1\nSTATE=inactive\nPID=0\nENABLED=disabled\n";
    [Theory][InlineData(ProxyProtocol.VlessReality)][InlineData(ProxyProtocol.Hysteria2)][InlineData(ProxyProtocol.TrojanTls)][InlineData(ProxyProtocol.Shadowsocks2022)]
    public async Task EveryProtocolUsesStagingValidationDedicatedUnitAndConfirmedStatus(ProxyProtocol protocol)
    {
        var request=Request(protocol);var session=new Session();var engine=new RemoteDeploymentService(new Factory(session),new ServerConfigurationGenerator(),new PinnedKernelReleaseCatalog());
        var outcome=await engine.DeployRemoteAsync(request);Assert.True(outcome.Success);Assert.Matches("^nekoproxy-[a-f0-9]{32}.service$",outcome.Unit!);
        var config=session.Uploads.Single(p=>p.Key.EndsWith("server.json")).Value;using var json=JsonDocument.Parse(config);Assert.Equal(443,json.RootElement.GetProperty("inbounds")[0].GetProperty("listen_port").GetInt32());
        Assert.Contains(session.Commands,c=>c.Contains("sha256sum -c"));Assert.Contains(session.Commands,c=>c.Contains(" check -c "));
        Assert.Contains(session.Commands,c=>c.Contains("systemctl is-active --quiet")&&c.Contains("ACTIVE=1"));
        Assert.DoesNotContain(session.Commands,c=>c.Contains("/etc/sing-box/config.json")||c.Contains("restart sing-box.service"));
        Assert.All(session.Commands,c=>Assert.DoesNotContain(request.Draft.Secrets.Credential,c));if(request.Draft.Secrets.RealityPrivateKey is {} key)Assert.All(session.Commands,c=>Assert.DoesNotContain(key,c));
        Assert.All(outcome.Steps,s=>Assert.NotEqual("进行中",s.State));Assert.True(session.Disposed);
    }
    [Theory][InlineData("FOREIGN=0","FOREIGN=1")][InlineData("ELEVATE=1","ELEVATE=0")][InlineData("SYSTEMD=1","SYSTEMD=0")][InlineData("ARCH=x86_64","ARCH=mips64")][InlineData("CERT=1","CERT=0")]
    public async Task UnsafePreflightBlocksAllRemoteWrites(string before,string after)
    {
        var session=new Session{InventoryText=Inventory.Replace(before,after)};var engine=new RemoteDeploymentService(new Factory(session),new ServerConfigurationGenerator(),new PinnedKernelReleaseCatalog());
        var result=await engine.DeployRemoteAsync(Request(ProxyProtocol.TrojanTls));Assert.False(result.Success);Assert.Empty(session.Uploads);Assert.Single(session.Commands);Assert.DoesNotContain(session.Commands,c=>c.Contains("install pinned"));
    }
    [Fact]public async Task OwnershipRequiresExplicitBackupPolicyAndStartupFailureAttemptsRecovery()
    {
        var session=new Session{InventoryText=Inventory.Replace("OWNED=0","OWNED=1").Replace("INSTALLED=0","INSTALLED=1")};var engine=new RemoteDeploymentService(new Factory(session),new ServerConfigurationGenerator(),new PinnedKernelReleaseCatalog());var r=Request();
        var denied=await engine.DeployRemoteAsync(r);Assert.False(denied.Success);Assert.Empty(session.Uploads);
        session.Commands.Clear();session.FailStartup=true;
        var failed=await engine.DeployRemoteAsync(r with{ExistingPolicy=ExistingDeploymentPolicy.BackupAndReplace});Assert.False(failed.Success);Assert.True(failed.RollbackAttempted);Assert.True(failed.RollbackSucceeded);
        Assert.Contains(session.Commands,c=>c.Contains("test -f")&&c.Contains("/.ready")&&c.Contains("restore this owned node only"));Assert.Contains(failed.Steps,s=>s.State=="失败");
    }
    [Fact]public async Task HashFailureNeverWritesServiceAndUnconfirmedActivationIsNotSuccess()
    {
        var session=new Session{FailChecksum=true};var engine=new RemoteDeploymentService(new Factory(session),new ServerConfigurationGenerator(),new PinnedKernelReleaseCatalog());
        var failed=await engine.DeployRemoteAsync(Request());Assert.False(failed.Success);Assert.DoesNotContain(session.Commands,c=>c.Contains("transactional activate"));
        session.FailChecksum=false;session.MissingActivation=true;failed=await engine.DeployRemoteAsync(Request());Assert.False(failed.Success);Assert.True(failed.RollbackAttempted);
    }
    [Fact]public void PortsAreCheckedForTheSelectedTransportAndOnlyOwnedPidMayShare()
    {
        var r=Request(ProxyProtocol.VlessReality);var text=Inventory+"SOCKET=udp UNCONN 0 0 [::]:443 [::]:* users:((\"other\",pid=900,fd=4))\n";
        Assert.True(RemoteDeploymentService.ParseInspection(text,r).PortAvailable);
        Assert.False(RemoteDeploymentService.ParseInspection(text,Request(ProxyProtocol.Hysteria2)).PortAvailable);
        Assert.True(RemoteDeploymentService.ParseInspection(Inventory+"PROC=/proc/net/udp\n",r).PortAvailable);
        text=Inventory.Replace("OWNED=0","OWNED=1").Replace("PID=0","PID=900")+"SOCKET=tcp LISTEN 0 0 [::]:443 [::]:* users:((\"sing-box\",pid=900,fd=4))\n";
        Assert.True(RemoteDeploymentService.ParseInspection(text,r).PortAvailable);Assert.False(RemoteDeploymentService.ParseInspection(text+"SOCKET=tcp LISTEN users:((\"other\",pid=901,fd=8))\n",r).PortAvailable);
    }
    [Fact]public void ArchFamilyAndPinnedArchitectureSelectionAreExplicit()
    {
        var i=RemoteDeploymentService.ParseInspection(Inventory.Replace("DISTRO=ubuntu","DISTRO=manjaro").Replace("LIKE=debian","LIKE=arch")+"PACMAN=1\n",Request());Assert.Equal("Pacman",i.PackageManager);
        var catalog=new PinnedKernelReleaseCatalog();Assert.Contains("linux-arm64",catalog.Get("aarch64").Url);Assert.Equal(64,catalog.Get("x86_64").Sha256.Length);Assert.Throws<ArgumentException>(()=>catalog.Get("i386"));
        Assert.Contains("openssl",new AptDistributionAdapter().PrerequisitePackages);Assert.Contains("openssl",new PacmanDistributionAdapter().PrerequisitePackages);
        Assert.Throws<ArgumentException>(()=>RemoteCommands.InstallKernel(new RemotePaths(Guid.NewGuid()),catalog.Get("x86_64"),"/tmp/nekoproxy.test1234","unknown"));
    }
    [Fact]public void CommandQuotingAndTransactionalGuardsDoNotRelyOnUserControlledNames()
    {
        Assert.Equal("'a'\"'\"'b'",RemoteCommands.Quote("a'b"));Assert.Throws<ArgumentException>(()=>RemoteCommands.Quote("a\nwhoami"));
        var r=Request();var paths=new RemotePaths(r.Draft.Node.Id);var script=RemoteCommands.Commit(paths,"/tmp/nekoproxy.12345678",paths.Directory+"/backups/test",true,true,true);
        Assert.True(script.IndexOf(".ready",StringComparison.Ordinal)<script.IndexOf("trap restore_node",StringComparison.Ordinal));
        Assert.Contains("trap restore_node EXIT HUP INT TERM",script);Assert.Contains(".had-config",script);Assert.Contains("systemctl is-active --quiet",script);
        Assert.DoesNotContain("pacman -Sy",RemoteCommands.InstallKernel(paths,new PinnedKernelReleaseCatalog().Get("x86_64"),"/tmp/nekoproxy.12345678","Pacman"));
    }
    [Fact]public void ResourceCountersUseRealDeltasAndMissingDataStaysUnknown()
    {
        var c=SshServerMonitor.Parse("UPTIME=100.5\ncpu 100 0 50 850 0 0 0 0 0 0\nMemTotal: 1024 kB\nMemAvailable: 512 kB\n eth0: 100 0 0 0 0 0 0 0 200 0 0 0 0 0 0 0\n lo: 999 0 0 0 0 0 0 0 999 0 0 0 0 0 0 0\n");
        Assert.Equal(1000,c.Total);Assert.Equal(850,c.Idle);Assert.Equal(512*1024,c.Memory);Assert.Equal(100,c.Rx);Assert.Equal(200,c.Tx);Assert.Null(SshServerMonitor.Parse("").Memory);
        Assert.Equal(1024*1024,SshServerMonitor.Parse("MemTotal: 1024 kB\nMemAvailable: 0 kB\n").Memory);
        Assert.Null(SshServerMonitor.Parse("MemTotal: 1024 kB\n").Memory);
    }
    private sealed class Factory(Session session):ISshConnectionFactory{public Task<ISshSession> ConnectAsync(ServerProfile s,TimeSpan t,CancellationToken token=default)=>Task.FromResult<ISshSession>(session);}
    private sealed class Session:ISshSession
    {
        public string InventoryText=Inventory;public bool FailStartup,FailChecksum,MissingActivation,Disposed;public List<string> Commands=[];public Dictionary<string,string> Uploads=[];
        public Task<string> ExecuteAsync(string command,CancellationToken token=default)
        {
            Commands.Add(command);token.ThrowIfCancellationRequested();
            if(command.Contains("readonly preflight"))return Task.FromResult(InventoryText);
            if(command.Contains("mktemp"))return Task.FromResult("/tmp/nekoproxy.abcdefgh12\n");
            if(command.Contains("install pinned")&&FailChecksum)throw new SshConnectionException(SshFailureKind.Command,"test checksum failure");
            if(command.Contains("transactional activate")){if(FailStartup)throw new SshConnectionException(SshFailureKind.Command,"test startup failure");return Task.FromResult(MissingActivation?"":"ACTIVE=1\n");}
            return Task.FromResult("");
        }
        public async Task UploadAsync(Stream data,string path,CancellationToken token=default){using var r=new StreamReader(data,Encoding.UTF8,leaveOpen:true);Uploads[path]=await r.ReadToEndAsync(token);}
        public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}
    }
}
