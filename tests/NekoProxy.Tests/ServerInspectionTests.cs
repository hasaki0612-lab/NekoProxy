using NekoProxy.Core;
using NekoProxy.Deployment;
using Xunit;
namespace NekoProxy.Tests;
public class ServerInspectionTests
{
    [Fact]public void UnknownSshStateCannotPass()=>Assert.Equal(InspectionState.NotChecked,ServerSecurityInspector.EvaluateSsh("").Item1);
    [Theory][InlineData("permitrootlogin yes\npasswordauthentication no")][InlineData("permitrootlogin no\npasswordauthentication yes")]
    public void PasswordOrRootLoginIsAttention(string s)=>Assert.Equal(InspectionState.Attention,ServerSecurityInspector.EvaluateSsh(s+"\nSSH_CONFIG_MODE=644 OWNER=root").Item1);
    [Theory][InlineData("0% packet loss",InspectionState.Normal)][InlineData("10% packet loss",InspectionState.Attention)][InlineData("100% packet loss",InspectionState.Attention)][InlineData("PING_UNAVAILABLE",InspectionState.NotChecked)]
    public void LossIsNotMistakenForZeroLoss(string text,InspectionState expected)=>Assert.Equal(expected,ServerSecurityInspector.EvaluatePerformance(text).Item1);
    [Fact]public void WritableSshConfigCannotPass()=>Assert.Equal(InspectionState.Attention,ServerSecurityInspector.EvaluateSsh("permitrootlogin no\npasswordauthentication no\nSSH_CONFIG_MODE=666 OWNER=root").Item1);
    [Fact]public void PrivateDestinationAccessAndRootCannotPass()=>Assert.Equal(InspectionState.Attention,ServerSecurityInspector.EvaluateNode("User=root\nPRIVATE_DESTINATIONS_BLOCKED=false\nCONFIG_MODE=600 OWNER=root").Item1);
    [Fact]public void MissingCertificateChainCannotPass()=>Assert.Equal(InspectionState.Attention,ServerSecurityInspector.EvaluateCertificate("EXPIRY_30D=valid\nPUBLIC_CA_CHAIN=not_verified").Item1);
    [Fact]public void CertificateFileChecksDoNotClaimLiveHandshake()=>Assert.Contains("不代表线上握手",ServerSecurityInspector.EvaluateCertificate("EXPIRY_30D=valid\nPUBLIC_CA_CHAIN=verified").Item2);
    [Fact]public async Task InspectorUsesBoundedReadOnlyProbesAndNoSecrets()
    {
        var factory=new Factory();var node=new ProxyNode(Guid.NewGuid(),"node",ProxyProtocol.Shadowsocks2022);node=node with{ServiceUnit=new RemotePaths(node.Id).Unit};
        var server=new ServerProfile(Guid.NewGuid(),"fixture","192.0.2.1","","",22,"root",null,[node]);
        var report=await new ServerSecurityInspector(factory).InspectAsync(server,TimeSpan.FromSeconds(2));
        Assert.Equal(6,report.Findings.Count);Assert.Equal(5,factory.Commands.Count);
        foreach(var cmd in factory.Commands)foreach(var forbidden in new[]{"systemctl restart","systemctl stop","sysctl -w","iptables -A","nft add","apt install","private_key","password="})Assert.DoesNotContain(forbidden,cmd);
        Assert.Contains("PRIVATE_DESTINATIONS_BLOCKED",factory.Commands.Last());
    }
    private sealed class Factory:ISshConnectionFactory
    {
        public List<string> Commands {get;}=[];
        public Task<ISshSession> ConnectAsync(ServerProfile s,TimeSpan t,CancellationToken c=default)=>Task.FromResult<ISshSession>(new Session(Commands));
    }
    private sealed class Session(List<string> commands):ISshSession
    {
        public Task<string> ExecuteAsync(string cmd,CancellationToken c=default){commands.Add(cmd);return Task.FromResult("unavailable fixture");}
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
