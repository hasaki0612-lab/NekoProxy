using System.Security.Cryptography;
namespace NekoProxy.Core;
public sealed record PreparedCredentials(string Credential,string? PrivateKey=null,string PublicKey="",string ShortId="");
// A book belongs to one draft, never to the process or another node. Switching
// protocol/method and retrying reuses that draft's material until explicit rotation.
public sealed class DraftCredentials(IRealityKeyGenerator keys)
{
    private readonly Dictionary<(ProxyProtocol,string),PreparedCredentials> _items=[];
    private static (ProxyProtocol,string) Key(ProxyProtocol protocol,string method)=>(protocol,protocol==ProxyProtocol.Shadowsocks2022?method:"");
    public PreparedCredentials Get(ProxyProtocol protocol,string method)=>_items.TryGetValue(Key(protocol,method),out var value)?value:Regenerate(protocol,method);
    public void Remember(ProxyProtocol protocol,string method,PreparedCredentials value)=>_items[Key(protocol,method)]=value;
    public PreparedCredentials Regenerate(ProxyProtocol protocol,string method)
    {
        PreparedCredentials result;
        if(protocol==ProxyProtocol.VlessReality){var k=keys.Generate();result=new(k.Uuid,k.PrivateKey,k.PublicKey,k.ShortId);}
        else result=new(protocol==ProxyProtocol.Shadowsocks2022?ShadowsocksKeys.Generate(method):Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));
        Remember(protocol,method,result);return result;
    }
    public void Clear()=>_items.Clear();
}
public enum RealityTargetState { Unverified, Verified, Failed }
public sealed record RealityTargetResult(string Host,RealityTargetState State,string Reason,DateTimeOffset? CheckedAt=null)
{public string StateLabel=>State switch{RealityTargetState.Verified=>"已验证",RealityTargetState.Failed=>"验证失败",_=>"尚未验证"};}
public interface IRealityTargetService
{
    Task<RealityTargetResult> CheckAsync(ServerProfile server,string host,TimeSpan timeout,string? secret=null,CancellationToken token=default);
}
