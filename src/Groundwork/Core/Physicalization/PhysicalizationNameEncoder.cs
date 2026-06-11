using System.Security.Cryptography;
using System.Text;

namespace Groundwork.Core.Physicalization;

public static class PhysicalizationNameEncoder
{
    private const int HashLength = 12;

    public static string Encode(string value, int? maxLength = null)
    {
        if (string.IsNullOrEmpty(value))
            return "empty";

        var encoded = string.Concat(value.Select(Encode));
        if (maxLength is null || encoded.Length <= maxLength.Value)
            return encoded;

        if (maxLength.Value <= HashLength + 1)
            throw new ArgumentOutOfRangeException(nameof(maxLength), "Maximum length must leave room for a readable prefix and hash suffix.");

        var suffix = StableHash(value);
        var prefixLength = maxLength.Value - suffix.Length - 1;
        return $"{encoded[..prefixLength]}_{suffix}";
    }

    private static string Encode(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9'
            ? character.ToString()
            : $"_x{(int)character:x4}_";

    private static string StableHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant()[..HashLength];
    }
}
