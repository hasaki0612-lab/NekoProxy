using System.Buffers.Text;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace NekoProxy.Infrastructure;

internal sealed class LinuxSecretServiceKeyStore : ISystemVaultKeyStore
{
    public string Name => "Linux Secret Service";
    private const string Secret = "libsecret-1.so.0", Glib = "libglib-2.0.so.0";
    private const string Application = "org.nekoproxy.desktop";
    [DllImport(Secret)] private static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);
    [DllImport(Secret)] private static extern int secret_password_storev_sync(IntPtr schema, IntPtr attributes,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? collection, [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
        byte[] password, IntPtr cancellable, out IntPtr error);
    [DllImport(Secret)] private static extern void secret_password_free(IntPtr password);
    [DllImport(Glib)] private static extern IntPtr g_hash_table_new(IntPtr hash, IntPtr equal);
    [DllImport(Glib)] private static extern int g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);
    [DllImport(Glib)] private static extern void g_hash_table_unref(IntPtr table);
    [DllImport(Glib)] private static extern void g_error_free(IntPtr error);

    private sealed class Attributes : IDisposable
    {
        private readonly List<IntPtr> _strings = [];
        public IntPtr Table { get; }
        public Attributes(string account)
        {
            var library = NativeLibrary.Load(Glib);
            try { Table = g_hash_table_new(NativeLibrary.GetExport(library, "g_str_hash"), NativeLibrary.GetExport(library, "g_str_equal")); }
            finally { NativeLibrary.Free(library); }
            if (Table == IntPtr.Zero) throw new OutOfMemoryException();
            Add("application", Application); Add("vault", account);
        }
        private void Add(string key, string value)
        {
            var k = Marshal.StringToCoTaskMemUTF8(key); _strings.Add(k);
            var v = Marshal.StringToCoTaskMemUTF8(value); _strings.Add(v);
            g_hash_table_insert(Table, k, v);
        }
        public void Dispose() { g_hash_table_unref(Table); foreach (var p in _strings) Marshal.FreeCoTaskMem(p); }
    }

    private static void Check(IntPtr error)
    {
        if (error == IntPtr.Zero) return;
        g_error_free(error);
        throw new CredentialStorageUnavailableException(PlatformSecretProtector.UnavailableMessage);
    }

    public byte[]? Read(string account)
    {
        using var attributes = new Attributes(account);
        var pointer = secret_password_lookupv_sync(IntPtr.Zero, attributes.Table, IntPtr.Zero, out var error);
        try
        {
            Check(error);
            if (pointer == IntPtr.Zero) return null;
            // Avoid retaining the base64 master key in an immutable managed string.
            var encoded = new byte[44]; var key = new byte[32];
            try
            {
                for (var i = 0; i < encoded.Length; i++)
                {
                    var b = Marshal.ReadByte(pointer, i);
                    if (b == 0) throw new CryptographicException("Invalid keyring master key.");
                    encoded[i] = b;
                }
                if (Marshal.ReadByte(pointer, 44) != 0 || Base64.DecodeFromUtf8(encoded, key, out var consumed, out var written) != System.Buffers.OperationStatus.Done || consumed != 44 || written != 32)
                    throw new CryptographicException("Invalid keyring master key.");
                return key;
            }
            catch { CryptographicOperations.ZeroMemory(key); throw; }
            finally { CryptographicOperations.ZeroMemory(encoded); }
        }
        finally { if (pointer != IntPtr.Zero) secret_password_free(pointer); }
    }

    public void Create(string account, byte[] key)
    {
        using var attributes = new Attributes(account);
        var encoded = new byte[45];
        try
        {
            Base64.EncodeToUtf8(key, encoded, out _, out _);
            var ok = secret_password_storev_sync(IntPtr.Zero, attributes.Table, "default", "NekoProxy local vault", encoded, IntPtr.Zero, out var error);
            Check(error);
            if (ok == 0) throw new CredentialStorageUnavailableException(PlatformSecretProtector.UnavailableMessage);
        }
        finally { CryptographicOperations.ZeroMemory(encoded); }
    }
}
