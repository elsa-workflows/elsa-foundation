using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Runtime.Core.Models;

internal static class RuntimeIdentityDigest
{
    public static string Compute(params ReadOnlySpan<string> values)
    {
        using var stream = new MemoryStream();
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            stream.Write(length);
            stream.Write(bytes);
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
}
