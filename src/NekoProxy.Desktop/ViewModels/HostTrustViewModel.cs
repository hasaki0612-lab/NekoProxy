using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;
namespace NekoProxy.Desktop.ViewModels;
public partial class HostTrustViewModel:ObservableObject,IHostKeyApproval
{
    private readonly SemaphoreSlim _gate=new(1,1);
    private TaskCompletionSource<bool>? _answer;
    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private string endpoint="";
    [ObservableProperty] private string algorithm="";
    [ObservableProperty] private string fingerprint="";
    public async Task<bool> ConfirmAsync(HostKeyCandidate key,CancellationToken token)
    {
        await _gate.WaitAsync(token);var answer=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Dispatcher.UIThread.InvokeAsync(()=>{_answer=answer;Endpoint=(key.Host.Contains(':')?"["+key.Host+"]":key.Host)+":"+key.Port;Algorithm=key.Algorithm;Fingerprint=key.Fingerprint;IsOpen=true;});
            return await answer.Task.WaitAsync(token);
        }
        finally{await Dispatcher.UIThread.InvokeAsync(()=>{IsOpen=false;_answer=null;});_gate.Release();}
    }
    [RelayCommand] private void Accept()=>_answer?.TrySetResult(true);
    [RelayCommand] public void Cancel()=>_answer?.TrySetResult(false);
}
