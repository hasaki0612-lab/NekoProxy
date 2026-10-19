using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NekoProxy.Infrastructure;
using NekoProxy.Desktop.ViewModels;
namespace NekoProxy.Desktop;
internal static class InteractiveVerification
{
    // No UI actions or fabricated results. This opt-in harness only supplies the
    // already-authorized test session secret to an isolated interactive GUI run.
    public static async Task PrepareAsync(MainViewModel main)
    {
        if(!Program.Arguments.Contains("--interactive-qa")||!Program.Arguments.Contains("--verification-title"))return;
        var data=Array.IndexOf(Program.Arguments,"--data-dir");var secret=Array.IndexOf(Program.Arguments,"--session-secret-file");
        if(data<0||secret<0)throw new InvalidOperationException("Isolated QA arguments required.");
        var root=Path.GetFullPath(Program.Arguments[data+1]);var normal=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NekoProxy");
        if(root.Equals(normal,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("QA cannot use ordinary application data.");
        var settings=await new JsonSettingsStore(root).LoadAsync();var bytes=new WindowsSecretProtector().Unprotect(await File.ReadAllBytesAsync(Program.Arguments[secret+1]));
        var value=Encoding.UTF8.GetString(bytes);CryptographicOperations.ZeroMemory(bytes);
        void Set(){foreach(var server in main.Servers)if(settings.KnownHosts.ContainsKey(SshConnectionFactory.Endpoint(server)))main.SshSecrets.SetSessionSecret(server,value);}
        Set();main.Servers.CollectionChanged+=(_,_)=>Set();
        main.Deployment.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(DeploymentViewModel.ConnectionDetail))File.AppendAllText(Path.Combine(root,"connection-events.txt"),DateTimeOffset.UtcNow.ToString("O")+"\n"+main.Deployment.ConnectionDetail+"\n\n");};
        // All captures are of the attached native view. Read-only button actions
        // and real deployment still require the normal UI command path.
    }
}
