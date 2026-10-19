using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace NekoProxy.Desktop.ViewModels;

public enum NoticeLevel { Information, Success, Error }
public sealed record OperationNotice(string Message, NoticeLevel Level);
public partial class ToastViewModel(TimeSpan? visibleFor=null) : ObservableObject, IDisposable
{
    private CancellationTokenSource? _expiry;
    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private bool isError;
    [ObservableProperty] private string message="";
    [ObservableProperty] private string title="";
    public void Show(OperationNotice notice)
    {
        if(string.IsNullOrWhiteSpace(notice.Message))return;
        _expiry?.Cancel();_expiry?.Dispose();_expiry=new();
        Message=notice.Message;IsError=notice.Level==NoticeLevel.Error;
        Title=notice.Level switch { NoticeLevel.Success=>"操作完成", NoticeLevel.Error=>"需要处理", _=>"提示" };
        IsOpen=true;_=ExpireAsync(_expiry.Token);
    }
    private async Task ExpireAsync(CancellationToken token)
    { try { await Task.Delay(visibleFor??TimeSpan.FromMilliseconds(3060),token);IsOpen=false; } catch(OperationCanceledException){} }
    [RelayCommand] private void Dismiss(){_expiry?.Cancel();IsOpen=false;}
    public void Dispose(){_expiry?.Cancel();_expiry?.Dispose();_expiry=null;IsOpen=false;}
}
