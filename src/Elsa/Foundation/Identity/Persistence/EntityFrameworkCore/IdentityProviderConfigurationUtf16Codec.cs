using System.Buffers.Binary;
using System.Text;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>Encodes identity text as base64 UTF-16 so unpaired surrogates survive every provider.</summary>
internal static class IdentityProviderConfigurationUtf16Codec
{
    public static string Encode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = new byte[value.Length * sizeof(char)];
        for (var index = 0; index < value.Length; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * sizeof(char), sizeof(char)), value[index]);
        return Convert.ToBase64String(bytes);
    }

    public static string Decode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length % sizeof(char) != 0)
            throw new FormatException("The persisted Identity UTF-16 value is malformed.");
        var result = new char[bytes.Length / sizeof(char)];
        for (var index = 0; index < result.Length; index++)
            result[index] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index * sizeof(char), sizeof(char)));
        return new string(result);
    }
}
