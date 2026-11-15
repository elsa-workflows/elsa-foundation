using System.Runtime.InteropServices;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Runtime.Http;

/// <summary>A repeated property name inside one JSON object; carries no source text.</summary>
internal sealed class EtlDuplicatePropertyException() : Exception("A JSON object repeats a property name.");

/// <summary>Reads declared typed fields from one JSON object, shared by HTTP JSON pages and JSON Lines files.</summary>
/// <remarks>Callers own size limits and map document-level failures to their own stable codes.</remarks>
internal static class EtlJsonRecordReader
{
    /// <summary>Resolves each declared field and returns either a typed row or the first field error.</summary>
    public static (EtlTypedRow? Row, EtlRowError? Error) ReadFields(JsonElement record, IReadOnlyList<EtlJsonField> fields,
        IReadOnlyList<EtlJsonPointer> fieldPointers, int recordOrdinal)
    {
        var values = new Dictionary<string, EtlTypedValue>(fields.Count, StringComparer.Ordinal);
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            if (!fieldPointers[index].TryResolve(record, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                if (field.Required)
                    return (null, new EtlRowError(recordOrdinal, field.Name, "etl.required-value-missing"));

                values.Add(field.Name, new EtlTypedValue(field.Type, null));
                continue;
            }

            if (!TryReadScalar(value, field.Type, out var typedValue))
                return (null, new EtlRowError(recordOrdinal, field.Name, "etl.invalid-value"));

            values.Add(field.Name, new EtlTypedValue(field.Type, typedValue));
        }

        return (new EtlTypedRow(recordOrdinal, values), null);
    }

    /// <summary>Rejects duplicate property names at any depth and forces decoding of every string, selected or not.</summary>
    /// <exception cref="EtlDuplicatePropertyException">A property name repeats within one object.</exception>
    /// <exception cref="InvalidOperationException">A string or property name is not valid UTF-8.</exception>
    public static void ValidateDocument(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new EtlDuplicatePropertyException();
                    ValidateDocument(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    ValidateDocument(item);
                break;
            case JsonValueKind.String:
                // Force validation even for metadata which no selector reads.
                _ = element.GetString();
                break;
        }
    }

    private static bool TryReadScalar(JsonElement value, EtlColumnType type, out object? result)
    {
        result = null;
        switch (type)
        {
            case EtlColumnType.String when value.ValueKind == JsonValueKind.String:
                result = value.GetString();
                return result is not null;
            case EtlColumnType.Int64 when value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var integer):
                result = integer;
                return true;
            case EtlColumnType.Decimal when value.ValueKind == JsonValueKind.Number && TryReadExactDecimal(value, out var decimalValue):
                result = decimalValue;
                return true;
            case EtlColumnType.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                result = value.GetBoolean();
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadExactDecimal(JsonElement value, out decimal result)
    {
        result = default;
        var raw = JsonMarshal.GetRawUtf8Value(value);
        if (raw.IsEmpty)
            return false;

        var position = 0;
        var negative = raw[position] == (byte)'-';
        if (negative)
            position++;

        var integerStart = position;
        while (position < raw.Length && IsDigit(raw[position]))
            position++;
        var integerEnd = position;
        var fractionStart = position;
        var fractionEnd = position;
        if (position < raw.Length && raw[position] == (byte)'.')
        {
            fractionStart = ++position;
            while (position < raw.Length && IsDigit(raw[position]))
                position++;
            fractionEnd = position;
        }

        long exponent = 0;
        if (position < raw.Length && raw[position] is (byte)'e' or (byte)'E')
        {
            position++;
            var exponentNegative = position < raw.Length && raw[position] == (byte)'-';
            if (exponentNegative || position < raw.Length && raw[position] == (byte)'+')
                position++;
            if (position == raw.Length)
                return false;

            // Saturate at a bound larger than any shift a token of this length can cancel.
            // This classifies extreme exponents without integer overflow or exponent-sized allocation.
            var exponentLimit = raw.Length + 64L;
            long magnitude = 0;
            while (position < raw.Length)
            {
                var current = raw[position++];
                if (!IsDigit(current))
                    return false;

                var digit = current - (byte)'0';
                if (magnitude < exponentLimit)
                    magnitude = magnitude > (exponentLimit - digit) / 10
                        ? exponentLimit
                        : magnitude * 10 + digit;
            }
            exponent = exponentNegative ? -magnitude : magnitude;
        }

        if (position != raw.Length)
            return false;

        var fractionDigits = fractionEnd - fractionStart;
        var totalDigits = integerEnd - integerStart + fractionDigits;
        var firstSignificant = -1;
        var lastSignificant = -1;
        var digitOrdinal = 0;
        for (var index = integerStart; index < integerEnd; index++, digitOrdinal++)
            MarkSignificantDigit(raw[index], digitOrdinal, ref firstSignificant, ref lastSignificant);
        for (var index = fractionStart; index < fractionEnd; index++, digitOrdinal++)
            MarkSignificantDigit(raw[index], digitOrdinal, ref firstSignificant, ref lastSignificant);

        // Every signed zero is exactly representable, whatever valid exponent spelling it carries.
        if (firstSignificant < 0)
        {
            result = new decimal(0, 0, 0, negative, 0);
            return true;
        }

        var significantDigits = lastSignificant - firstSignificant + 1;
        if (significantDigits > 29)
            return false;
        var trailingZeroDigits = totalDigits - lastSignificant - 1;
        var shift = exponent - fractionDigits + trailingZeroDigits;
        int scale;
        int appendedZeros;
        if (shift >= 0)
        {
            if (shift > 29 - significantDigits)
                return false;
            scale = 0;
            appendedZeros = (int)shift;
        }
        else
        {
            if (shift < -28)
                return false;
            scale = (int)-shift;
            appendedZeros = 0;
        }

        UInt128 coefficient = 0;
        digitOrdinal = 0;
        for (var index = integerStart; index < integerEnd; index++, digitOrdinal++)
            if (digitOrdinal >= firstSignificant && digitOrdinal <= lastSignificant)
                coefficient = coefficient * 10 + (uint)(raw[index] - (byte)'0');
        for (var index = fractionStart; index < fractionEnd; index++, digitOrdinal++)
            if (digitOrdinal >= firstSignificant && digitOrdinal <= lastSignificant)
                coefficient = coefficient * 10 + (uint)(raw[index] - (byte)'0');
        for (var index = 0; index < appendedZeros; index++)
            coefficient *= 10;

        var maximumCoefficient = ((UInt128)1 << 96) - 1;
        if (coefficient > maximumCoefficient)
            return false;

        var low = (uint)coefficient;
        var middle = (uint)(coefficient >> 32);
        var high = (uint)(coefficient >> 64);
        result = new decimal((int)low, (int)middle, (int)high, negative, (byte)scale);
        return true;
    }

    private static void MarkSignificantDigit(byte digit, int ordinal, ref int first, ref int last)
    {
        if (digit == (byte)'0')
            return;
        if (first < 0)
            first = ordinal;
        last = ordinal;
    }

    private static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';
}
