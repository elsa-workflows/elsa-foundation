using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

internal static class IdentityHashFraming
{
    public static string Sha256Hex(IEnumerable<string> parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> lengthBuffer = stackalloc byte[sizeof(int)];
        foreach (var part in parts)
        {
            var bytes = Encoding.UTF8.GetBytes(part);
            BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, bytes.Length);
            hash.AppendData(lengthBuffer);
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
