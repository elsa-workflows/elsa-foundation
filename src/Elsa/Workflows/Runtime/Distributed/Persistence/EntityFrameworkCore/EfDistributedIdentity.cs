using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

/// <summary>Provider-independent scoped identity, digest, and ordinal UTF-16 projections shared by EF leaves.</summary>
internal static class EfDistributedIdentity
{
    public static string CreateId(string scope, string logicalIdentity) =>
        Hash($"{scope.Length}:{scope}{logicalIdentity.Length}:{logicalIdentity}");

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(EncodeUtf16CodeUnits(value)));

    public static string EncodeScope(string value) => Convert.ToBase64String(EncodeUtf16CodeUnits(value));

    public static string DecodeScope(string encoded)
    {
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length % sizeof(char) != 0)
                throw new FormatException("The encoded scope does not contain complete UTF-16 code units.");
            var chars = new char[bytes.Length / sizeof(char)];
            for (var index = 0; index < chars.Length; index++)
                chars[index] = (char)(bytes[index * sizeof(char)] | bytes[index * sizeof(char) + 1] << 8);
            return new string(chars);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidOperationException("The row contains an invalid encoded scope projection.", exception);
        }
    }

    public static byte[] CreateOrderKey(string value, int width)
    {
        var result = new byte[width];
        for (var index = 0; index < value.Length; index++)
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(index * sizeof(char), sizeof(char)), value[index]);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(result.Length - sizeof(ushort)), checked((ushort)value.Length));
        return result;
    }

    private static byte[] EncodeUtf16CodeUnits(string value)
    {
        var bytes = new byte[checked(value.Length * sizeof(char))];
        for (var index = 0; index < value.Length; index++)
        {
            bytes[index * sizeof(char)] = (byte)value[index];
            bytes[index * sizeof(char) + 1] = (byte)(value[index] >> 8);
        }
        return bytes;
    }
}
