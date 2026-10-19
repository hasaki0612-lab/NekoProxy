using System.Text;
using System.Text.Json;
using NekoProxy.Core;

namespace NekoProxy.Exporters;

public static class ExportValidation
{
    public static void Check(ClientNodeConfiguration n)=>ProtocolParameters.ValidateClient(n);
    public static string Link(ClientNodeConfiguration n)
    {
        Check(n);
        var host = n.Host.Contains(':') ? $"[{n.Host}]" : n.Host;
        var name = Uri.EscapeDataString(n.NodeName);
        if (n.Protocol == ProxyProtocol.Hysteria2)
            return $"hysteria2://{Uri.EscapeDataString(n.Credential)}@{host}:{n.Port}/?sni={Uri.EscapeDataString(n.Sni)}#{name}";
        if(n.Protocol==ProxyProtocol.TrojanTls) return $"trojan://{Uri.EscapeDataString(n.Credential)}@{host}:{n.Port}?security=tls&type=tcp&sni={Uri.EscapeDataString(n.Sni)}#{name}";
        if(n.Protocol==ProxyProtocol.Shadowsocks2022) return $"ss://{n.Method}:{Uri.EscapeDataString(n.Credential)}@{host}:{n.Port}#{name}";
        return $"vless://{n.Credential}@{host}:{n.Port}?encryption=none&security=reality&type=tcp&flow=xtls-rprx-vision&sni={Uri.EscapeDataString(n.Sni)}&fp=chrome&pbk={Uri.EscapeDataString(n.RealityPublicKey!)}&sid={Uri.EscapeDataString(n.ShortId!)}#{name}";
    }
}
public sealed class ShadowrocketExporter : IClientExporter
{
    public ClientType Client => ClientType.Shadowrocket;
    public string Extension => ".txt";
    public string Generate(ClientNodeConfiguration n)
    {
        if(n.Policy is not null)PrivacyPolicyValidation.Check(n.Policy,Client,n.Host);
        if(n.Protocol==ProxyProtocol.Shadowsocks2022)throw new ArgumentException("SS2022 请使用 Mihomo 或 sing-box；Shadowrocket 版本兼容性尚未验证。");
        return ExportValidation.Link(n)+Environment.NewLine;
    }
    public string GenerateLink(ClientNodeConfiguration n) { _=Generate(n);return ExportValidation.Link(n); }
}
public sealed class SingBoxExporter : IClientExporter
{
    public ClientType Client => ClientType.SingBox;
    public string Extension => ".json";
    public string? GenerateLink(ClientNodeConfiguration n) => ExportValidation.Link(n);
    public string Generate(ClientNodeConfiguration n)
    {
        if(n.Policy is not null)return PrivacyConfigurations.SingBox(n);
        ExportValidation.Check(n);
        var outbound = new Dictionary<string, object?>
        {
            ["type"] = n.Protocol switch {ProxyProtocol.VlessReality=>"vless",ProxyProtocol.Hysteria2=>"hysteria2",ProxyProtocol.TrojanTls=>"trojan",_=>"shadowsocks"},
            ["tag"] = n.NodeName, ["server"] = n.Host, ["server_port"] = n.Port
        };
        var tls = new Dictionary<string, object?> { ["enabled"] = true, ["server_name"] = n.Sni };
        if (n.Protocol == ProxyProtocol.VlessReality)
        {
            outbound["uuid"] = n.Credential; outbound["flow"] = "xtls-rprx-vision";
            tls["utls"] = new { enabled = true, fingerprint = "chrome" };
            tls["reality"] = new { enabled = true, public_key = n.RealityPublicKey, short_id = n.ShortId };
        }
        else outbound["password"] = n.Credential;
        if(n.Protocol==ProxyProtocol.Shadowsocks2022)outbound["method"]=n.Method;else outbound["tls"]=tls;
        return JsonSerializer.Serialize(new { log = new { level = "warn" },
            inbounds = new[] { new { type = "mixed", tag = "mixed-in", listen = "127.0.0.1", listen_port = 2080 } },
            outbounds = new[] { outbound } }, new JsonSerializerOptions { WriteIndented = true });
    }
}
public sealed class MihomoExporter : IClientExporter
{
    public ClientType Client => ClientType.Mihomo;
    public string Extension => ".yaml";
    public string? GenerateLink(ClientNodeConfiguration n) => ExportValidation.Link(n);
    private static string Q(string s) => JsonSerializer.Serialize(s);
    public string Generate(ClientNodeConfiguration n)
    {
        if(n.Policy is not null)return PrivacyConfigurations.Mihomo(n);
        ExportValidation.Check(n);
        var b = new StringBuilder($"mixed-port: 7890\nallow-lan: false\nmode: rule\nproxies:\n  - name: {Q(n.NodeName)}\n    type: {(n.Protocol switch {ProxyProtocol.VlessReality=>"vless",ProxyProtocol.Hysteria2=>"hysteria2",ProxyProtocol.TrojanTls=>"trojan",_=>"ss"})}\n    server: {Q(n.Host)}\n    port: {n.Port}\n");
        if (n.Protocol == ProxyProtocol.VlessReality)
            b.Append($"    uuid: {Q(n.Credential)}\n    network: tcp\n    tls: true\n    udp: true\n    flow: xtls-rprx-vision\n    servername: {Q(n.Sni)}\n    client-fingerprint: chrome\n    reality-opts:\n      public-key: {Q(n.RealityPublicKey!)}\n      short-id: {Q(n.ShortId!)}\n");
        else if(n.Protocol==ProxyProtocol.Shadowsocks2022)b.Append($"    cipher: {Q(n.Method)}\n    password: {Q(n.Credential)}\n    udp: true\n");
        else b.Append($"    password: {Q(n.Credential)}\n    sni: {Q(n.Sni)}\n    skip-cert-verify: false\n");
        b.Append($"proxy-groups:\n  - name: Proxy\n    type: select\n    proxies: [{Q(n.NodeName)}]\nrules:\n  - MATCH,Proxy\n");
        return b.ToString();
    }
}
public sealed class ExportService(IEnumerable<IClientExporter> exporters) : IExportService
{
    public Task<ExportResult> ExportLinkAsync(ClientNodeConfiguration node,ClientType client,string directory,CancellationToken cancellationToken=default)
    {
        var link=exporters.Single(x=>x.Client==client).GenerateLink(node with{Policy=null})??throw new ArgumentException("该客户端不支持此节点链接。");
        return WriteAsync(node,client,directory,".txt",link+Environment.NewLine,link,"node-link",cancellationToken);
    }
    public static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
        var cleaned = new string(value.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (string.IsNullOrEmpty(cleaned)) return "unnamed";
        var stem = cleaned.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9')) cleaned = "_" + cleaned;
        return cleaned[..Math.Min(cleaned.Length, 80)];
    }
    public async Task<ExportResult> ExportAsync(ClientNodeConfiguration node, ClientType client, string directory, CancellationToken cancellationToken = default)
    {
        var exporter = exporters.Single(x => x.Client == client);
        var content = exporter.Generate(node);
        return await WriteAsync(node,client,directory,exporter.Extension,content,exporter.GenerateLink(node with{Policy=null}),node.Policy is null?"node":"policy-"+node.Policy.Mode,cancellationToken);
    }
    private static async Task<ExportResult> WriteAsync(ClientNodeConfiguration node,ClientType client,string directory,string extension,string content,string? link,string scope,CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetFullPath(directory), SafeName(node.ServerName), SafeName(node.NodeName));
        Directory.CreateDirectory(folder);
        // Each explicit export gets a unique suffix; never overwrite another endpoint.
        var id = Guid.NewGuid().ToString("N")[..8];
        var file = Path.Combine(folder, $"{SafeName(node.ServerName)}__{SafeName(node.NodeName)}__{node.Protocol}__{client}__{scope}__p{node.Port}__{id}{extension}");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            await using var stream = new FileStream(file, options);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(content.AsMemory(), cancellationToken);
        }
        catch { if (File.Exists(file)) File.Delete(file); throw; }
        return new(file, link);
    }
}
