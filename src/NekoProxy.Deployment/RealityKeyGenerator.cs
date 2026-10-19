using System.Security.Cryptography;
using NekoProxy.Core;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
namespace NekoProxy.Deployment;
public sealed class RealityKeyGenerator : IRealityKeyGenerator
{
    public RealityKeyMaterial Generate()
    {
        var key = new X25519PrivateKeyParameters(new SecureRandom());
        static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new(Guid.NewGuid().ToString(), Encode(key.GetEncoded()), Encode(key.GeneratePublicKey().GetEncoded()), Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant());
    }
}
