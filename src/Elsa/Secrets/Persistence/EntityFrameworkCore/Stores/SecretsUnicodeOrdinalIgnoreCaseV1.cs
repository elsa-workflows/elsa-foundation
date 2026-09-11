using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Elsa-owned, persisted-key projection pinned to the .NET 10 <c>ToUpperInvariant</c> mapping
/// used when the Phase 1 storage contract was established (ICU 74 / Unicode 15.1 on Linux).
/// Runtime casing APIs are intentionally excluded so a host runtime upgrade cannot silently
/// change lookup or search keys already stored by the Secrets EF module.
/// </summary>
internal static class SecretsUnicodeOrdinalIgnoreCaseV1
{
    public const string UnicodeVersion = UnicodeOrdinalCasingData.UnicodeVersion;
    public const string MappingFingerprint = "296b59c818b7c72305dc51e37c67ede6f49940a25fd9f861cf4541ba92431523";
    public const string AlgorithmId = "elsa-secrets-unicode-ordinal-ignore-case-v1-" + MappingFingerprint;

    static SecretsUnicodeOrdinalIgnoreCaseV1()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> pair = stackalloc byte[8];
        var mappings = UnicodeOrdinalCasingData.SimpleUppercaseMappings;
        for (var index = 0; index < mappings.Length; index += 2)
        {
            BinaryPrimitives.WriteInt32BigEndian(pair, mappings[index]);
            BinaryPrimitives.WriteInt32BigEndian(pair[4..], mappings[index + 1]);
            hash.AppendData(pair);
        }

        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (!string.Equals(actual, MappingFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Secrets Unicode projection data has fingerprint '{actual}', expected '{MappingFingerprint}'.");
        }
    }

    public static string Project(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateWellFormed(value);
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length;)
        {
            var scalar = char.ConvertToUtf32(value, index);
            index += scalar > char.MaxValue ? 2 : 1;
            result.Append(char.ConvertFromUtf32(MapScalar(scalar)));
        }

        return result.ToString();
    }

    private static int MapScalar(int scalar)
    {
        var mappings = UnicodeOrdinalCasingData.SimpleUppercaseMappings;
        var low = 0;
        var high = mappings.Length / 2 - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var index = middle * 2;
            var candidate = mappings[index];
            if (candidate == scalar)
                return mappings[index + 1];
            if (candidate < scalar)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return scalar;
    }

    private static void ValidateWellFormed(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsLowSurrogate(value[index]) ||
                char.IsHighSurrogate(value[index]) &&
                (++index >= value.Length || !char.IsLowSurrogate(value[index])))
            {
                throw new ArgumentException(
                    "Secrets lookup and search values must be well-formed UTF-16.",
                    nameof(value));
            }
        }
    }
}
