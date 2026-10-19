using System.Text.Json;
using NekoProxy.Core;

namespace NekoProxy.Infrastructure;

public sealed class JsonFileStore<T>(string path, Func<T> emptyFactory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<T> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return emptyFactory();
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken)
                ?? throw new InvalidDataException("本地数据文件为空。请备份并检查该文件。");
        }
        finally { _gate.Release(); }
    }
    public async Task SaveAsync(T value, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); _gate.Release(); }
    }
    public async Task<T> UpdateAsync(Func<T, T> change)
    {
        await _gate.WaitAsync(); var temporary = path + ".tmp";
        try
        {
            var current = File.Exists(path) ? JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), Options) ?? throw new InvalidDataException() : emptyFactory();
            var next = change(current); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(next, Options)); File.Move(temporary, path, true); return next;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); _gate.Release(); }
    }
}
public sealed class JsonServerRepository(string directory) : IServerRepository
{
    private readonly JsonFileStore<List<ServerProfile>> _store = new(Path.Combine(directory, "servers.json"), () => []);
    public async Task<IReadOnlyList<ServerProfile>> LoadAsync(CancellationToken cancellationToken = default) => await _store.LoadAsync(cancellationToken);
    public Task SaveAsync(IReadOnlyList<ServerProfile> servers, CancellationToken cancellationToken = default) => _store.SaveAsync(servers.ToList(), cancellationToken);
}
public sealed class JsonSettingsStore(string directory) : ISettingsStore
{
    private readonly JsonFileStore<AppSettings> _store = new(Path.Combine(directory, "settings.json"), () => new());
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => _store.LoadAsync(cancellationToken);
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => _store.SaveAsync(settings, cancellationToken);
    public Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> change) => _store.UpdateAsync(change);
}
// Explicit sample metadata only. Documentation IPs cannot be mistaken for live VPS data.
public static class SampleProfiles
{
    public static List<ServerProfile> Create() =>
    [
        new(Guid.NewGuid(), "Evoxt-HK-01", "203.0.113.10", "香港", "用于演示多节点服务器的本地资料；尚未连接远程服务。", 22, "root", null,
            [new(Guid.NewGuid(), "HK · REALITY", ProxyProtocol.VlessReality), new(Guid.NewGuid(), "HK · HY2", ProxyProtocol.Hysteria2, 8443)]),
        new(Guid.NewGuid(), "JP-Osaka-01", "198.51.100.28", "日本 · 大阪", "用于演示单节点服务器。所有监控数值均为空。", 22, "root", null,
            [new(Guid.NewGuid(), "Osaka · HY2", ProxyProtocol.Hysteria2)]),
        new(Guid.NewGuid(), "SG-Test-01", "192.0.2.56", "新加坡", "待部署的服务器资料。", 22, "root", null, [])
    ];
}
public sealed class DisconnectedServerMonitor : IServerMonitor
{
    public Task<ServiceSnapshot> RefreshAsync(ServerProfile server, AppSettings settings, CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceSnapshot.Disconnected with { Message = "远程监控尚未接入。本地资料已保留，没有生成模拟监控数值。" });
}
