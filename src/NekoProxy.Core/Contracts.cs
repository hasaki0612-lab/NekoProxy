namespace NekoProxy.Core;

public interface IServerRepository
{
    Task<IReadOnlyList<ServerProfile>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyList<ServerProfile> servers, CancellationToken cancellationToken = default);
}
public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
    async Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> change)
    {
        var next = change(await LoadAsync()); await SaveAsync(next); return next;
    }
}
public interface IServerMonitor
{
    Task<ServiceSnapshot> RefreshAsync(ServerProfile server, AppSettings settings, CancellationToken cancellationToken = default);
    Task<ServiceSnapshot> RefreshNodeAsync(ServerProfile server,ProxyNode? node,AppSettings settings,CancellationToken token=default)=>RefreshAsync(server,settings,token);
}
public interface ISshSession : IAsyncDisposable
{
    TimeSpan? ConnectionElapsed=>null;
    ConnectionAttempt? Attempt=>null;
    Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default);
    Task<string> ExecuteEnvironmentAsync(string command,CancellationToken token=default)=>ExecuteAsync(command,token);
    Task UploadAsync(Stream data,string path,CancellationToken token=default)=>throw new NotSupportedException();
}
public interface ISshConnectionFactory
{
    Task<ISshSession> ConnectAsync(ServerProfile server, TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<ISshSession> ConnectWithKeyAsync(ServerProfile server,TimeSpan timeout,string? passphrase,CancellationToken token=default)=>ConnectAsync(server,timeout,token);
}
public interface IDistributionAdapter
{
    bool Supports(string distributionId);
    string Name { get; }
    IReadOnlyList<string> PrerequisitePackages { get; }
    string InstallPrerequisitesCommand { get; }
}
public interface IDeploymentService
{
    Task<DeploymentResult> DeployAsync(DeploymentPlan plan, CancellationToken cancellationToken = default);
}
public interface IClientExporter
{
    ClientType Client { get; }
    string Extension { get; }
    string Generate(ClientNodeConfiguration node);
    string? GenerateLink(ClientNodeConfiguration node);
}
public interface IExportService
{
    Task<ExportResult> ExportLinkAsync(ClientNodeConfiguration node, ClientType client, string directory,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<ExportResult> ExportAsync(ClientNodeConfiguration node, ClientType client, string directory,
        CancellationToken cancellationToken = default);
}
public interface IPlatformActions
{
    Task OpenUrlAsync(string url) => throw new NotSupportedException();
    Task CopyAsync(string text);
    Task OpenDirectoryAsync(string directory);
}
public sealed record RealityKeyMaterial(string Uuid, string PrivateKey, string PublicKey, string ShortId);
public interface IRealityKeyGenerator { RealityKeyMaterial Generate(); }
