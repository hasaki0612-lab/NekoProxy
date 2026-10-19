using System.Text.Json.Serialization;
namespace NekoProxy.Core;

public enum ClientVerificationState { Unverified, Passed, Failed }
// Evidence belongs to a configuration revision, not to the mere presence of a unit.
// Older records deserialize without evidence and are deliberately not backfilled.
public sealed record NodeVerification
{
    public DateTimeOffset? GeneratedAt {get;init;}
    public DateTimeOffset? CoreCheckedAt {get;init;}
    public DateTimeOffset? DeployedAt {get;init;}
    public DateTimeOffset? ServiceStartedAt {get;init;}
    public ClientVerificationState ClientState {get;init;}
    public DateTimeOffset? ClientCheckedAt {get;init;}
    public string ClientSource {get;init;}="";
    [JsonIgnore] public string ClientLabel=>ClientState switch{ClientVerificationState.Passed=>"客户端连接验证成功",ClientVerificationState.Failed=>"客户端连接验证失败",_=>"客户端尚未验证"};
    [JsonIgnore] public string Summary=>string.Join("\n",GeneratedAt is null?"配置生成尚无记录":"配置已生成",CoreCheckedAt is null?"核心检查尚无记录":"核心检查通过",DeployedAt is null?"服务部署尚无记录":"服务已部署",ServiceStartedAt is null?"服务启动尚无记录":"部署时 systemd 已确认运行",ClientLabel);
}
