namespace NekoProxy.Core;

public enum SshAuthenticationKind {PrivateKey,Password}
public interface ISshSecretSource
{
    long Revision=>0;
    Task<string?> RequestAsync(ServerProfile server,CancellationToken token);
    void SetSessionSecret(ServerProfile server,string secret);
    void Clear();
    void Forget(ServerProfile server){}
}
public interface IInteractiveSshSession:IAsyncDisposable
{
    event Action<string>? Output;
    event Action? Closed { add {} remove {} }
    void StartReading();
    Task WriteAsync(string input,CancellationToken token=default);
    void Resize(uint columns,uint rows);
}
public interface IInteractiveSshFactory
{
    Task<IInteractiveSshSession> OpenTerminalAsync(ServerProfile server,TimeSpan timeout,CancellationToken token=default);
}
public enum CertificateChallenge {Http01,TlsAlpn01}
public sealed record AutomaticCertificateOptions(string Email,CertificateChallenge Challenge=CertificateChallenge.Http01,bool Staging=true,bool DomainAuthorized=false);
public sealed record CertificateHealth(string Domain,string State,DateTimeOffset? ExpiresAt,string? Issuer,string RenewalStatus,DateTimeOffset CheckedAt)
{public int? RemainingDays=>ExpiresAt is {} expiry?(int)Math.Floor((expiry-CheckedAt).TotalDays):null;}
public sealed record MonitoringSample(DateTimeOffset Timestamp,ServiceSnapshot Snapshot,int ExpectedIntervalSeconds=30);
public interface IMonitoringHistoryStore
{
    Task<IReadOnlyList<MonitoringSample>> LoadAsync(Guid serverId,CancellationToken token=default);
    Task AppendAsync(Guid serverId,MonitoringSample sample,int retentionHours=24,CancellationToken token=default);
}
public enum RemoteServiceAction {Start,Stop,Restart,CheckConfiguration,RecentLogs,Environment,Certificate}
public sealed record RemoteToolResult(bool Success,string Summary,string Detail,string Kind);
public interface IRemoteAdministration
{
    Task<RemoteToolResult> RunAsync(ServerProfile server,ProxyNode? node,RemoteServiceAction action,TimeSpan timeout,CancellationToken token=default);
    Task<RemoteToolResult> TestTcpAsync(ServerProfile server,int port,CancellationToken token=default);
    Task<RemoteToolResult> TestSshAsync(ServerProfile server,TimeSpan timeout,CancellationToken token=default);
}
