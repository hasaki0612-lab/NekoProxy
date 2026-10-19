namespace NekoProxy.Core;
public enum InspectionState { Normal, Attention, NotChecked, Unsupported }
public sealed record SecurityFinding(string Id,string Area,string Title,InspectionState State,string Evidence,string Recommendation,DateTimeOffset CheckedAt)
{
    public string StateLabel=>State switch{InspectionState.Normal=>"正常",InspectionState.Attention=>"需要关注",InspectionState.NotChecked=>"未检查",_=>"不支持"};
}
public sealed record ServerInspectionReport(Guid ServerId,DateTimeOffset StartedAt,DateTimeOffset FinishedAt,IReadOnlyList<SecurityFinding> Findings);
public interface IServerSecurityInspector
{
    Task<ServerInspectionReport> InspectAsync(ServerProfile server,TimeSpan timeout,CancellationToken token=default);
}
