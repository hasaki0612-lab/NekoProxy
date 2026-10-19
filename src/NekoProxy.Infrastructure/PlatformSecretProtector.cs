using System.Security.Cryptography;
using System.Text;

namespace NekoProxy.Infrastructure;

public static class PlatformSecretProtector
{
    public const string UnavailableMessage = "系统安全存储不可用或未解锁。请启用系统密钥环后重试；不会以明文保存凭据。";
    public static ISecretProtector Create(string dataDirectory)
    {
        if (OperatingSystem.IsWindows()) return new WindowsSecretProtector();
        var account = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory))));
        bool HasCiphertext() => File.Exists(Path.Combine(dataDirectory, "credentials.json")) &&
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(dataDirectory, "credentials.json")))?.Count > 0;
        if (OperatingSystem.IsMacOS()) return new KeyringSecretProtector(new MacKeychainKeyStore(), account, HasCiphertext);
        if (OperatingSystem.IsLinux()) return new KeyringSecretProtector(new LinuxSecretServiceKeyStore(), account, HasCiphertext);
        throw new PlatformNotSupportedException("No secure credential store is available on this platform.");
    }

    public static string Description => OperatingSystem.IsWindows() ? "Windows DPAPI · CurrentUser"
        : OperatingSystem.IsMacOS() ? "macOS Keychain · AES-256-GCM"
        : OperatingSystem.IsLinux() ? "Linux Secret Service · AES-256-GCM（需已解锁的系统密钥环）" : "安全存储不可用。";
}

public sealed class CredentialStorageUnavailableException(string message, Exception? inner = null) : IOException(message, inner);

internal interface ISystemVaultKeyStore
{
    string Name { get; }
    byte[]? Read(string account);
    void Create(string account, byte[] key);
}

// Only an authenticated envelope is stored in credentials.json. The random master
// key lives exclusively in the user's OS credential store, never in a key file.
internal sealed class KeyringSecretProtector(ISystemVaultKeyStore store, string account, Func<bool> hasCiphertext) : ISecretProtector, IDisposable
{
    private static readonly byte[] Header = "NekoProxy.Keyring.v1\0"u8.ToArray();
    private readonly object _gate = new();
    private byte[]? _key;
    private bool _disposed;
    public string Name => store.Name + " · AES-256-GCM";

    private byte[] Key(bool create)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_key is not null) return _key;
        // Serialize first-use creation across app instances; libsecret storev may
        // otherwise update an item created concurrently by a second process.
        using var mutex = new Mutex(false, "NekoProxy.Vault." + account);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new CredentialStorageUnavailableException("系统密钥环正忙，请稍后重试。未保存明文凭据。");
            var key = store.Read(account);
            if (key is null && create)
            {
                if (hasCiphertext()) throw new CredentialStorageUnavailableException("系统密钥环中缺少此资料目录的密钥。请解锁原密钥环，或从加密备份恢复；不会覆盖现有加密资料。");
                var generated = RandomNumberGenerator.GetBytes(32);
                try { store.Create(account, generated); }
                finally { CryptographicOperations.ZeroMemory(generated); }
                key = store.Read(account);
            }
            if (key is null || key.Length != 32)
            {
                if (key is not null) CryptographicOperations.ZeroMemory(key);
                throw new CredentialStorageUnavailableException("系统密钥环中缺少此资料目录的密钥。请解锁原密钥环，或从加密备份恢复；不会创建替代密钥来解密现有数据。");
            }
            return _key = key;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new CredentialStorageUnavailableException(PlatformSecretProtector.UnavailableMessage, e);
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    public byte[] Protect(byte[] value)
    {
        lock (_gate)
        {
            var result = new byte[Header.Length + 12 + 16 + value.Length];
            Header.CopyTo(result, 0);
            var nonce = result.AsSpan(Header.Length, 12);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(Key(true), 16);
            aes.Encrypt(nonce, value, result.AsSpan(Header.Length + 28), result.AsSpan(Header.Length + 12, 16), Header);
            return result;
        }
    }

    public byte[] Unprotect(byte[] value)
    {
        lock (_gate)
        {
            if (value.Length < Header.Length + 28 || !value.AsSpan(0, Header.Length).SequenceEqual(Header))
                throw new CryptographicException("此凭据格式不属于当前平台。跨平台迁移请使用加密备份，不要直接复制 credentials.json。");
            var plain = new byte[value.Length - Header.Length - 28];
            try
            {
                using var aes = new AesGcm(Key(false), 16);
                aes.Decrypt(value.AsSpan(Header.Length, 12), value.AsSpan(Header.Length + 28), value.AsSpan(Header.Length + 12, 16), plain, Header);
                return plain;
            }
            catch { CryptographicOperations.ZeroMemory(plain); throw; }
        }
    }

    public void Dispose()
    {
        lock (_gate) { if (_key is not null) CryptographicOperations.ZeroMemory(_key); _key = null; _disposed = true; }
    }
}
