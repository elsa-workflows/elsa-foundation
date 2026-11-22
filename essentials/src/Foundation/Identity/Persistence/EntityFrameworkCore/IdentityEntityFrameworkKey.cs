using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>Provider-neutral canonical keys for tenant-local Identity records.</summary>
internal static class IdentityEntityFrameworkKey
{
    public static string Normalize(string? value)
    {
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

    public static string TenantRecordId(string tenantId, string recordId) =>
        FramedSha256(Normalize(tenantId), Normalize(recordId));

    public static string RecordId(string recordId) => FramedSha256(Normalize(recordId));

    /// <summary>
    /// Encodes a normalized UTF-16 value into a fixed-width binary order key. Code units are
    /// big-endian so byte ordering matches ordinal UTF-16 ordering. Zero padding handles prefixes;
    /// the trailing code-unit length resolves the otherwise ambiguous case where a longer value
    /// contains only NUL code units after a shorter prefix. Fixed width keeps relational indexes
    /// provider-safe while supporting the 400-code-unit Identity key contract.
    /// </summary>
    public static byte[] SortableUtf16Bytes(string? value, int width)
    {
        value ??= "";
        const int lengthBytes = sizeof(ushort);
        if (width < lengthBytes || width % sizeof(ushort) != 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (value.Length > (width - lengthBytes) / sizeof(char) || value.Length > ushort.MaxValue)
            throw new ArgumentException("The value does not fit the requested sortable key width.", nameof(value));

        var result = new byte[width];
        for (var index = 0; index < value.Length; index++)
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(index * sizeof(char), sizeof(char)), value[index]);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(width - lengthBytes, lengthBytes), checked((ushort)value.Length));
        return result;
    }

    /// <summary>Creates a deterministic key from opaque request parts without normalizing their payload.</summary>
    public static string FramedRecordId(params string?[] parts) =>
        FramedSha256Nullable(parts);

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

    private static string FramedSha256(params string[] parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> lengthBuffer = stackalloc byte[sizeof(int)];
        Span<byte> codeUnitBuffer = stackalloc byte[sizeof(char)];
        foreach (var part in parts)
        {
            checked
            {
                BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, part.Length * sizeof(char));
            }

            hash.AppendData(lengthBuffer);
            foreach (var codeUnit in part)
            {
                BinaryPrimitives.WriteUInt16BigEndian(codeUnitBuffer, codeUnit);
                hash.AppendData(codeUnitBuffer);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string FramedSha256Nullable(params string?[] parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> lengthBuffer = stackalloc byte[sizeof(int)];
        Span<byte> codeUnitBuffer = stackalloc byte[sizeof(char)];
        foreach (var part in parts)
        {
            if (part is null)
            {
                BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, -1);
                hash.AppendData(lengthBuffer);
                continue;
            }

            checked { BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, part.Length * sizeof(char)); }
            hash.AppendData(lengthBuffer);
            foreach (var codeUnit in part)
            {
                BinaryPrimitives.WriteUInt16BigEndian(codeUnitBuffer, codeUnit);
                hash.AppendData(codeUnitBuffer);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
