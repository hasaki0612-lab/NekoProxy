using System.ComponentModel;
namespace NekoProxy.Desktop.ViewModels;
public partial class MainViewModel
{
    private async Task HandleServiceCompletedAsync(Guid serverId,Guid? nodeId,NekoProxy.Core.RemoteServiceAction action,bool success)
    {
        if(!success||SelectedServer?.Id!=serverId||SelectedNode?.Id!=nodeId||action==NekoProxy.Core.RemoteServiceAction.RecentLogs)return;
        try
        {
            if(action==NekoProxy.Core.RemoteServiceAction.CheckConfiguration&&SelectedServer is {} server&&SelectedNode is {} node&&!node.NeedsDeployment)
            {
                var updated=node with{Verification=(node.Verification??new()) with{CoreCheckedAt=DateTimeOffset.UtcNow}};
                await PersistUpdatedServerAsync(server with{Nodes=server.Nodes.Select(n=>n.Id==node.Id?updated:n).ToList()});
            }
            await Monitoring.RefreshAsync();
        }
        catch {Notify("服务操作已完成，但状态刷新或验证记录保存失败。请重新检查本地存储与连接。",NoticeLevel.Error);}
    }
    public string NodeConfigStatus=>SelectedNode?.NeedsDeployment==true?"配置检查：参数已修改，需要重新检查":SelectedNode?.Verification?.CoreCheckedAt is {} at?$"配置检查：核心通过 · {at.ToLocalTime():MM-dd HH:mm:ss}":"配置检查：尚未由核心验证";
    public string NodeRuntimeStatus=>Snapshot.Success?$"服务运行：{Snapshot.ServiceState} · {Snapshot.RefreshedAt?.ToLocalTime():MM-dd HH:mm:ss}":SelectedNode?.Verification?.ServiceStartedAt is {} at?$"服务运行：部署时已启动 · {at.ToLocalTime():MM-dd HH:mm:ss}（未刷新）":"服务运行：尚未读取当前状态";
    public string NodeClientEvidence=>NodeClientStatus+(SelectedNode?.Verification?.ClientCheckedAt is {} at?$" · {at.ToLocalTime():MM-dd HH:mm:ss}":"");
    public ToastViewModel Toast {get;}=new();
    private INotifyPropertyChanged? _feedbackEditor,_feedbackNode;
    private void EditorFeedback(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName=="Error")
        {
            var text=sender is ServerEditorViewModel s?s.Error:sender is NodeEditorViewModel n?n.Error:"";
            if(text.Length>0)Toast.Show(new(text,NoticeLevel.Error));
        }
    }
    private void WireEditorFeedback(ServerEditorViewModel? value)
    {if(_feedbackEditor is not null)_feedbackEditor.PropertyChanged-=EditorFeedback;_feedbackEditor=value;if(value is not null)value.PropertyChanged+=EditorFeedback;}
    private void WireNodeFeedback(NodeEditorViewModel? value)
    {if(_feedbackNode is not null)_feedbackNode.PropertyChanged-=EditorFeedback;_feedbackNode=value;if(value is not null)value.PropertyChanged+=EditorFeedback;}
    private void Notify(string message,NoticeLevel level=NoticeLevel.Success){Status=message;Toast.Show(new(message,level));}
}
