using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Runtime.Http;

/// <summary>One bounded HTTP JSON page decoded into source rows and a protected continuation value.</summary>
internal sealed record EtlJsonPage(IReadOnlyList<EtlJsonPageRecord> Records, string? Continuation);

/// <summary>A page item with its absolute item index, raw UTF-8 size, and either a typed row or safe row error.</summary>
internal sealed record EtlJsonPageRecord(long Index, long RawInputBytes, EtlTypedRow? Row, EtlRowError? Error);

/// <summary>A page-level decoding refusal that exposes only a stable diagnostic code.</summary>
internal sealed class EtlJsonPageReadException(string code)
    : Exception("The ETL HTTP JSON page is invalid or exceeds its configured limits.")
{
    public string Code { get; } = code;
}

/// <summary>Reads one bounded JSON response without retaining unbounded collections or source values in diagnostics.</summary>
internal static class EtlJsonPageReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Parses one page and returns its rows plus the selected opaque continuation, if any.</summary>
    public static EtlJsonPage Read(
        ReadOnlyMemory<byte> utf8,
        EtlHttpJsonSource source,
        EtlConnectorLimits limits,
        EtlHttpPolicy policy,
        long firstRecordOrdinal = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(policy);
        ValidateConfiguration(source, limits, policy, firstRecordOrdinal);

        // Enforce the wire bound before parsing or constructing the JSON document.
        if (utf8.Length > policy.MaxPageBytes)
            throw Refused("etl.http.page-too-large");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = policy.MaxJsonDepth
            });
        }
        catch (JsonException)
        {
            throw Refused("etl.http.page-invalid");
        }

        using (document)
        {
            try
            {
                ValidateDocument(document.RootElement);
            }
            catch (InvalidOperationException)
            {
                // JsonDocument decodes strings lazily, including names and unselected metadata.
                // Do not retain a decoder exception that can contain fragments of the input.
                throw Refused("etl.http.page-invalid");
            }

            EtlJsonPointer recordsPointer;
            EtlJsonPointer[] fieldPointers;
            EtlJsonPointer? continuationPointer;
            try
            {
                recordsPointer = new EtlJsonPointer(source.RecordsPointer);
                fieldPointers = source.Fields.Select(field => new EtlJsonPointer(field.Pointer)).ToArray();
                continuationPointer = source.Pagination switch
                {
                    EtlTokenPagination token => new EtlJsonPointer(token.ContinuationPointer),
                    EtlNextLinkPagination link => new EtlJsonPointer(link.ContinuationPointer),
                    EtlSinglePagePagination => null,
                    _ => throw Refused("etl.http.selector-invalid")
                };
            }
            catch (ArgumentException)
            {
                throw Refused("etl.http.selector-invalid");
            }

            if (!recordsPointer.TryResolve(document.RootElement, out var selectedRecords) ||
                selectedRecords.ValueKind != JsonValueKind.Array)
                throw Refused("etl.http.records-invalid");

            var recordCount = selectedRecords.GetArrayLength();
            if (recordCount > policy.MaxPageRecords)
                throw Refused("etl.http.page-record-limit");
            if (recordCount > 0 && firstRecordOrdinal > long.MaxValue - (recordCount - 1L))
                throw Refused("etl.http.position-invalid");

            // The page record count was checked above, before allocating its result array.
            var records = new EtlJsonPageRecord[recordCount];
            var localOrdinal = 1;
            foreach (var record in selectedRecords.EnumerateArray())
            {
                var rawBytes = JsonMarshal.GetRawUtf8Value(record).Length;
                var absoluteIndex = firstRecordOrdinal + (localOrdinal - 1L);
                records[localOrdinal - 1] = ReadRecord(record, rawBytes, absoluteIndex, localOrdinal, source, limits, fieldPointers);
                localOrdinal++;
            }

            var continuation = ReadContinuation(document.RootElement, source.Pagination, continuationPointer, policy.MaxContinuationBytes);
            return new EtlJsonPage(Array.AsReadOnly(records), continuation);
        }
    }

    private static void ValidateConfiguration(EtlHttpJsonSource source, EtlConnectorLimits limits, EtlHttpPolicy policy, long firstRecordOrdinal)
    {
        if (firstRecordOrdinal < 0 || limits.MaxRecordBytes <= 0 || limits.MaxFields <= 0 ||
            source.Fields is null || source.Fields.Count == 0 || source.Fields.Count > limits.MaxFields ||
            policy.MaxPageBytes <= 0 || policy.MaxPageRecords <= 0 || policy.MaxJsonDepth is <= 0 or > 64 ||
            policy.MaxContinuationBytes <= 0)
            throw Refused("etl.http.page-policy-invalid");

        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in source.Fields)
            if (field is null || string.IsNullOrWhiteSpace(field.Name) || !Enum.IsDefined(field.Type) || !fieldNames.Add(field.Name))
                throw Refused("etl.http.page-policy-invalid");

        if (source.Pagination is not EtlSinglePagePagination and not EtlTokenPagination and not EtlNextLinkPagination)
            throw Refused("etl.http.selector-invalid");
    }

    private static EtlJsonPageRecord ReadRecord(
        JsonElement record,
        int rawBytes,
        long absoluteIndex,
        int localOrdinal,
        EtlHttpJsonSource source,
        EtlConnectorLimits limits,
        IReadOnlyList<EtlJsonPointer> fieldPointers)
    {
        if (rawBytes > limits.MaxRecordBytes)
            return Error("etl.record-too-large", null);
        var values = new Dictionary<string, EtlTypedValue>(source.Fields.Count, StringComparer.Ordinal);
        for (var index = 0; index < source.Fields.Count; index++)
        {
            var field = source.Fields[index];
            if (!fieldPointers[index].TryResolve(record, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                if (field.Required)
                    return Error("etl.required-value-missing", field.Name);

                values.Add(field.Name, new EtlTypedValue(field.Type, null));
                continue;
            }

            if (!TryReadScalar(value, field.Type, out var typedValue))
                return Error("etl.invalid-value", field.Name);

            values.Add(field.Name, new EtlTypedValue(field.Type, typedValue));
        }

        // The durable page index is absolute; the transformer's legacy diagnostic ordinal stays page-local.
        return new EtlJsonPageRecord(absoluteIndex, rawBytes, new EtlTypedRow(localOrdinal, values), null);

        EtlJsonPageRecord Error(string code, string? field) =>
            new(absoluteIndex, rawBytes, null, new EtlRowError(localOrdinal, field, code));
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

    private static string? ReadContinuation(
        JsonElement document,
        EtlHttpPagination pagination,
        EtlJsonPointer? pointer,
        int maximumBytes)
    {
        if (pagination is EtlSinglePagePagination)
            return null;

        if (pointer is null || !pointer.TryResolve(document, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;

        string? continuation = pagination switch
        {
            EtlTokenPagination when value.ValueKind == JsonValueKind.String => value.GetString(),
            EtlTokenPagination when value.ValueKind == JsonValueKind.Number => value.GetRawText(),
            EtlTokenPagination when value.ValueKind is JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            EtlNextLinkPagination when value.ValueKind == JsonValueKind.String => value.GetString(),
            EtlTokenPagination or EtlNextLinkPagination => throw Refused("etl.http.continuation-invalid"),
            _ => throw Refused("etl.http.selector-invalid")
        };

        if (string.IsNullOrEmpty(continuation))
            return null;

        int continuationBytes;
        try
        {
            continuationBytes = StrictUtf8.GetByteCount(continuation);
        }
        catch (EncoderFallbackException)
        {
            throw Refused("etl.http.continuation-invalid");
        }

        if (continuationBytes > maximumBytes)
            throw Refused("etl.http.continuation-too-large");
        return continuation;
    }

    private static void ValidateDocument(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw Refused("etl.http.duplicate-property");
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

    private static EtlJsonPageReadException Refused(string code) => new(code);
}
