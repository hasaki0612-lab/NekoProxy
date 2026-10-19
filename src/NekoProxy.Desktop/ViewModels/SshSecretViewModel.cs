using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;
namespace NekoProxy.Desktop.ViewModels;

public partial class SshSecretViewModel:ObservableObject,ISshSecretSource
{
    private readonly Dictionary<string,string> _session=[];
    private readonly SemaphoreSlim _gate=new(1,1);
    private TaskCompletionSource<string?>? _answer;
    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private string endpoint="";
    [ObservableProperty] private string password="";
    [ObservableProperty] private string title="SSH 登录密码";
    private static string Key(ServerProfile s)=>s.Id+"|"+s.Host+"|"+s.SshPort+"|"+s.Username+"|"+s.Authentication+"|"+s.PrivateKeyPath;
    private long _revision;
    public long Revision=>Interlocked.Read(ref _revision);
    public void SetSessionSecret(ServerProfile server,string value){lock(_session){if(_session.GetValueOrDefault(Key(server))==value)return;_session[Key(server)]=value;Interlocked.Increment(ref _revision);}}
    public void Forget(ServerProfile server){lock(_session){_session.Remove(Key(server));Interlocked.Increment(ref _revision);}}
    public void Clear(){lock(_session){_session.Clear();Interlocked.Increment(ref _revision);}Password="";}
    public async Task<string?> RequestAsync(ServerProfile server,CancellationToken token)
    {
        lock(_session)if(_session.TryGetValue(Key(server),out var value))return value;
        await _gate.WaitAsync(token);var answer=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Dispatcher.UIThread.InvokeAsync(()=>{_answer=answer;Password="";Title=server.Authentication==SshAuthenticationKind.Password?"SSH 登录密码":"SSH 私钥口令";Endpoint=server.Username+"@"+server.Host+":"+server.SshPort;IsOpen=true;});
            var result=await answer.Task.WaitAsync(token);if(!string.IsNullOrEmpty(result))SetSessionSecret(server,result);return result;
        }
        finally{await Dispatcher.UIThread.InvokeAsync(()=>{Password="";IsOpen=false;_answer=null;});_gate.Release();}
    }
    [RelayCommand] private void Accept(){if(!string.IsNullOrEmpty(Password))_answer?.TrySetResult(Password);}
    [RelayCommand] public void Cancel()=>_answer?.TrySetResult(null);
}
