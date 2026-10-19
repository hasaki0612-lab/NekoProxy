namespace NekoProxy.Core;
public enum ProbeDirection { Inbound, Outbound, ServerConfiguration }
public enum ProbeState { Reachable, Partial, Timeout, Refused, DnsError, NotListening, Unsupported, PathRestrictionSuspected, InsufficientEvidence, Failed, NotTested }
public sealed record ProbeSource(string Id,string Name,string Region,string Operator,bool External=false);
public sealed record NetworkProbeResult(ProbeSource Source,ProbeDirection Direction,string Family,string Target,int? Port,string Protocol,
    ProbeState State,int Attempts,int Successes,double? MeanMilliseconds,double? P95Milliseconds,DateTimeOffset TestedAt,string Evidence)
{
    public string SuccessRate=>Attempts>0?$"{Successes}/{Attempts} · {100d*Successes/Attempts:F0}%":"静态检查";
    public string Label=>$"{Family} · {Protocol}"+(Port is {} p?$" :{p}":"");
    public string Timing=>Attempts>0 ? $"{Attempts} 次探测 · 成功 {SuccessRate}"+(MeanMilliseconds is {} ms?$" · 平均 {ms:F0} ms":" · 无成功耗时样本")+(P95Milliseconds is {} p?$" · P95 {p:F0} ms":"") : "静态检查 · "+StateLabel+" · 不计算成功率";
    public string SourceLabel=>$"{Source.Name} · {Source.Region} · {Source.Operator} · {TestedAt.ToLocalTime():MM-dd HH:mm:ss}";
    public string StateLabel=>State switch{ProbeState.Reachable=>"正常可达",ProbeState.Partial=>"部分可达",ProbeState.Timeout=>"连接超时",ProbeState.Refused=>"连接被拒绝",ProbeState.DnsError=>"DNS 异常",ProbeState.NotListening=>"服务未监听",ProbeState.Unsupported=>"不支持 / 未配置",ProbeState.PathRestrictionSuspected=>"疑似网络路径限制",ProbeState.Failed=>"检查失败",ProbeState.NotTested=>"尚未检测",_=>"证据不足，无法判断"};
}
public sealed record NetworkDiagnosticReport(Guid ServerId,DateTimeOffset TestedAt,IReadOnlyList<NetworkProbeResult> Results,string ExternalStatus)
{public IReadOnlyList<string> Findings {get;init;}=[];}
public interface INetworkDiagnostics
{
    Task<NetworkDiagnosticReport> RunAsync(ServerProfile server,string? domain,TimeSpan sshTimeout,IProgress<NetworkProbeResult>? progress=null,CancellationToken token=default);
}
// Extension contract. An implementation must receive explicit endpoint-specific
// consent; no external providers are registered by the application by default.
public interface IExternalNetworkProbe
{
    ProbeSource Source {get;}
    Task<IReadOnlyList<NetworkProbeResult>> RunAsync(ExternalProbeTarget target, bool endpointDisclosureAuthorized,CancellationToken token);
}
// Deliberately excludes SSH identity, private-key paths and node credentials.
public sealed record ExternalProbeTarget(string Address,IReadOnlyList<ExternalProbePort> Ports);
public sealed record ExternalProbePort(int Port,string Protocol);
public static class ProbeStatistics
{
    public static (double? Mean,double? P95) Summarize(IEnumerable<double> values)
    {var data=values.Where(x=>double.IsFinite(x)&&x>=0).Order().ToArray();return data.Length==0?(null,null):(data.Average(),data[Math.Clamp((int)Math.Ceiling(data.Length*.95)-1,0,data.Length-1)]);}
    public static bool HasPathRestrictionEvidence(IEnumerable<NetworkProbeResult> values)
    {
        var rows=values.Where(x=>x.Direction==ProbeDirection.Inbound&&x.Protocol=="TCP"&&x.Attempts>=3).ToArray();
        return rows.GroupBy(x=>(x.Target,x.Port,x.Family)).Any(group=>
            group.Any(x=>x.Successes==x.Attempts)&&group.Any(x=>x.State==ProbeState.Timeout&&x.Successes==0)&&
            group.Select(x=>x.Source.Id).Distinct().Count()>=2);
    }
}
