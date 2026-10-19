using System.Net;
using System.Text.Json.Serialization;

namespace NekoProxy.Core;

public enum ProxyProtocol { VlessReality, Hysteria2, Shadowsocks2022, TrojanTls }
public enum ClientType { Shadowrocket, Mihomo, SingBox }
public static class ProtocolNames
{
    public static string Display(this ProxyProtocol protocol) => protocol switch
    {
        ProxyProtocol.VlessReality => "VLESS + REALITY",
        ProxyProtocol.Hysteria2 => "Hysteria 2",
        ProxyProtocol.TrojanTls => "Trojan + TLS",
        _ => "Shadowsocks 2022"
    };
}

// A server is a VPS; its nodes represent independent protocol endpoints.
// Neither authentication material nor server private keys belong in this metadata.
public sealed record ProxyNode(Guid Id, string Name, ProxyProtocol Protocol, int Port = 443)
{
    public AutomaticCertificateOptions? AutomaticCertificate {get;init;}
    public bool ParametersConfigured { get; init; }
    public string? ServiceUnit { get; init; }
    public DateTimeOffset? LastDeployment { get; init; }
    public bool NeedsDeployment {get;init;}
    public NodeVerification? Verification {get;init;}
    public string Sni { get; init; } = "";
    public string Method { get; init; } = ShadowsocksKeys.DefaultMethod;
    public string RealityPublicKey { get; init; } = "";
    public string ShortId { get; init; } = "";
    public string CertificatePath { get; init; } = "";
    public string CertificateKeyPath { get; init; } = "";
    [JsonIgnore] public string EndpointSummary=>Protocol.Display()+" · :"+Port;
    [JsonIgnore] public string DeploymentLabel=>NeedsDeployment?"参数待重新部署":ServiceUnit is null?"尚未部署":"已有部署记录";
}
public sealed record ServerProfile(Guid Id, string Name, string Host, string Location,
    string Notes, int SshPort, string Username, string? PrivateKeyPath, List<ProxyNode> Nodes):System.ComponentModel.INotifyPropertyChanged
{
    public SshAuthenticationKind Authentication {get;init;}
    public string Ipv6Address {get;init;}="";
    [JsonIgnore] public string LocationAndIp => $"{Location} · {Host}";
    [JsonIgnore] public string ProtocolSummary => Nodes.Count == 0 ? "尚未添加代理节点" :
        string.Join(" · ", Nodes.Select(x => x.Protocol.Display()).Distinct());
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    [JsonIgnore] public ServiceSnapshot? LastObservation {get;private set;}
    [JsonIgnore] public string LatencyDisplay => LastObservation?.Success==true&&LastObservation.SshConnectMilliseconds is {} ms?$"SSH {ms:F0} ms":"—";
    [JsonIgnore] public string ConnectionSummary=>LastObservation is {} value?value.SshState+" · "+value.ServiceState:"尚未检测";
    public void Observe(ServiceSnapshot snapshot){LastObservation=snapshot;PropertyChanged?.Invoke(this,new(nameof(LatencyDisplay)));PropertyChanged?.Invoke(this,new(nameof(ConnectionSummary)));}
}
public sealed record AppSettings
{
    public ClientPrivacyPolicy ClientPolicy {get;init;} = new();
    public bool SimplifiedVisuals {get;init;}
    public int MonitorIntervalSeconds {get;init;}=5;
    public int HistoryRetentionHours {get;init;}=24;
    public string ExportDirectory { get; init; } = ApplicationDirectories.DefaultExportDirectory;
    public string Theme { get; init; } = "system";
    public string Language { get; init; } = "简体中文";
    public int SshTimeoutSeconds { get; init; } = 15;
    public string KernelVersionPolicy { get; init; } = "固定稳定版本（部署前确认）";
    public bool OnboardingHandled { get; init; }
    public int OnboardingVersion { get; init; }
    public string? OnboardingOutcome { get; init; }
    public bool ReduceMotion { get; init; }
    public double WindowWidth { get; init; } = 1060;
    public double WindowHeight { get; init; } = 720;
    public bool WindowMaximized { get; init; }
    public bool AutoCheckUpdates { get; init; } = true;
    public Dictionary<string,string> KnownHosts { get; init; } = [];
    public DateTimeOffset? LastUpdateAttempt { get; init; }
    public DateTimeOffset? LastUpdateSuccess { get; init; }
    public string? LatestKnownVersion { get; init; }
    public string? LatestReleasePage { get; init; }
    public string? IgnoredVersion { get; init; }
    public DateTimeOffset? RemindUpdateAfter { get; init; }
    public string WindowPreset { get; init; } = "standard";
}
public sealed record ServiceSnapshot(string SshState, string ServiceState, double? CpuPercent,
    long? MemoryBytes, long? UploadBytesPerSecond, long? DownloadBytesPerSecond,
    TimeSpan? Uptime, DateTimeOffset? RefreshedAt, string Message)
{
    public string? Distribution {get;init;}
    public string? Architecture {get;init;}
    public string? KernelVersion {get;init;}
    public TimeSpan? SystemUptime {get;init;}
    public long? MemoryTotalBytes {get;init;}
    public double? MemoryPercent {get;init;}
    public double? DiskPercent {get;init;}
    public long? DiskTotalBytes {get;init;}
    public long? DiskUsedBytes {get;init;}
    public double? SshConnectMilliseconds {get;init;}
    public bool Success {get;init;}
    public SshFailureKind? ConnectionFailure {get;init;}
    public CertificateHealth? Certificate {get;init;}
    public static ServiceSnapshot Disconnected { get; } = new("未连接", "尚未检测", null, null,
        null, null, null, null, "连接服务器后可获取服务状态与资源数据。");
}
public sealed record NetworkSample(DateTimeOffset Timestamp, long UploadBytesPerSecond, long DownloadBytesPerSecond);
public sealed record RemoteEnvironment(string Distribution, string Architecture, bool IsRoot, string ServiceState);
public sealed record DeploymentPlan(ProxyProtocol Protocol, ServerProfile Server, string Summary);
public sealed record DeploymentResult(bool Success, string Message);
public sealed record ClientNodeConfiguration(string ServerName, string NodeName, string Host, int Port,
    ProxyProtocol Protocol, string Credential, string Sni, string? RealityPublicKey = null,
    string? ShortId = null, string Method = ShadowsocksKeys.DefaultMethod)
{
    public ClientPrivacyPolicy? Policy {get;init;}
}
public sealed record ExportResult(string FilePath, string? NodeLink);

public static class ProfileValidation
{
    public static string? Validate(ServerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name)) return "请输入服务器名称。";
        if (!IPAddress.TryParse(profile.Host, out _) && (Uri.CheckHostName(profile.Host)!=UriHostNameType.Dns || !profile.Host.Contains('.'))) return "请输入有效的 IPv4、IPv6 或服务器域名。";
        if(!string.IsNullOrWhiteSpace(profile.Ipv6Address)&&(!IPAddress.TryParse(profile.Ipv6Address,out var ipv6)||ipv6.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetworkV6))return "请输入有效的服务器 IPv6 地址。";
        if (profile.SshPort is < 1 or > 65535) return "SSH 端口必须在 1–65535 之间。";
        if (string.IsNullOrWhiteSpace(profile.Username)) return "请输入 SSH 用户名。";
        if (profile.PrivateKeyPath is { } keyPath && (keyPath.Contains("PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase) || keyPath.Contains('\n') || keyPath.Contains('\r')))
            return "请输入私钥文件路径，不要粘贴私钥内容。";
        if (profile.Nodes.Any(n => string.IsNullOrWhiteSpace(n.Name) || n.Port is < 1 or > 65535))
            return "节点名称不能为空，端口必须在 1–65535 之间。";
        return null;
    }
}
