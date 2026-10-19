using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;
namespace NekoProxy.Desktop.ViewModels;

public partial class AdvancedViewModel(IRemoteAdministration? administration,IInteractiveSshFactory? terminalFactory,Func<AppSettings> settings,IServerSecurityInspector? inspector=null):ObservableObject,IAsyncDisposable
{
    public ServerProfile? Server {get;private set;}
    public ProxyNode? Node {get;private set;}
    private CancellationTokenSource? _operation;
    private TaskCompletionSource? _completed;
    private bool _disposed;
    private IInteractiveSshSession? _terminal;
    private Action<string>? _outputHandler;
    private Action? _closedHandler;
    private int _revision;
    private int _terminalRevision;
    private readonly TerminalBuffer _buffer=new();
    private RemoteServiceAction? _confirmedAction;
    public Action<OperationNotice>? Feedback {get;set;}
    public event Action<Guid,Guid?,RemoteServiceAction,bool>? ServiceCompleted;
    [ObservableProperty] private bool hasServiceResult;
    public IReadOnlyList<string> Categories {get;}=["服务与日志","环境与连接","SSH 终端","安全与性能（只读）"];
    public System.Collections.ObjectModel.ObservableCollection<SecurityFinding> Findings {get;}=[];
    [ObservableProperty] private string inspectionSummary="主动检查当前服务器的认证、监听、隔离、证书与性能；不会更改服务器配置。包含三次 VPS 出站 ICMP 探测。";
    public bool Security=>CategoryIndex==3;
    [ObservableProperty] private int categoryIndex;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private bool hasConfirmation;
    [ObservableProperty] private string confirmationText="";
    [ObservableProperty] private string result="选择服务器后，主动执行检查或连接终端。";
    [ObservableProperty] private string terminalText="";
    [ObservableProperty] private string terminalInput="";
    [ObservableProperty] private string tcpPort="443";
    public bool Services=>CategoryIndex==0;
    public bool Environment=>CategoryIndex==1;
    public bool Terminal=>CategoryIndex==2;
    public string ConfirmButtonLabel=>_confirmedAction switch{RemoteServiceAction.Start=>"确认启动",RemoteServiceAction.Stop=>"确认停止",RemoteServiceAction.Restart=>"确认重启",_=>"确认操作"};
    public string Target=>Server is {} s?s.Name+" · "+s.Host+(Node is {} n?" · "+n.Name+" · "+n.Protocol.Display()+" :"+n.Port:""):"尚未选择服务器";
    public void Select(ServerProfile? server,ProxyNode? node)
    {
        var endpointChanged=Server?.Id!=server?.Id||Server?.Host!=server?.Host||Server?.SshPort!=server?.SshPort||Server?.Username!=server?.Username||Server?.Authentication!=server?.Authentication||Server?.PrivateKeyPath!=server?.PrivateKeyPath;
        if(endpointChanged||Node?.Id!=node?.Id){_revision++;_operation?.Cancel();HasServiceResult=false;Result="选择服务器后，主动执行检查或连接终端。";if(endpointChanged){Findings.Clear();InspectionSummary="主动检查当前服务器的认证、监听、隔离、证书与性能；不会更改服务器配置。包含三次 VPS 出站 ICMP 探测（1.1.1.1）。";}}
        if(endpointChanged)_=DisconnectAsync();
        Server=server;Node=node;TcpPort=(node?.Port??443).ToString();HasConfirmation=false;_confirmedAction=null;OnPropertyChanged(nameof(Target));OnPropertyChanged(nameof(ConfirmButtonLabel));
    }
    partial void OnCategoryIndexChanged(int value){foreach(var n in new[]{nameof(Services),nameof(Environment),nameof(Terminal),nameof(Security)})OnPropertyChanged(n);}
    [RelayCommand] private async Task InspectSecurityAsync()
    {
        if(Server is not {} server||inspector is null){Feedback?.Invoke(new("请先选择服务器。",NoticeLevel.Error));return;}
        var revision=_revision;
        await Operate(async token=>
        {
            InspectionSummary="正在执行只读检查，可随时取消。";
            var report=await inspector.InspectAsync(server,TimeSpan.FromSeconds(settings().SshTimeoutSeconds),token);token.ThrowIfCancellationRequested();if(revision!=_revision)return;
            Findings.Clear();foreach(var row in report.Findings)Findings.Add(row);
            InspectionSummary=$"检查完成 · {report.FinishedAt.LocalDateTime:g} · {Findings.Count} 项。未执行任何配置修改。";
            Result=InspectionSummary;Feedback?.Invoke(new("只读检查完成，请查看逐项证据与建议。",NoticeLevel.Success));
        });
        if(InspectionSummary.StartsWith("正在执行"))InspectionSummary="检查未完成："+Result;
    }
    [RelayCommand] private void RequestService(string action)
    {
        if(IsBusy||!Enum.TryParse<RemoteServiceAction>(action,out var selected))return;
        if(Server is null||Node is null&&selected!=RemoteServiceAction.Environment){Feedback?.Invoke(new("请先选择服务器与代理节点。",NoticeLevel.Error));return;}
        if(selected is RemoteServiceAction.Start or RemoteServiceAction.Stop or RemoteServiceAction.Restart)
        {_confirmedAction=selected;OnPropertyChanged(nameof(ConfirmButtonLabel));ConfirmationText="确认操作此 NekoProxy 节点服务："+Target+" · "+(selected==RemoteServiceAction.Start?"启动":selected==RemoteServiceAction.Stop?"停止":"重启");HasConfirmation=true;return;}
        _=RunAsync(selected);
    }
    [RelayCommand] private async Task ConfirmAsync(){var action=_confirmedAction;HasConfirmation=false;_confirmedAction=null;if(action is {} value)await RunAsync(value);}
    [RelayCommand] private void CancelConfirmation(){HasConfirmation=false;_confirmedAction=null;}
    private async Task RunAsync(RemoteServiceAction action)
    {
        if(Server is not {} server||administration is null||IsBusy)return;
        var node=Node;var revision=_revision;
        await Operate(async token=>{var r=await administration.RunAsync(server,node,action,TimeSpan.FromSeconds(settings().SshTimeoutSeconds),token);token.ThrowIfCancellationRequested();if(revision==_revision){HasServiceResult=true;Result=r.Summary+"\n"+r.Kind+"\n"+r.Detail;Feedback?.Invoke(new(r.Summary,r.Success?NoticeLevel.Success:NoticeLevel.Error));ServiceCompleted?.Invoke(server.Id,node?.Id,action,r.Success);}});
    }
    [RelayCommand] private async Task SshTestAsync()
    {if(Server is {} s&&administration is not null)await Operate(async token=>{var r=await administration.TestSshAsync(s,TimeSpan.FromSeconds(settings().SshTimeoutSeconds),token);token.ThrowIfCancellationRequested();Result=r.Summary+"\n"+r.Detail;Feedback?.Invoke(new(r.Summary,r.Success?NoticeLevel.Success:NoticeLevel.Error));});}
    [RelayCommand] private async Task TcpTestAsync()
    {if(Server is {} s&&administration is not null)await Operate(async token=>{if(!int.TryParse(TcpPort,out var port))throw new ArgumentException("请输入有效的 TCP 端口。");var r=await administration.TestTcpAsync(s,port,token);token.ThrowIfCancellationRequested();Result=r.Summary+"\n"+r.Detail;Feedback?.Invoke(new(r.Summary,r.Success?NoticeLevel.Success:NoticeLevel.Error));});}
    [RelayCommand] private async Task ConnectTerminalAsync()
    {
        if(Server is not {} server||terminalFactory is null||IsBusy)return;
        await DisconnectAsync();
        await Operate(async token=>{var session=await terminalFactory.OpenTerminalAsync(server,TimeSpan.FromSeconds(settings().SshTimeoutSeconds),token);if(token.IsCancellationRequested){await session.DisposeAsync();token.ThrowIfCancellationRequested();}_terminal=session;var generation=++_terminalRevision;_outputHandler=text=>Dispatcher.UIThread.Post(()=>{if(generation!=_terminalRevision)return;_buffer.Append(text);TerminalText=_buffer.Text;});session.Output+=_outputHandler;_closedHandler=()=>TerminalClosed(generation);session.Closed+=_closedHandler;_buffer.Clear();TerminalText="";session.StartReading();IsConnected=true;Result="SSH 终端已连接。输入不会保存到普通日志。";});
    }
    [RelayCommand] private async Task SendAsync(){if(_terminal is {} terminal&&IsConnected){var input=TerminalInput;TerminalInput="";await Operate(token=>terminal.WriteAsync(input+"\r",token));}}
    [RelayCommand] private async Task InterruptAsync(){if(_terminal is {} terminal)await Operate(token=>terminal.WriteAsync("\u0003",token));}
    private void TerminalClosed(int generation)=>Dispatcher.UIThread.Post(()=>{if(generation!=_terminalRevision)return;IsConnected=false;Result="SSH 终端已断开。请重新连接。";});
    [RelayCommand] public async Task DisconnectAsync(){_terminalRevision++;var terminal=_terminal;var handler=_outputHandler;var closed=_closedHandler;_terminal=null;_outputHandler=null;_closedHandler=null;IsConnected=false;TerminalInput="";if(Result.StartsWith("SSH 终端已连接",StringComparison.Ordinal))Result="SSH 终端已断开。";if(terminal is not null){terminal.Output-=handler;terminal.Closed-=closed;await terminal.DisposeAsync();}}
    [RelayCommand] private void CancelOperation()=>_operation?.Cancel();
    private async Task Operate(Func<CancellationToken,Task> action)
    {
        if(IsBusy||_disposed)return;var revision=_revision;IsBusy=true;_operation=new();_completed=new(TaskCreationOptions.RunContinuationsAsynchronously);
        try{await action(_operation.Token);}catch(OperationCanceledException){if(revision==_revision)Result="操作已取消。";}catch(SshConnectionException e){if(revision==_revision){Result=e.Message;Feedback?.Invoke(new(Result,NoticeLevel.Error));}}catch(ArgumentException e){if(revision==_revision){Result=e.Message;Feedback?.Invoke(new(Result,NoticeLevel.Error));}}catch{if(revision==_revision){Result="远程操作没有完成，请检查连接和服务状态。";Feedback?.Invoke(new(Result,NoticeLevel.Error));}}
        finally{_operation.Dispose();_operation=null;IsBusy=false;_completed.TrySetResult();}
    }
    public async ValueTask DisposeAsync(){_disposed=true;_operation?.Cancel();if(_completed is {} completion)await completion.Task;await DisconnectAsync();_buffer.Clear();TerminalText="";}
}

