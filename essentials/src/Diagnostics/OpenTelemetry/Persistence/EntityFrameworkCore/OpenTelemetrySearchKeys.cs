using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;

/// <summary>
/// Stable Unicode ordinal-ignore-case projections used by every EF query. The mapping table is shared, but this
/// storage contract is pinned here: <see cref="MappingFingerprint"/> is checked when the type initializes, so a
/// change to the shared table made for another feature fails loudly instead of silently changing persisted keys.
/// </summary>
internal static class OpenTelemetrySearchKeys
{
    private const int ExpansionFactor = 7;
    // Search keys for ordinary signal fields are deliberately unbounded.  The
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
        var actual = UnicodeOrdinalCasingTable.ComputeMappingFingerprint();
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
            result.Append('|').Append(UnicodeOrdinalCasingTable.ToSimpleUppercase(scalar).ToString("X6", CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }

    public static string RequiredKey(string? value, string parameterName = "value")
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"OpenTelemetry field '{parameterName}' is required.", parameterName);
        return Key(value, parameterName);
    }

    public static string TraceKey(string traceId) => Hash(TraceId(traceId));
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
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"OpenTelemetry field '{parameterName}' is required.", parameterName);
        if (value.Length > maximum)
            throw new ArgumentOutOfRangeException(parameterName, value.Length, $"The OpenTelemetry value exceeds the maximum of {maximum} UTF-16 code units.");
        return Key(value, parameterName);
    }

    public static string Hash(string value)
    {
        Span<byte> bytes = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(value), bytes);
        return Convert.ToHexStringLower(bytes);
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
