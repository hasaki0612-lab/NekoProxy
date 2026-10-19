using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NekoProxy.Core;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace NekoProxy.Infrastructure;

public interface ISecretProtector
{
    string Name { get; }
    byte[] Protect(byte[] value);
    byte[] Unprotect(byte[] value);
}

// Current-user DPAPI; this implementation and its on-disk format remain Windows-only.
public sealed class WindowsSecretProtector : ISecretProtector
{
    public string Name => OperatingSystem.IsWindows() ? "Windows DPAPI · CurrentUser" : "Unavailable";
    public byte[] Protect(byte[] value) => Transform(value, false);
    public byte[] Unprotect(byte[] value) => Transform(value, true);
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Transform(byte[] value, bool decrypt)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A platform credential provider is required.");
        var input = new Blob { Size = value.Length, Data = Marshal.AllocHGlobal(value.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(value, 0, input.Data, value.Length);
            var ok = decrypt ? CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            for (var i = 0; i < input.Size; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { for (var i = 0; i < output.Size; i++) Marshal.WriteByte(output.Data, i, 0); LocalFree(output.Data); }
        }
    }
}

public sealed class CredentialVault(string directory, ISecretProtector protector)
{
    private readonly JsonFileStore<Dictionary<string, string>> _store = new(Path.Combine(directory, "credentials.json"), () => []);
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<NodeSecrets> GetNodeSecretsAsync(Guid node)=>NodeSecretsCodec.Decode(await GetAsync(node));
    public Task SetNodeSecretsAsync(Guid node,NodeSecrets secrets)=>SetAsync(node,NodeSecretsCodec.Encode(secrets));
    public async Task SetClientCredentialAsync(Guid node,string credential)
    {
        await _gate.WaitAsync();
        byte[]? bytes=null;
        try
        {
            var previous=await GetNodeSecretsAsync(node);
            bytes=Encoding.UTF8.GetBytes(NodeSecretsCodec.Encode(previous with{Credential=credential}));
            var encrypted = await Task.Run(() => protector.Protect(bytes));
            await _store.UpdateAsync(values=>{values[node.ToString()]=Convert.ToBase64String(encrypted);return values;});
        }
        finally{if(bytes is not null)CryptographicOperations.ZeroMemory(bytes);_gate.Release();}
    }
    public async Task RemoveAsync(Guid node)
    {
        await _gate.WaitAsync();
        try { await _store.UpdateAsync(values => { values.Remove(node.ToString()); return values; }); }
        finally { _gate.Release(); }
    }
    public async Task PruneUnreferencedAsync(IEnumerable<Guid> referenced)
    {
        var keep=referenced.ToHashSet();await _gate.WaitAsync();
        try { await _store.UpdateAsync(values=>{foreach(var id in values.Keys.Where(k=>Guid.TryParse(k,out var node)&&!keep.Contains(node)).ToArray())values.Remove(id);return values;}); }
        finally { _gate.Release(); }
    }
    public async Task SetAsync(Guid node, string secret)
    {
        await _gate.WaitAsync();
        byte[] bytes = Encoding.UTF8.GetBytes(secret);
        try { var values = await _store.LoadAsync(); values[node.ToString()] = Convert.ToBase64String(await Task.Run(() => protector.Protect(bytes))); await _store.SaveAsync(values); }
        finally { CryptographicOperations.ZeroMemory(bytes); _gate.Release(); }
    }
    public async Task<string?> GetAsync(Guid node)
    {
        var values = await _store.LoadAsync(); if (!values.TryGetValue(node.ToString(), out var encrypted)) return null;
        var bytes = await Task.Run(() => protector.Unprotect(Convert.FromBase64String(encrypted)));
        try { return Encoding.UTF8.GetString(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public async Task<Dictionary<string, string>> ExportForBackupAsync(IEnumerable<Guid>? referenced = null)
    {
        var values = await _store.LoadAsync(); var result = new Dictionary<string, string>();
        var keep = referenced?.Select(id => id.ToString()).ToHashSet();
        foreach (var pair in values.Where(p => keep is null || keep.Contains(p.Key))) { var bytes = await Task.Run(() => protector.Unprotect(Convert.FromBase64String(pair.Value))); try { result[pair.Key] = Encoding.UTF8.GetString(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); } }
        return result;
    }
    public Dictionary<string, string> ProtectImported(Dictionary<string, string> values)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in values) { if (!Guid.TryParse(pair.Key, out _)) throw new InvalidDataException(); var bytes = Encoding.UTF8.GetBytes(pair.Value); try { result[pair.Key] = Convert.ToBase64String(protector.Protect(bytes)); } finally { CryptographicOperations.ZeroMemory(bytes); } }
        return result;
    }
}

public sealed record BackupPayload(int Version, DateTimeOffset Created, AppSettings Settings, List<ServerProfile> Servers, Dictionary<string, string> Credentials);
public sealed record BackupEnvelope(int Version, string Kdf, int MemoryKiB, int Iterations, int Parallelism, string Salt, string Nonce, string Ciphertext, string Tag);
public static class BackupCrypto
{
    private static readonly byte[] Aad = "NekoProxy.Backup.v1"u8.ToArray();
    public static byte[] Encrypt(BackupPayload payload, string password)
    {
        if (password.Length < 12) throw new ArgumentException("备份密码至少需要 12 个字符。");
        var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Derive(password, salt); var plain = JsonSerializer.SerializeToUtf8Bytes(payload); var cipher = new byte[plain.Length]; var tag = new byte[16];
        try { using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, plain, cipher, tag, Aad); return JsonSerializer.SerializeToUtf8Bytes(new BackupEnvelope(1, "Argon2id", 65536, 3, 1, Convert.ToBase64String(salt), Convert.ToBase64String(nonce), Convert.ToBase64String(cipher), Convert.ToBase64String(tag))); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    public static BackupPayload Decrypt(byte[] bytes, string password)
    {
        if (bytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("Backup size exceeds limit.");
        var envelope = JsonSerializer.Deserialize<BackupEnvelope>(bytes) ?? throw new InvalidDataException();
        if (envelope.Version != 1 || envelope.Kdf != "Argon2id" || envelope.MemoryKiB != 65536 || envelope.Iterations != 3 || envelope.Parallelism != 1) throw new InvalidDataException("Unsupported backup format.");
        var salt = Convert.FromBase64String(envelope.Salt); var nonce = Convert.FromBase64String(envelope.Nonce); var cipher = Convert.FromBase64String(envelope.Ciphertext); var tag = Convert.FromBase64String(envelope.Tag);
        if (salt.Length != 16 || nonce.Length != 12 || tag.Length != 16) throw new InvalidDataException();
        var key = Derive(password, salt); var plain = new byte[cipher.Length];
        try { using var aes = new AesGcm(key, 16); aes.Decrypt(nonce, cipher, tag, plain, Aad); var payload = JsonSerializer.Deserialize<BackupPayload>(plain) ?? throw new InvalidDataException(); if (payload.Version != 1 || payload.Servers.Any(s => ProfileValidation.Validate(s) is not null) || payload.Servers.Select(s => s.Id).Distinct().Count() != payload.Servers.Count) throw new InvalidDataException(); return payload; }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    private static byte[] Derive(string password, byte[] salt)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password); var key = new byte[32];
        try { var generator = new Argon2BytesGenerator(); generator.Init(new Argon2Parameters.Builder(Argon2Parameters.Argon2id).WithVersion(Argon2Parameters.Version13).WithSalt(salt).WithMemoryAsKB(65536).WithIterations(3).WithParallelism(1).Build()); generator.GenerateBytes(passwordBytes, key); return key; }
        finally { CryptographicOperations.ZeroMemory(passwordBytes); }
    }
}

public sealed record LocalDataInfo(string Directory, long Bytes, DateTimeOffset? LastSaved);
public sealed record LocalExportFile(string Name, string Path, long Bytes);
public sealed record ClearSelection(bool Servers, bool Exports, bool Logs, bool Cache);
// Operations accept only app-owned paths. No remote services, shell commands or network clients.
public sealed class LocalPrivacyService(string directory, ISettingsStore settings, IServerRepository servers, CredentialVault vault,
    string? configurationDirectory = null, string? stateDirectory = null, string? cacheDirectory = null)
{
    public string DataDirectory { get; } = Path.GetFullPath(directory);
    public string ConfigurationDirectory { get; } = Path.GetFullPath(configurationDirectory ?? directory);
    public string StateDirectory { get; } = Path.GetFullPath(stateDirectory ?? directory);
    public string CacheDirectory { get; } = Path.GetFullPath(cacheDirectory ?? Path.Combine(directory, "cache"));
    public string LogDirectory => Path.Combine(StateDirectory, "logs");
    public LocalDataInfo Inspect()
    {
        var files = new[] { DataDirectory, ConfigurationDirectory, StateDirectory, CacheDirectory }.Distinct().SelectMany(SafeFiles).DistinctBy(f => f.FullName).ToList();
        var dataFiles = files.Where(f => Path.GetDirectoryName(f.FullName) == DataDirectory || Path.GetDirectoryName(f.FullName) == ConfigurationDirectory).ToList();
        return new(DataDirectory, files.Sum(f => f.Length), dataFiles.Count == 0 ? null : dataFiles.Max(f => (DateTimeOffset)f.LastWriteTimeUtc));
    }
    public IReadOnlyList<LocalExportFile> ListExports(string exportDirectory) => SafeFiles(exportDirectory).Where(IsExport).Select(f => new LocalExportFile(Path.GetRelativePath(exportDirectory, f.FullName), f.FullName, f.Length)).ToList();
    private static bool IsExport(FileInfo file) => System.Text.RegularExpressions.Regex.IsMatch(file.Name, @"__(VlessReality|Hysteria2|TrojanTls|Shadowsocks2022)__(Shadowrocket|Mihomo|SingBox)__(?:(?:node|node-link|policy-(?:Smart|Privacy|Custom))__)?p[0-9]{1,5}__[a-f0-9]{8}\.(json|yaml|txt)$");
    public void DeleteExport(string exportDirectory, LocalExportFile file)
    {
        var path = Path.GetFullPath(file.Path); var allowed = ListExports(exportDirectory).Any(f => f.Path == path);
        if (!allowed) throw new InvalidOperationException("Export is outside the permitted directory."); File.Delete(path);
    }
    public async Task BackupAsync(string path, string password)
    {
        var profiles = (await servers.LoadAsync()).ToList();
        var payload = new BackupPayload(1, DateTimeOffset.UtcNow, await settings.LoadAsync(), profiles, await vault.ExportForBackupAsync(profiles.SelectMany(s => s.Nodes).Select(n => n.Id)));
        var encrypted = await Task.Run(() => BackupCrypto.Encrypt(payload, password));
        await AtomicWriteAsync(path, encrypted);
    }
    public async Task RestoreAsync(string path, string password, bool replace)
    {
        var info = new FileInfo(path); if (info.Length > 32 * 1024 * 1024) throw new InvalidDataException();
        var encrypted = await File.ReadAllBytesAsync(path); var payload = await Task.Run(() => BackupCrypto.Decrypt(encrypted, password));
        // Authenticate before touching state. Re-encrypt for this OS user's secure store.
        var localSettings = await settings.LoadAsync(); var localServers = (await servers.LoadAsync()).ToList();
        var importedIds = payload.Servers.SelectMany(s => s.Nodes).Select(n => n.Id.ToString()).ToHashSet();
        var imported = await Task.Run(() => vault.ProtectImported(payload.Credentials.Where(p => importedIds.Contains(p.Key)).ToDictionary()));
        var credentialsPath = Path.Combine(DataDirectory, "credentials.json");
        var existing = File.Exists(credentialsPath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(credentialsPath))! : [];
        var merged = replace ? payload.Servers : localServers.Where(s => payload.Servers.All(p => p.Id != s.Id)).Concat(payload.Servers).ToList();
        if (!replace) foreach (var pair in imported) existing[pair.Key] = pair.Value; else existing = imported;
        var retainedIds = merged.SelectMany(s => s.Nodes).Select(n => n.Id.ToString()).ToHashSet();
        existing = existing.Where(p => retainedIds.Contains(p.Key)).ToDictionary();
        // Preserve local paths, window state and onboarding state; restore portable preferences only.
        var policy=PrivacyPolicyValidation.Check(payload.Settings.ClientPolicy,ClientType.SingBox,"192.0.2.1");
        var restoredSettings = localSettings with { ClientPolicy=policy,Language = payload.Settings.Language, SshTimeoutSeconds = Math.Clamp(payload.Settings.SshTimeoutSeconds, 5, 120), ReduceMotion = payload.Settings.ReduceMotion };
        var checkpoint = Path.Combine(DataDirectory, "checkpoints", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-ffff")); Directory.CreateDirectory(checkpoint);
        var paths = new[] { Path.Combine(DataDirectory,"servers.json"), Path.Combine(ConfigurationDirectory,"settings.json"), credentialsPath };
        foreach (var current in paths) { if (File.Exists(current)) File.Copy(current, Path.Combine(checkpoint, Path.GetFileName(current))); }
        try { await servers.SaveAsync(merged); await settings.SaveAsync(restoredSettings); await AtomicWriteAsync(credentialsPath, JsonSerializer.SerializeToUtf8Bytes(existing)); }
        catch { foreach (var current in paths) { var previous = Path.Combine(checkpoint, Path.GetFileName(current)); if (File.Exists(previous)) File.Copy(previous, current, true); else if (File.Exists(current)) File.Delete(current); } throw; }
    }
    public async Task ClearAsync(ClearSelection selection, string exportDirectory)
    {
        if (selection.Servers) { await servers.SaveAsync([]);await settings.UpdateAsync(s=>s with{KnownHosts=[]}); var path = Path.Combine(DataDirectory, "credentials.json"); if (File.Exists(path)) File.Delete(path); foreach (var checkpoint in SafeFiles(Path.Combine(DataDirectory,"checkpoints"))) checkpoint.Delete(); }
        if (selection.Exports) foreach (var file in ListExports(exportDirectory)) DeleteExport(exportDirectory, file);
        if (selection.Logs) foreach (var file in SafeFiles(LogDirectory)) file.Delete();
        if(selection.Cache||selection.Servers)foreach(var file in SafeFiles(Path.Combine(StateDirectory,"history")))file.Delete();
        if (selection.Cache) foreach (var file in SafeFiles(CacheDirectory)) file.Delete();
    }
    public async Task ExportDiagnosticsAsync(string path)
    {
        // Allowlist report; never include raw settings, server metadata, credentials, links, paths or arbitrary log contents.
        var report = new { App = "NekoProxy", Version = AppVersion.Current, Runtime = Environment.Version.ToString(), OS = Environment.OSVersion.VersionString, Timestamp = DateTimeOffset.UtcNow, ServerCount = (await servers.LoadAsync()).Count, RemoteOperationsEnabled = true };
        await AtomicWriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static IEnumerable<FileInfo> SafeFiles(string root)
    {
        if (!System.IO.Directory.Exists(root)) yield break;
        var info = new DirectoryInfo(root); if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) yield break;
        foreach (var file in info.EnumerateFiles()) if (!file.Attributes.HasFlag(FileAttributes.ReparsePoint)) yield return file;
        foreach (var child in info.EnumerateDirectories()) if (!child.Attributes.HasFlag(FileAttributes.ReparsePoint)) foreach (var file in SafeFiles(child.FullName)) yield return file;
    }
    private static async Task AtomicWriteAsync(string path, byte[] bytes)
    {
        var full = Path.GetFullPath(path); System.IO.Directory.CreateDirectory(Path.GetDirectoryName(full)!); var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, bytes); File.Move(temporary, full, true); } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
