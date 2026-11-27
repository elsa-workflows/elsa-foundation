using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Provider-neutral, lossless projections for opaque .NET string identities stored by EF adapters.
/// </summary>
public static class EfRelationalIdentity
{
    public static string Hash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexString(SHA256.HashData(EncodeUtf16CodeUnits(value)));
    }

    /// <summary>Hashes several opaque identities without delimiter collisions between adjacent values.</summary>
    public static string HashLengthFramed(params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var identity = new StringBuilder();
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            identity.Append(value.Length).Append(':').Append(value);
        }
        return Hash(identity.ToString());
    }

    public static string Encode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToBase64String(EncodeUtf16CodeUnits(value));
    }

    public static string Decode(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length % sizeof(char) != 0)
                throw new FormatException("The encoded identity does not contain complete UTF-16 code units.");

            var chars = new char[bytes.Length / sizeof(char)];
            for (var index = 0; index < chars.Length; index++)
                chars[index] = (char)(bytes[index * sizeof(char)] | bytes[index * sizeof(char) + 1] << 8);
            return new string(chars);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidOperationException("The row contains an invalid encoded identity projection.", exception);
        }
    }

    public static byte[] CreateOrderKey(string value, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
        if (value.Length > maximumLength)
            throw new ArgumentException($"The identity cannot exceed {maximumLength} UTF-16 code units.", nameof(value));

        var result = new byte[checked((maximumLength + 1) * sizeof(char))];
        for (var index = 0; index < value.Length; index++)
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(index * sizeof(char), sizeof(char)), value[index]);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(result.Length - sizeof(ushort)), checked((ushort)value.Length));
        return result;
    }

    /// <summary>
    /// Lossless ordinal text projection for identities whose length has no contract-enforced upper bound.
    /// Four uppercase hexadecimal digits per UTF-16 code unit preserve StringComparer.Ordinal, including
    /// embedded NUL and lone surrogate code units; a shorter prefix sorts before its extension.
    /// </summary>
    public static string CreateOrdinalTextOrderKey(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = new byte[checked(value.Length * sizeof(char))];
        for (var index = 0; index < value.Length; index++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(index * sizeof(char), sizeof(char)), value[index]);
        return Convert.ToHexString(bytes);
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
