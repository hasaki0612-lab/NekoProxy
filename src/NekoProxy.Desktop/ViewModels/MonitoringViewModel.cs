using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;
namespace NekoProxy.Desktop.ViewModels;

public sealed record MonitorChoice(string Label,int Value);
public partial class MonitoringViewModel(IServerMonitor monitor,IMonitoringHistoryStore history,Func<AppSettings> settings):ObservableObject,IAsyncDisposable
{
    private static readonly SemaphoreSlim SamplingGate=new(2,2);
    private CancellationTokenSource? _poll;
    private CancellationTokenSource _scope=new();
    private readonly List<Task> _pending=[];
    private bool _disposed;
    private Task _polling=Task.CompletedTask;
    private readonly SemaphoreSlim _single=new(1,1);
    private int _revision;
    private bool _visible;
    private readonly HashSet<string> _requiresExplicitRetry=[];
    private string Endpoint=>_server is {} s?s.Host.Trim().ToLowerInvariant()+"|"+s.SshPort:"";
    private ServerProfile? _server;private ProxyNode? _node;
    public IReadOnlyList<MonitorChoice> Intervals {get;}=[new("5 秒（推荐）",5),new("10 秒",10),new("30 秒",30),new("60 秒",60),new("手动刷新",0)];
    public IReadOnlyList<MonitorChoice> Ranges {get;}=[new("5 分钟",300),new("30 分钟",1800),new("6 小时",21600)];
    public IReadOnlyList<string> Metrics {get;}=["CPU 使用率","内存使用率","网络速率","磁盘使用率","SSH 连接耗时"];
    [ObservableProperty] private IReadOnlyList<MonitoringSample> samples=Array.Empty<MonitoringSample>();
    [ObservableProperty] private ServiceSnapshot snapshot=ServiceSnapshot.Disconnected;
    [ObservableProperty] private bool isPolling;
    [ObservableProperty] private bool isSampling;
    [ObservableProperty] private int intervalIndex;
    [ObservableProperty] private int rangeIndex;
    [ObservableProperty] private int metricIndex;
    [ObservableProperty] private string message="进入服务器监测后自动采样；离开页面会暂停。";
    public MonitorMetric Metric=>(MonitorMetric)Math.Clamp(MetricIndex,0,4);
    public int RangeSeconds=>Ranges[Math.Clamp(RangeIndex,0,2)].Value;
    public bool HasSamples=>Samples.Any(s=>s.Snapshot.Success);
    public bool HasCertificate=>_node?.Protocol is ProxyProtocol.TrojanTls or ProxyProtocol.Hysteria2;
    public string LastSample=>Snapshot.RefreshedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")??"—";
    public string ConnectionTime=>Snapshot.SshConnectMilliseconds is {} ms?$"{ms:F0} ms":"—";
    public string MemoryUsage=>Snapshot.MemoryBytes is {} n&&Snapshot.MemoryTotalBytes is {} total?$"{n/1048576d:F0} / {total/1048576d:F0} MiB":"—";
    public string DiskUsage=>Snapshot.DiskPercent is {} d?$"{d:F1}%":"—";
    public string SystemSummary=>string.Join(" · ",new[]{Snapshot.Distribution,Snapshot.Architecture,Snapshot.KernelVersion is {} v?"sing-box "+v:null}.Where(s=>!string.IsNullOrWhiteSpace(s)));
    public string SystemUptime=>Snapshot.SystemUptime is {} t?$"{t.Days} 天 {t.Hours} 小时":"—";
    public event Action<ServiceSnapshot>? Sampled;
    public event Action<int>? IntervalChanged;
    private bool _loadingPreferences;
    public void LoadPreferences(AppSettings value){_loadingPreferences=true;try{IntervalIndex=value.MonitorIntervalSeconds switch{10=>1,30=>2,60=>3,0=>4,_=>0};}finally{_loadingPreferences=false;}}
    public async Task SelectAsync(ServerProfile? server,ProxyNode? node,bool visible)
    {
        Stop();var revision=++_revision;_server=server;_node=node;_visible=visible;Snapshot=ServiceSnapshot.Disconnected;Samples=[];OnPropertyChanged(nameof(HasCertificate));
        if(server is null)return;
        try{var rows=await history.LoadAsync(server.Id);if(revision==_revision)Samples=rows;}
        catch{if(revision==_revision)Message="本地历史无法读取，原文件会保留。实时采样仍可使用。";}
        if(revision==_revision&&_visible&&!_disposed)await StartAutomaticallyAsync();
    }
    private Task StartAutomaticallyAsync(){if(_requiresExplicitRetry.Contains(Endpoint)){Message="自动采样已暂停。请主动点击开始监测，重新核实指纹或认证资料。";return Task.CompletedTask;}return StartAsync();}
    public void SetVisible(bool visible){var entered=visible&&!_visible;_visible=visible;if(!visible)Stop();else if(entered)_=StartAutomaticallyAsync();}
    [RelayCommand] public void Stop(){_poll?.Cancel();_scope.Cancel();_scope.Dispose();_scope=new();IsPolling=false;Message="采样已暂停；保留真实时间与历史缺口。";}
    [RelayCommand] private async Task StartAsync()
    {
        if(_disposed||_server is null||!_visible||IsPolling)return;
        _requiresExplicitRetry.Remove(Endpoint);
        Stop();var interval=Intervals[Math.Clamp(IntervalIndex,0,4)].Value;
        if(interval==0){await RefreshAsync();return;}
        _poll=CancellationTokenSource.CreateLinkedTokenSource(_scope.Token);var owner=_poll;
        IsPolling=true;_polling=LoopAsync(interval,owner);Track(_polling);await Task.CompletedTask;
    }
    [RelayCommand] public Task RefreshAsync(){if(_disposed)return Task.CompletedTask;var task=SampleAsync(_scope.Token,true);Track(task);return task;}
    private void Track(Task task){_pending.RemoveAll(t=>t.IsCompleted);_pending.Add(task);}
    public Task WaitForIdleAsync()=>Task.WhenAll(_pending.ToArray());
    private async Task LoopAsync(int interval,CancellationTokenSource owner)
    {
        var token=owner.Token;
        var failures=0;
        try
        {
            while(!token.IsCancellationRequested&&_visible)
            {
                var began=System.Diagnostics.Stopwatch.StartNew();
                await SampleAsync(token);failures=Snapshot.Success?0:Math.Min(failures+1,4);
                if(Snapshot.ConnectionFailure is SshFailureKind.HostRejected or SshFailureKind.HostChanged or SshFailureKind.Authentication or SshFailureKind.KeyFile)
                {_requiresExplicitRetry.Add(Endpoint);Message="自动采样已暂停。请主动点击开始监测，重新核实指纹或认证资料。";break;}
                // A 5-second cadence includes SSH/CPU measurement time, rather
                // than adding five seconds after each completed remote read.
                var period=Math.Min(300,interval*Math.Pow(2,failures));
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1,period-began.Elapsed.TotalSeconds)),token);
            }
        }
        catch(OperationCanceledException){}
        finally{if(ReferenceEquals(_poll,owner)){IsPolling=false;_poll=null;}owner.Dispose();}
    }
    private async Task SampleAsync(CancellationToken token,bool userRequested=false)
    {
        if(_disposed||token.IsCancellationRequested||_server is not {} server||(!_visible&&!userRequested)||!_single.Wait(0))return;
        var revision=_revision;var node=_node;var gate=false;
        try
        {
            IsSampling=true;await SamplingGate.WaitAsync(token);gate=true;
            var snap=await monitor.RefreshNodeAsync(server,node,settings(),token);token.ThrowIfCancellationRequested();
            if(revision!=_revision)return;Snapshot=snap;Sampled?.Invoke(snap);
            var expected=Intervals[Math.Clamp(IntervalIndex,0,4)].Value;var row=new MonitoringSample(snap.RefreshedAt??DateTimeOffset.UtcNow,snap,expected==0?Math.Max(5,settings().MonitorIntervalSeconds):expected);
            Samples=Samples.Append(row).Where(s=>s.Timestamp>DateTimeOffset.UtcNow.AddHours(-24)).TakeLast(10000).ToArray();
            Message=snap.Success?"正在显示服务器实际样本。SSH 耗时不是代理节点延迟。":snap.Message;
            try{await history.AppendAsync(server.Id,row,settings().HistoryRetentionHours,token);}catch(OperationCanceledException){throw;}catch{Message="采样已读取，但历史保存失败。原历史文件没有覆盖。";}
        }
        catch(OperationCanceledException){}
        catch{if(revision==_revision&&!token.IsCancellationRequested){Snapshot=ServiceSnapshot.Disconnected with{Message="采样失败；请查看 SSH 连接诊断。",RefreshedAt=DateTimeOffset.UtcNow};Samples=Samples.Append(new MonitoringSample(DateTimeOffset.UtcNow,Snapshot,Math.Max(5,settings().MonitorIntervalSeconds))).TakeLast(10000).ToArray();Message=Snapshot.Message;Sampled?.Invoke(Snapshot);}}
        finally{if(gate)SamplingGate.Release();IsSampling=false;_single.Release();}
    }
    partial void OnIntervalIndexChanged(int value){var running=IsPolling;Stop();if(!_loadingPreferences){IntervalChanged?.Invoke(Intervals[Math.Clamp(value,0,4)].Value);if(running)_=StartAsync();}}
    partial void OnRangeIndexChanged(int value)=>OnPropertyChanged(nameof(RangeSeconds));
    partial void OnMetricIndexChanged(int value)=>OnPropertyChanged(nameof(Metric));
    partial void OnSamplesChanged(IReadOnlyList<MonitoringSample> value)=>OnPropertyChanged(nameof(HasSamples));
    partial void OnSnapshotChanged(ServiceSnapshot value){foreach(var name in new[]{nameof(LastSample),nameof(ConnectionTime),nameof(MemoryUsage),nameof(DiskUsage),nameof(SystemSummary),nameof(SystemUptime)})OnPropertyChanged(name);}
    public async ValueTask DisposeAsync(){if(_disposed)return;_disposed=true;Stop();await Task.WhenAll(_pending);_scope.Dispose();}
}
