using System.Collections.Concurrent;
namespace NekoProxy.Core;

public enum ConnectionStage { Input, Dns, Tcp, Negotiation, HostKey, Authentication, Session, Environment, ServerBanner }
public enum AttemptState { Running, Succeeded, Failed, Cancelled, Skipped }
public sealed record ConnectionStageRecord(ConnectionStage Stage, DateTimeOffset Started, DateTimeOffset? Ended,
    AttemptState State, string Detail, string? ErrorType = null, int? ExitCode = null)
{
    public double? Milliseconds => Ended is {} end ? (end-Started).TotalMilliseconds : null;
    public string Label => Stage switch { ConnectionStage.Input=>"地址与认证输入", ConnectionStage.Dns=>"DNS 解析", ConnectionStage.Tcp=>"TCP 预连接", ConnectionStage.ServerBanner=>"SSH 实际连接 / 等待 banner", ConnectionStage.Negotiation=>"SSH 密钥交换", ConnectionStage.HostKey=>"主机指纹核验", ConnectionStage.Authentication=>"身份认证", ConnectionStage.Session=>"会话建立", _=>"远程环境检查" };
    public string Display => $"{Label} · {State} · {Milliseconds:F0} ms · {Detail}";
}
// Records contain IDs and bounded diagnostic facts, never credentials or command text.
public sealed class ConnectionAttempt(Guid serverId)
{
    private readonly object _sync=new();
    private readonly List<ConnectionStageRecord> _records=[];
    public Guid Id {get;}=Guid.NewGuid();
    public Guid ServerId {get;}=serverId;
    public DateTimeOffset Started {get;}=DateTimeOffset.UtcNow;
    public ConnectionStage Current {get;private set;}
    public AttemptState State {get;private set;}=AttemptState.Running;
    public IReadOnlyList<ConnectionStageRecord> Records {get {lock(_sync)return _records.ToArray();}}
    public void Begin(ConnectionStage stage,string detail="") {lock(_sync){Current=stage;_records.Add(new(stage,DateTimeOffset.UtcNow,null,AttemptState.Running,detail));}}
    public void End(AttemptState state=AttemptState.Succeeded,string detail="",string? errorType=null,int? exitCode=null)
    {lock(_sync){if(_records.Count==0)return;var last=_records[^1];if(last.State==AttemptState.Running)_records[^1]=last with{Ended=DateTimeOffset.UtcNow,State=state,Detail=detail,ErrorType=errorType,ExitCode=exitCode};}}
    public void Finish(AttemptState state)=>State=state;
    public string Summary => $"尝试 {Id.ToString("N")[..8]} · {Started.ToLocalTime():HH:mm:ss} · {State}";
}
public sealed class ConnectionJournal
{
    private readonly ConcurrentQueue<ConnectionAttempt> _attempts=new();
    public ConnectionAttempt Start(Guid serverId){var attempt=new ConnectionAttempt(serverId);_attempts.Enqueue(attempt);while(_attempts.Count>100)_attempts.TryDequeue(out _);return attempt;}
    public IReadOnlyList<ConnectionAttempt> ForServer(Guid id)=>_attempts.Where(x=>x.ServerId==id).Reverse().ToArray();
    public ConnectionAttempt? Latest(Guid id)=>ForServer(id).FirstOrDefault();
}
