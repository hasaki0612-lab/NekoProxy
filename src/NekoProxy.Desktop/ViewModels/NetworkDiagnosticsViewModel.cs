using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;
namespace NekoProxy.Desktop.ViewModels;
public partial class NetworkDiagnosticsViewModel(INetworkDiagnostics? diagnostics,ConnectionJournal? journal,Func<AppSettings> settings,IPlatformActions platform):ObservableObject
{
    private ServerProfile? _server;
    private CancellationTokenSource? _operation;
    private TaskCompletionSource? _completion;
    public async Task CancelAndWaitAsync(){_operation?.Cancel();if(_completion is {} completed)await completed.Task;}
    private long _revision;
    public ObservableCollection<NetworkProbeResult> Inbound {get;}=[];
    public ObservableCollection<NetworkProbeResult> Outbound {get;}=[];
    public ObservableCollection<NetworkProbeResult> Configuration {get;}=[];
    public string Overview
    {
        get
        {
            var rows=Inbound.Concat(Outbound).Concat(Configuration).ToArray();
            if(rows.Length==0)return "尚无测量结果；入站与出站会分别呈现。";
            return $"已获得 {rows.Length} 项证据 · {rows.Count(r=>r.State==ProbeState.Reachable)} 项正常 · {rows.Count(r=>r.State is ProbeState.InsufficientEvidence or ProbeState.Unsupported)} 项证据不足或未配置 · {rows.Count(r=>r.State is not (ProbeState.Reachable or ProbeState.InsufficientEvidence or ProbeState.Unsupported))} 项需要检查";
        }
    }
    private void NotifyOverview()=>OnPropertyChanged(nameof(Overview));
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string domain="";
    [ObservableProperty] private string status="选择服务器后，可执行低频诊断。检测不会改变网络或服务配置。";
    [ObservableProperty] private string connectionDetail="尚无连接尝试记录。";
    public string ExternalNotice=>"缺少中国大陆网络测量数据。外部探测点尚未接入；服务器地址不会上传给第三方。";
    public void Select(ServerProfile? server)
    {
        _operation?.Cancel();_revision++;_server=server;Inbound.Clear();Outbound.Clear();Configuration.Clear();NotifyOverview();Domain=server?.Nodes.FirstOrDefault(n=>n.Protocol is ProxyProtocol.Hysteria2 or ProxyProtocol.TrojanTls)?.Sni??"";Status="准备就绪；只检测 SSH 与此服务器已记录节点端口。";RefreshConnection();
    }
    [RelayCommand] private void RefreshConnection()
    {var attempts=_server is null?[]:journal?.ForServer(_server.Id)??[];ConnectionDetail=attempts.Count==0?"尚无连接尝试记录。":string.Join("\n\n",attempts.Take(8).Select(a=>a.Summary+"\n"+string.Join("\n",a.Records.Select(r=>r.Display))));}
    [RelayCommand] private async Task RunAsync()
    {
        if(IsBusy||_server is null||diagnostics is null)return;
        var revision=_revision;IsBusy=true;_operation=new();_completion=new(TaskCreationOptions.RunContinuationsAsynchronously);Inbound.Clear();Outbound.Clear();Configuration.Clear();Status="正在探测本机入站路径与 VPS 状态…";
        try
        {
            var progress=new Progress<NetworkProbeResult>(row=>{if(revision!=_revision)return;(row.Direction==ProbeDirection.Inbound?Inbound:row.Direction==ProbeDirection.Outbound?Outbound:Configuration).Add(row);NotifyOverview();});
            var report=await diagnostics.RunAsync(_server,Domain,TimeSpan.FromSeconds(settings().SshTimeoutSeconds),progress,_operation.Token);
            if(revision==_revision)Status=$"检测完成 · {report.TestedAt.ToLocalTime():HH:mm:ss} · {report.Results.Count} 项独立证据；请分别查看入站、出站与服务状态。"+string.Join("\n",report.Findings.Prepend(""));
        }
        catch(OperationCanceledException){if(revision==_revision)Status="诊断已取消；已完成的结果保留。";}
        catch(Exception){if(revision==_revision)Status="诊断未完成，请核对输入并查看连接记录。";}
        finally{_operation.Dispose();_operation=null;IsBusy=false;if(revision==_revision)RefreshConnection();_completion.TrySetResult();}
    }
    [RelayCommand] private void Cancel()=>_operation?.Cancel();
    [RelayCommand] private async Task CopySummaryAsync()
    {
        // Sharing intentionally excludes raw output, endpoints and usernames.
        var summary=Inbound.Concat(Outbound).Concat(Configuration).Select(r=>new{r.Source.Name,r.Direction,r.Family,r.Port,r.Protocol,r.State,r.Attempts,r.Successes,r.MeanMilliseconds,r.P95Milliseconds,r.TestedAt});
        await platform.CopyAsync(JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));Status="已复制脱敏结果摘要（不包含地址、用户名或原始输出）。";
    }
}
