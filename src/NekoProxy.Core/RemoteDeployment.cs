namespace NekoProxy.Core;

public sealed record HostKeyCandidate(string Host,int Port,string Algorithm,string Fingerprint);
public interface IHostKeyApproval
{
    Task<bool> ConfirmAsync(HostKeyCandidate key,CancellationToken token);
}
public enum SshFailureKind { KeyFile,Authentication,Timeout,Network,HostChanged,HostRejected,Permission,Command }
public sealed class SshConnectionException(SshFailureKind kind,string message):Exception(message)
{
    public SshFailureKind Kind {get;}=kind;
    public ConnectionAttempt? Attempt {get;init;}
    public int? ExitCode {get;init;}
    public string? SafeStandardError {get;init;}
}
public enum ExistingDeploymentPolicy { Cancel,BackupAndReplace }
public sealed record RemoteInspection(string Distribution,string Architecture,bool Systemd,bool CanElevate,
    bool SingBoxInstalled,bool OwnedConfiguration,bool ForeignConfiguration,bool PortAvailable,bool CertificatesReadable,
    string ServiceState,string PackageManager,DateTimeOffset CheckedAt)
{
    public bool ServiceEnabled {get;init;}
    public bool ConfigRootExists {get;init;}
    public bool BinaryRootExists {get;init;}
    public bool AcmeDnsMatches {get;init;}=true;
    public bool AcmePortAvailable {get;init;}=true;
    public bool CanDeploy=>Systemd&&CanElevate&&!ForeignConfiguration&&PortAvailable&&CertificatesReadable&&AcmeDnsMatches&&AcmePortAvailable&&PackageManager.Length>0&&Architecture is "x86_64" or "aarch64";
}
public sealed record RemoteDeploymentRequest(LocalNodeDraft Draft,ServerNodeConfiguration Configuration,TimeSpan Timeout,
    ExistingDeploymentPolicy ExistingPolicy=ExistingDeploymentPolicy.Cancel,string? KeyPassphrase=null);
public sealed record DeploymentProgress(string Step,string State,string Detail);
public sealed record RemoteDeploymentOutcome(bool Success,string Message,string? Unit,RemoteInspection? Inspection,IReadOnlyList<DeploymentProgress> Steps,bool RollbackAttempted=false,bool RollbackSucceeded=false)
{
    public bool CoreChecked {get;init;}
    public bool ConfigurationGenerated {get;init;}
    public bool ServiceStarted {get;init;}
}
public interface IRemoteDeploymentService : IDeploymentService
{
    Task<RemoteInspection> InspectHostAsync(ServerProfile server,ProxyNode node,TimeSpan timeout,string? passphrase=null,CancellationToken token=default);
    Task<RemoteInspection> InspectAsync(RemoteDeploymentRequest request,CancellationToken token=default);
    Task<RemoteDeploymentOutcome> DeployRemoteAsync(RemoteDeploymentRequest request,IProgress<DeploymentProgress>? progress=null,CancellationToken token=default);
}
public sealed record KernelReleaseAsset(string Version,string Architecture,string Url,string Sha256);
public interface IKernelReleaseCatalog { KernelReleaseAsset Get(string architecture); }
