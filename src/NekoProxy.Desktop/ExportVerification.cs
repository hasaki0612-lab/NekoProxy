using NekoProxy.Core;
using NekoProxy.Deployment;
using NekoProxy.Exporters;
namespace NekoProxy.Desktop;
// Explicit development fixture command. It does not run during normal use or deployment.
internal static class ExportVerification
{
    public static async Task GenerateAsync(string directory)
    {
        var keys = new RealityKeyGenerator().Generate();
        var service = new ExportService([new SingBoxExporter(), new MihomoExporter(), new ShadowrocketExporter()]);
        foreach (var protocol in new[] { ProxyProtocol.VlessReality, ProxyProtocol.Hysteria2 })
        {
            var node = new ClientNodeConfiguration("Test-Documentation-IP", protocol.Display(), "192.0.2.1", 443,
                protocol, protocol == ProxyProtocol.VlessReality ? keys.Uuid : "demonstration-only", "example.com", keys.PublicKey, keys.ShortId);
            foreach (var client in Enum.GetValues<ClientType>()) await service.ExportAsync(node, client, directory);
        }
    }
}
