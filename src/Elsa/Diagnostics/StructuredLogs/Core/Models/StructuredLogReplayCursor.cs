using System.Diagnostics.CodeAnalysis;

namespace Elsa.Diagnostics.StructuredLogs.Core.Models;

/// <summary>
/// A versioned, source-qualified opaque continuation value for committed structured-log order. The value
/// is safe to round-trip as an SSE <c>id</c>/<c>Last-Event-ID</c>; callers must not parse it or infer
/// ordering from it. Store adapters alone interpret their provider payload.
/// </summary>
public readonly record struct StructuredLogReplayCursor
{
    public const int MaxLength = 2048;

    public StructuredLogReplayCursor(string value)
    {
        if (!IsValidWireValue(value))
            throw new ArgumentException("A structured-log replay cursor must be a bounded single-line value.", nameof(value));

        Value = value;
    }

    /// <summary>The complete opaque wire value.</summary>
    public string Value { get; }

    /// <summary>
    /// Whether this value satisfies the complete SSE wire invariant. Callers crossing a store or HTTP
    /// boundary must check this property because the CLR can create the default value of any struct
    /// without invoking its constructor.
    /// </summary>
    public bool IsValid => IsValidWireValue(Value);

    public override string ToString() => IsValid
        ? Value
        : throw new InvalidOperationException("The structured-log replay cursor is not a valid wire value.");

    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out StructuredLogReplayCursor? cursor)
    {
        if (!IsValidWireValue(value))
        {
            cursor = null;
            return false;
        }

        cursor = new(value!);
        return true;
    }

    private static bool IsValidWireValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaxLength &&
        !value.Contains('\0') &&
        !value.Contains('\r') &&
        !value.Contains('\n');
}
