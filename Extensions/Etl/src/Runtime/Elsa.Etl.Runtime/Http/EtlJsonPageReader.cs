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
                EtlJsonRecordReader.ValidateDocument(document.RootElement);
            }
            catch (EtlDuplicatePropertyException)
            {
                throw Refused("etl.http.duplicate-property");
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
            policy.MaxContinuationBytes <= 0 || policy.MaxContinuationBytes > EtlHttpPolicy.MaximumSupportedContinuationBytes)
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
            return new(absoluteIndex, rawBytes, null, new EtlRowError(localOrdinal, null, "etl.record-too-large"));

        // The durable page index is absolute; the transformer's legacy diagnostic ordinal stays page-local.
        var (row, error) = EtlJsonRecordReader.ReadFields(record, source.Fields, fieldPointers, localOrdinal);
        return new EtlJsonPageRecord(absoluteIndex, rawBytes, row, error);
    }

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

    private static EtlJsonPageReadException Refused(string code) => new(code);
}
