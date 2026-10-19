using System.Security.Cryptography;
using System.Text;
using NekoProxy.Infrastructure;
using Xunit;

namespace NekoProxy.Tests;

// Algorithm/failure-boundary tests only. These do not substitute for the separate
// ActualOsVault test, which always uses the real OS credential provider.
public sealed class KeyringEnvelopeTests
{
    private sealed class Store : ISystemVaultKeyStore
    {
        public string Name => "isolated unit-test store";
        public byte[]? Key;
        public int Creates;
        public bool Unavailable;
        public byte[]? Read(string account) => Unavailable ? throw new CredentialStorageUnavailableException("fixture unavailable") : Key?.ToArray();
        public void Create(string account, byte[] key) { Creates++; Key=key.ToArray(); }
    }
    [Fact] public void EnvelopeUsesIndependentNoncesAndAuthenticatesHeaderCiphertextAndKey()
    {
        var store=new Store();using var protector=new KeyringSecretProtector(store,"unit-"+Guid.NewGuid().ToString("N"),()=>false);
        var plain=Encoding.UTF8.GetBytes("isolated-envelope-test");var first=protector.Protect(plain);var second=protector.Protect(plain);
        Assert.NotEqual(first,second);Assert.Equal(plain,protector.Unprotect(first));Assert.Equal(1,store.Creates);
        first[^1]^=1;Assert.ThrowsAny<CryptographicException>(()=>protector.Unprotect(first));
        second[0]^=1;Assert.ThrowsAny<CryptographicException>(()=>protector.Unprotect(second));
    }
    [Fact] public void MissingKeyNeverCreatesReplacementForExistingCiphertext()
    {
        var store=new Store();using var provider=new KeyringSecretProtector(store,"unit-"+Guid.NewGuid().ToString("N"),()=>true);
        Assert.Throws<CredentialStorageUnavailableException>(()=>provider.Protect([1,2,3]));
        Assert.Equal(0,store.Creates);Assert.Null(store.Key);
    }
    [Fact] public async Task UnavailableKeyringFailsBeforeAnyCredentialFileIsWritten()
    {
        var dir=Path.Combine(Path.GetTempPath(),"NekoProxy-no-fallback-"+Guid.NewGuid().ToString("N"));
        var store=new Store{Unavailable=true};using var provider=new KeyringSecretProtector(store,"unit-"+Guid.NewGuid().ToString("N"),()=>false);
        await Assert.ThrowsAsync<CredentialStorageUnavailableException>(()=>new CredentialVault(dir,provider).SetAsync(Guid.NewGuid(),"never-save-plaintext"));
        Assert.False(File.Exists(Path.Combine(dir,"credentials.json")));Assert.Equal(0,store.Creates);
    }
}
