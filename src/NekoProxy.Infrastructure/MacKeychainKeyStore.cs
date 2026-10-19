using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace NekoProxy.Infrastructure;

internal sealed class MacKeychainKeyStore : ISystemVaultKeyStore
{
    public string Name => "macOS Keychain";
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static readonly Lazy<IntPtr> SecurityHandle = new(() => NativeLibrary.Load(Security));
    private static readonly Lazy<IntPtr> CfHandle = new(() => NativeLibrary.Load(CF));
    [DllImport(Security)] private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
    [DllImport(Security)] private static extern int SecItemAdd(IntPtr attributes, out IntPtr result);
    [DllImport(CF)] private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keyCallbacks, IntPtr valueCallbacks);
    [DllImport(CF)] private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);
    [DllImport(CF)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);
    [DllImport(CF)] private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);
    [DllImport(CF)] private static extern nint CFDataGetLength(IntPtr data);
    [DllImport(CF)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport(CF)] private static extern void CFRelease(IntPtr value);
    private static IntPtr Sec(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(SecurityHandle.Value, name));
    private static IntPtr True => Marshal.ReadIntPtr(NativeLibrary.GetExport(CfHandle.Value, "kCFBooleanTrue"));

    private sealed class Query : IDisposable
    {
        public IntPtr Handle { get; } = CFDictionaryCreateMutable(IntPtr.Zero, 0,
            NativeLibrary.GetExport(CfHandle.Value, "kCFTypeDictionaryKeyCallBacks"),
            NativeLibrary.GetExport(CfHandle.Value, "kCFTypeDictionaryValueCallBacks"));
        public Query(string account)
        {
            if (Handle == IntPtr.Zero) throw new OutOfMemoryException();
            Set("kSecClass", Sec("kSecClassGenericPassword"));
            Text("kSecAttrService", "org.nekoproxy.desktop.credentials");
            Text("kSecAttrAccount", account);
        }
        public void Set(string key, IntPtr value) => CFDictionarySetValue(Handle, Sec(key), value);
        private void Text(string key, string text)
        {
            var value = CFStringCreateWithCString(IntPtr.Zero, text, 0x08000100);
            if (value == IntPtr.Zero) throw new OutOfMemoryException();
            try { Set(key, value); } finally { CFRelease(value); }
        }
        public void Dispose() => CFRelease(Handle);
    }

    public byte[]? Read(string account)
    {
        using var query = new Query(account);
        query.Set("kSecReturnData", True); query.Set("kSecMatchLimit", Sec("kSecMatchLimitOne"));
        var status = SecItemCopyMatching(query.Handle, out var data);
        try
        {
            if (status == -25300) return null; // errSecItemNotFound only; other errors must fail closed.
            if (status != 0) throw new CredentialStorageUnavailableException(PlatformSecretProtector.UnavailableMessage);
            if (data == IntPtr.Zero || CFDataGetLength(data) != 32) throw new CryptographicException("Invalid Keychain master key.");
            var key = new byte[32]; Marshal.Copy(CFDataGetBytePtr(data), key, 0, key.Length); return key;
        }
        finally { if (data != IntPtr.Zero) CFRelease(data); }
    }

    public void Create(string account, byte[] key)
    {
        using var query = new Query(account);
        var data = CFDataCreate(IntPtr.Zero, key, key.Length);
        if (data == IntPtr.Zero) throw new OutOfMemoryException();
        try
        {
            query.Set("kSecValueData", data);
            // Default macOS login Keychain; no synchronizable attribute requested.
            // The system controls access prompts; no security CLI or plaintext file.
            var status = SecItemAdd(query.Handle, out var result);
            if (result != IntPtr.Zero) CFRelease(result);
            if (status != 0 && status != -25299) throw new CredentialStorageUnavailableException(PlatformSecretProtector.UnavailableMessage);
        }
        finally { CFRelease(data); }
    }
}
