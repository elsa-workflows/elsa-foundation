using System.Security.Cryptography;
using System.Text;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Connections.Persistence.EntityFrameworkCore.Stores;

// Persisted projections use the existing Elsa-owned Unicode table, never the host's casing data.
internal static class ConnectionKeys
{
    private const string MappingFingerprint = "bcbcc4bf0951b182137ed0f42681f30bafda7777f500c42203cf58bb7e4eaaa1";
    static ConnectionKeys()
    {
        if (UnicodeOrdinalCasingTable.ComputeMappingFingerprint() != MappingFingerprint)
            throw new InvalidOperationException("The connection projection requires its versioned Unicode casing data.");
    }

    public static string Name(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var name = value.Trim();
        if (name.Length > 128) throw new ArgumentException("A connection name cannot exceed 128 characters.", nameof(value));
        return Fold(name);
    }

    public static void Tenant(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 256) throw new ArgumentException("A tenant identity cannot exceed 256 characters.", nameof(value));
        _ = Fold(value); // Validate UTF-16 without changing the ordinal tenant identity.
    }

    public static string Secret(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Fold(value))));

    public static string Fold(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length;)
        {
            if (char.IsLowSurrogate(value[index]) || char.IsHighSurrogate(value[index]) &&
                (index + 1 == value.Length || !char.IsLowSurrogate(value[index + 1])))
                throw new ArgumentException("Connection lookup values must be well-formed UTF-16.", nameof(value));
            var scalar = char.ConvertToUtf32(value, index);
            index += scalar > char.MaxValue ? 2 : 1;
            result.Append(char.ConvertFromUtf32(UnicodeOrdinalCasingTable.ToSimpleUppercase(scalar)));
        }
        return result.ToString();
    }
}
