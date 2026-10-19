using NekoProxy.Core;
namespace NekoProxy.Deployment;
public sealed class RealityTargetService(ISshConnectionFactory connections):IRealityTargetService
{
    // Versioned local candidates. No permanent availability claim: every draft
    // must check from its own VPS and deployment rechecks the selected target.
    public static IReadOnlyList<string> Candidates {get;}=["www.microsoft.com","www.cloudflare.com","www.apple.com"];
    public const string CandidateVersion="2026-10-15";
    public async Task<RealityTargetResult> CheckAsync(ServerProfile server,string host,TimeSpan timeout,string? secret=null,CancellationToken token=default)
    {
        if(Uri.CheckHostName(host)!=UriHostNameType.Dns||!host.Contains('.'))return new(host,RealityTargetState.Failed,"请输入有效目标域名。",DateTimeOffset.UtcNow);
        await using var session=await connections.ConnectWithKeyAsync(server,timeout,secret,token);
        var q=RemoteCommands.Quote;
        var output=await session.ExecuteAsync("if ! command -v openssl >/dev/null || ! command -v timeout >/dev/null; then printf 'TOOLS=missing'; else timeout 12 openssl s_client -connect "+q(host+":443")+" -servername "+q(host)+" -tls1_3 -groups X25519 -verify_return_error -verify_hostname "+q(host)+" </dev/null >/dev/null 2>&1; code=$?; printf 'EXIT=%s' \"$code\"; fi",token);
        var success=output.Trim()=="EXIT=0";
        return new(host,success?RealityTargetState.Verified:RealityTargetState.Failed,success?"VPS 已验证 TLS 1.3、X25519 与证书主机名；代理握手仍需客户端验证。":output.Contains("missing")?"VPS 缺少 OpenSSL / timeout，未执行兼容性检测。":output.Contains("124")?"目标握手超时。":"目标 TLS、证书或网络检查未通过。",DateTimeOffset.UtcNow);
    }
}
