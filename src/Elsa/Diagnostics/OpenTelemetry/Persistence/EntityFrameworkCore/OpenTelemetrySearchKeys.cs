using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;

/// <summary>
/// Stable Unicode ordinal-ignore-case projections used by every EF query.  The table is copied into
/// this module deliberately: changing an unrelated feature must never change this storage contract.
/// </summary>
internal static class OpenTelemetrySearchKeys
{
    private const int ExpansionFactor = 7;
    // Search keys for ordinary signal fields are deliberately unbounded.  The Groundwork
    // contract only bounds fields that participate in a bounded projection; names, bodies and
    // severities remain valid storage strings.
    public const int MaximumTraceIdCodeUnits = 256;
    public const int MaximumSummaryElementCodeUnits = 512;
    public const int MaximumSummaryElementCount = 5_000;
    public const int MaximumSummaryNameCodeUnits = 571;
    public const int MaximumSummaryNameSearchKeyCodeUnits = MaximumSummaryNameCodeUnits * ExpansionFactor;
    public const string MappingFingerprint = "bcbcc4bf0951b182137ed0f42681f30bafda7777f500c42203cf58bb7e4eaaa1";
    public const string AlgorithmId = "elsa-opentelemetry-unicode-ordinal-ignore-case-v1-" + MappingFingerprint;

    static OpenTelemetrySearchKeys()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> pair = stackalloc byte[8];
        var mappings = UnicodeOrdinalCasingData.SimpleUppercaseMappings;
        for (var i = 0; i < mappings.Length; i += 2)
        {
            BinaryPrimitives.WriteInt32BigEndian(pair, mappings[i]);
            BinaryPrimitives.WriteInt32BigEndian(pair[4..], mappings[i + 1]);
            hash.AppendData(pair);
        }
        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (!string.Equals(actual, MappingFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException($"OpenTelemetry Unicode projection data has fingerprint '{actual}'.");
    }

    public static string Key(string value, string parameterName = "value")
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        Validate(value, parameterName);
        var result = new StringBuilder(checked(value.Length * ExpansionFactor));
        for (var i = 0; i < value.Length;)
        {
            var scalar = char.ConvertToUtf32(value, i);
            i += scalar > char.MaxValue ? 2 : 1;
            result.Append('|').Append(MapScalar(scalar).ToString("X6", CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }

    public static string RequiredKey(string? value, string parameterName = "value")
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"OpenTelemetry field '{parameterName}' is required.", parameterName);
        return Key(value, parameterName);
    }

    public static string TraceKey(string traceId) => Hash(Key(traceId, nameof(traceId)));
    public static string OrderKey(string value) => Hash(Key(value, nameof(value)));
    public static string TraceId(string value) => Bounded(value, MaximumTraceIdCodeUnits, nameof(value));
    public static string ResourceId(string value) => Bounded(value, 512, nameof(value));
    public static string ServiceName(string value) => Bounded(value, 512, nameof(value));
    public static string SpanId(string value) => Bounded(value, 128, nameof(value));
    public static string LogSpanId(string value) => Bounded(value, 256, nameof(value));
    public static string SignalId(string value) => Bounded(value, 128, nameof(value));
    public static string InstrumentId(string value) => Bounded(value, 256, nameof(value));
    public static string SummaryElement(string value) => Bounded(value, MaximumSummaryElementCodeUnits, nameof(value));
    public static string SummaryName(string value) => Bounded(value, MaximumSummaryNameCodeUnits, nameof(value));

    private static string Bounded(string? value, int maximum, string parameterName)
    {
        _ = RequiredKey(value, parameterName);
        if (value!.Length > maximum)
            throw new ArgumentOutOfRangeException(parameterName, value.Length, $"The OpenTelemetry value exceeds the maximum of {maximum} UTF-16 code units.");
        return Key(value, parameterName);
    }

    public static string Hash(string value)
    {
        Span<byte> bytes = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(value), bytes);
        return Convert.ToHexStringLower(bytes);
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

    private static void Validate(string value, string parameterName)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsLowSurrogate(value[i]) ||
                char.IsHighSurrogate(value[i]) && (++i >= value.Length || !char.IsLowSurrogate(value[i])))
                throw new ArgumentException("The OpenTelemetry search value must be well-formed UTF-16.", parameterName);
        }
    }
}
