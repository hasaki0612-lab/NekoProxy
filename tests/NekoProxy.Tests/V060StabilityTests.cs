using NekoProxy.Core;
using NekoProxy.Desktop.ViewModels;
using Xunit;
namespace NekoProxy.Tests;
public sealed class V060StabilityTests
{
    private static ServerProfile Server()=>new(Guid.NewGuid(),"fixture","192.0.2.1","","",22,"root",null,[]);
    [Fact] public async Task RejectedHostStopsPollingAndAutomaticPageReentryUntilExplicitRetry()
    {
        var probe=new RejectedMonitor();var vm=new MonitoringViewModel(probe,new History(),()=>new());
        await vm.SelectAsync(Server(),null,true);await vm.WaitForIdleAsync();Assert.False(vm.IsPolling);Assert.Equal(1,probe.Calls);
        vm.SetVisible(false);vm.SetVisible(true);await vm.WaitForIdleAsync();Assert.Equal(1,probe.Calls);
        await vm.StartCommand.ExecuteAsync(null);await vm.WaitForIdleAsync();Assert.Equal(2,probe.Calls);await vm.DisposeAsync();
    }
    [Fact] public async Task RepeatedToastResetsOneExpiryAndAutomaticallyDisappears()
    {
        using var toast=new ToastViewModel(TimeSpan.FromMilliseconds(160));var visibleChanges=0;
        toast.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(toast.IsOpen))visibleChanges++;};
        var notice=new OperationNotice("fixture result",NoticeLevel.Success);toast.Show(notice);await Task.Delay(90);toast.Show(notice);
        await Task.Delay(100);Assert.True(toast.IsOpen);Assert.Equal(1,visibleChanges);
        await Task.Delay(160);Assert.False(toast.IsOpen);Assert.Equal(2,visibleChanges);
    }
    [Fact] public async Task ExplicitTerminalDisconnectClearsConnectedStatusText()
    {
        var factory=new TerminalFactory();var vm=new AdvancedViewModel(null,factory,()=>new());vm.Select(Server(),null);
        await vm.ConnectTerminalCommand.ExecuteAsync(null);Assert.True(vm.IsConnected);Assert.Contains("已连接",vm.Result);
        await vm.DisconnectAsync();Assert.False(vm.IsConnected);Assert.Contains("已断开",vm.Result);Assert.True(factory.Session.Disposed);await vm.DisposeAsync();
    }
    private sealed class RejectedMonitor:IServerMonitor
    {public int Calls;public Task<ServiceSnapshot> RefreshAsync(ServerProfile s,AppSettings p,CancellationToken token=default){Calls++;return Task.FromResult(ServiceSnapshot.Disconnected with{ConnectionFailure=SshFailureKind.HostRejected});}}
    private sealed class History:IMonitoringHistoryStore
    {public Task<IReadOnlyList<MonitoringSample>> LoadAsync(Guid id,CancellationToken token=default)=>Task.FromResult<IReadOnlyList<MonitoringSample>>([]);public Task AppendAsync(Guid id,MonitoringSample s,int hours=24,CancellationToken token=default)=>Task.CompletedTask;}
    private sealed class TerminalFactory:IInteractiveSshFactory
    {public Terminal Session {get;}=new();public Task<IInteractiveSshSession> OpenTerminalAsync(ServerProfile s,TimeSpan timeout,CancellationToken token=default)=>Task.FromResult<IInteractiveSshSession>(Session);}
    private sealed class Terminal:IInteractiveSshSession
    {public bool Disposed;public event Action<string>? Output {add{}remove{}}public void StartReading(){}public Task WriteAsync(string text,CancellationToken token=default)=>Task.CompletedTask;public void Resize(uint c,uint r){}public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}}
}
