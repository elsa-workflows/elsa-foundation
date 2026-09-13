using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

internal static class IdentityProviderConfigurationCanonicalizer
{
    public const int MaximumIdentityLength = 400;

    public static string Normalize(string? value)
    {
        Validate(value, nameof(value));
        var normalized = (value ?? "").ToLowerInvariant();
        if (!ContainsGarayCapital(normalized))
            return normalized;

        var result = new StringBuilder(normalized.Length);
        for (var index = 0; index < normalized.Length; index++)
        {
            if (index + 1 < normalized.Length && char.IsSurrogatePair(normalized[index], normalized[index + 1]))
            {
                var scalar = char.ConvertToUtf32(normalized[index], normalized[index + 1]);
                if (scalar is >= 0x10D50 and <= 0x10D65)
                {
                    result.Append(char.ConvertFromUtf32(scalar + 0x20));
                    index++;
                    continue;
                }
            }

            result.Append(normalized[index]);
        }

        return result.ToString();
    }

    public static string TenantProviderId(string tenant, string provider) =>
        Sha256Framed(Normalize(tenant), Normalize(provider));

    public static string GlobalProviderId(string provider) => Sha256Framed(Normalize(provider));

    public static bool Matches(string? tenant, string? tenantLookup, string provider, string providerLookup) =>
        (tenant is null ? tenantLookup is null : string.Equals(tenantLookup, Normalize(tenant), StringComparison.Ordinal)) &&
        string.Equals(providerLookup, Normalize(provider), StringComparison.Ordinal);

    public static void Validate(string? value, string parameterName)
    {
        if (value is null)
            return;
        if (value.Length > MaximumIdentityLength)
            throw new ArgumentException($"Identity key values cannot exceed {MaximumIdentityLength} UTF-16 code units.", parameterName);
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsLowSurrogate(value[index]) ||
                (char.IsHighSurrogate(value[index]) &&
                 (++index >= value.Length || !char.IsLowSurrogate(value[index]))))
                throw new ArgumentException("Identity key values must be well-formed UTF-16.", parameterName);
        }
    }

    private static bool ContainsGarayCapital(string value)
    {
        for (var index = 0; index + 1 < value.Length; index++)
        {
            if (!char.IsSurrogatePair(value[index], value[index + 1]))
                continue;
            var scalar = char.ConvertToUtf32(value[index], value[index + 1]);
            if (scalar is >= 0x10D50 and <= 0x10D65)
                return true;
        }

        return false;
    }

    private static string Sha256Framed(params string[] parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var lengthBuffer = new byte[sizeof(int)];
        foreach (var part in parts)
        {
            var bytes = Encoding.UTF8.GetBytes(part);
            BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, bytes.Length);
            hash.AppendData(lengthBuffer);
            hash.AppendData(bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
