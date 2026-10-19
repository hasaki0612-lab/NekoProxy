using NekoProxy.Core;
using NekoProxy.Deployment;
namespace NekoProxy.Desktop;
internal static class DeploymentScriptVerification
{
    public static async Task GenerateAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var node=new ProxyNode(Guid.Parse("12345678-1234-1234-1234-123456789012"),"SCRIPT TEST",ProxyProtocol.Shadowsocks2022);
        var server=new ServerProfile(Guid.NewGuid(),"LOCAL TEST ONLY","127.0.0.1","","",22,"root","",[node]);
        var client=new ClientNodeConfiguration(server.Name,node.Name,server.Host,443,node.Protocol,ShadowsocksKeys.Generate(ShadowsocksKeys.DefaultMethod),"");
        var request=new RemoteDeploymentRequest(new(server,node,new(client.Credential)),new(client),TimeSpan.FromSeconds(3));
        var p=new RemotePaths(node.Id);var stage="/tmp/nekoproxy.abcdefgh12";var checkpoint=p.Directory+"/backups/test";
        var scripts=new Dictionary<string,string>{["probe.sh"]=RemoteCommands.Probe(request),["commit.sh"]=RemoteCommands.Commit(p,stage,checkpoint,true,true,true),["rollback.sh"]=RemoteCommands.Privileged(RemoteCommands.Rollback(p,checkpoint,true,true,true)),["apt-install.sh"]=RemoteCommands.InstallKernel(p,new PinnedKernelReleaseCatalog().Get("x86_64"),stage,"APT"),["pacman-install.sh"]=RemoteCommands.InstallKernel(p,new PinnedKernelReleaseCatalog().Get("aarch64"),stage,"Pacman")};
        foreach(var item in scripts)await File.WriteAllTextAsync(Path.Combine(directory,item.Key),item.Value.Replace("\r\n","\n")+"\n");
    }
}
