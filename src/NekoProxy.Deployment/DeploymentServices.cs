using NekoProxy.Core;

namespace NekoProxy.Deployment;

public sealed class AptDistributionAdapter : IDistributionAdapter
{
    public string Name => "APT";
    public bool Supports(string id) => id is "debian" or "ubuntu";
    public IReadOnlyList<string> PrerequisitePackages => ["curl", "ca-certificates", "tar", "openssl"];
    public string InstallPrerequisitesCommand => "apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends "+string.Join(' ',PrerequisitePackages);
}
public sealed class PacmanDistributionAdapter : IDistributionAdapter
{
    public string Name => "Pacman";
    public bool Supports(string id) => id is "arch";
    public IReadOnlyList<string> PrerequisitePackages => ["curl", "ca-certificates", "tar", "openssl"];
    public string InstallPrerequisitesCommand => "pacman -S --needed --noconfirm "+string.Join(' ',PrerequisitePackages);
}
public sealed class PendingDeploymentService : IDeploymentService
{
    public Task<DeploymentResult> DeployAsync(DeploymentPlan plan, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DeploymentResult(false, "远程部署尚未实现。没有连接服务器或修改任何远程配置。"));
}
public sealed class LinuxEnvironmentDetector(ISshConnectionFactory connections)
{
    public async Task<RemoteEnvironment> DetectAsync(ServerProfile server, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await using var session = await connections.ConnectAsync(server, timeout, cancellationToken);
        var distribution = await session.ExecuteAsync(". /etc/os-release && printf '%s' \"$ID\"", cancellationToken);
        var architecture = await session.ExecuteAsync("uname -m", cancellationToken);
        var uid = await session.ExecuteAsync("id -u", cancellationToken);
        var state = await session.ExecuteAsync("systemctl is-active sing-box 2>/dev/null || true", cancellationToken);
        return new(distribution.Trim(), architecture.Trim(), uid.Trim() == "0", state.Trim());
    }
}
