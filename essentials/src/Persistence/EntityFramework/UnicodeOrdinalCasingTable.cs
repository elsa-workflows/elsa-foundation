using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The pinned simple-uppercase mappings stores use to project persisted ordinal-ignore-case search keys, so a
/// host runtime or operating-system upgrade cannot change those keys. Every consumer pins
/// <see cref="ComputeMappingFingerprint"/> in its own persisted algorithm id and refuses a mismatch, so this table is
/// immutable: a different mapping set is a new table, not an edit to this one.
/// </summary>
public static partial class UnicodeOrdinalCasingTable
{
    /// <summary>Returns the pinned simple uppercase of <paramref name="scalar"/>, or the scalar itself when it has none.</summary>
    public static int ToSimpleUppercase(int scalar)
    {
        var mappings = SimpleUppercaseMappings;
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

    /// <summary>Lowercase hex SHA-256 over every mapping pair, each scalar written as a big-endian 32-bit integer.</summary>
    public static string ComputeMappingFingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> pair = stackalloc byte[8];
        var mappings = SimpleUppercaseMappings;
        for (var index = 0; index < mappings.Length; index += 2)
        {
            BinaryPrimitives.WriteInt32BigEndian(pair, mappings[index]);
            BinaryPrimitives.WriteInt32BigEndian(pair[4..], mappings[index + 1]);
            hash.AppendData(pair);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
