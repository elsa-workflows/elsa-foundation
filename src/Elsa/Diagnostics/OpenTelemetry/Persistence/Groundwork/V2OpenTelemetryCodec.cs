using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Groundwork.Kernel;
using Groundwork.Store;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

internal static class V2OpenTelemetryCodec
{
    internal const int MaximumSummaryElementCodeUnits = 512;
    internal const int MaximumSummaryElementCount = 5_000;
    // UnicodeOrdinalIgnoreCase search keys expand to at most six ASCII code units per
    // source code unit. SQL Server's portable ordinary-string ceiling is nvarchar(4000),
    // so 666 is the largest whole source bound that remains valid on every provider.
    internal const int MaximumSummaryNameCodeUnits = 666;
    internal const int MaximumSummaryNameSearchKeyCodeUnits = MaximumSummaryNameCodeUnits * 6;
    internal const int MaximumCanonicalSearchKeyCodeUnits = MaximumSummaryElementCodeUnits * 6;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = false
    };

    internal static StorageValues Trace(TelemetryTrace value, string? serviceName = null) => new(new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        [V2OpenTelemetryStorageSchema.Id] = value.TraceId,
        [V2OpenTelemetryStorageSchema.TraceId] = Required(value.TraceId, nameof(value.TraceId)),
        [V2OpenTelemetryStorageSchema.TraceKey] = TraceKey(value.TraceId),
        [V2OpenTelemetryStorageSchema.RootSpanId] = value.RootSpanId,
        [V2OpenTelemetryStorageSchema.ResourceId] = Required(value.ResourceIds.FirstOrDefault(), nameof(value.ResourceIds)),
        [V2OpenTelemetryStorageSchema.ServiceName] = serviceName,
        [V2OpenTelemetryStorageSchema.WorkflowInstanceId] = value.WorkflowInstanceIds.FirstOrDefault(),
        [V2OpenTelemetryStorageSchema.Name] = value.Name,
        [V2OpenTelemetryStorageSchema.Status] = (long)value.Status,
        [V2OpenTelemetryStorageSchema.StartTime] = value.StartTime,
        [V2OpenTelemetryStorageSchema.EndTime] = value.EndTime,
        [V2OpenTelemetryStorageSchema.SpanCount] = (long)value.SpanCount,
        [V2OpenTelemetryStorageSchema.Payload] = Serialize(value)
    });

    internal static StorageValues TraceSummary(TelemetryTrace value, IEnumerable<string> serviceKeys)
    {
        ArgumentNullException.ThrowIfNull(value);
        var resourceIds = SummaryElements(value.ResourceIds, nameof(value.ResourceIds));
        var workflowInstanceIds = SummaryElements(value.WorkflowInstanceIds, nameof(value.WorkflowInstanceIds));
        var resourceKeys = CanonicalElements(resourceIds.Select(CanonicalSearchKey), V2OpenTelemetryStorageSchema.ResourceKeys);
        var services = CanonicalElements(
            serviceKeys,
            V2OpenTelemetryStorageSchema.ServiceNames);
        var traceIdSearchKey = BoundedSearchKey(value.TraceId, 256, nameof(value.TraceId));
        var name = string.IsNullOrWhiteSpace(value.Name)
            ? null
            : RequiredBounded(value.Name, MaximumSummaryNameCodeUnits, nameof(value.Name));
        var normalized = value with
        {
            ResourceIds = resourceIds,
            WorkflowInstanceIds = workflowInstanceIds,
            Name = name
        };

        return new(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [V2OpenTelemetryStorageSchema.TraceKey] = TraceKey(normalized.TraceId),
            [V2OpenTelemetryStorageSchema.TraceId] = Required(normalized.TraceId, nameof(value.TraceId)),
            [V2OpenTelemetryStorageSchema.TraceIdSearchKey] = traceIdSearchKey,
            [V2OpenTelemetryStorageSchema.RootSpanId] = normalized.RootSpanId,
            [V2OpenTelemetryStorageSchema.Name] = name,
            [V2OpenTelemetryStorageSchema.NameSearchKey] = name is null ? null : BoundedSearchKey(
                name,
                MaximumSummaryNameCodeUnits,
                nameof(value.Name)),
            [V2OpenTelemetryStorageSchema.Status] = (long)normalized.Status,
            [V2OpenTelemetryStorageSchema.StartTime] = normalized.StartTime,
            [V2OpenTelemetryStorageSchema.EndTime] = normalized.EndTime,
            [V2OpenTelemetryStorageSchema.SpanCount] = (long)normalized.SpanCount,
            [V2OpenTelemetryStorageSchema.ResourceIds] = Serialize(resourceIds),
            [V2OpenTelemetryStorageSchema.ResourceKeys] = Serialize(resourceKeys),
            [V2OpenTelemetryStorageSchema.ServiceNames] = Serialize(services),
            [V2OpenTelemetryStorageSchema.WorkflowInstanceIds] = Serialize(workflowInstanceIds),
            [V2OpenTelemetryStorageSchema.Payload] = Serialize(normalized)
        });
    }

    internal static TelemetryTrace DeserializeTraceSummary(IReadOnlyDictionary<string, object?> row) =>
        Deserialize<TelemetryTrace>(row);

    internal static IReadOnlyList<string> DeserializeSummaryElements(
        IReadOnlyDictionary<string, object?> row,
        string field)
    {
        if (!row.TryGetValue(field, out var value) || value is null)
            throw new InvalidDataException($"The OpenTelemetry trace summary omitted '{field}'.");

        string[] elements;
        try
        {
            elements = JsonSerializer.Deserialize<string[]>(JsonText(value, field), Json) ??
                       throw new InvalidDataException($"The OpenTelemetry trace summary field '{field}' was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The OpenTelemetry trace summary field '{field}' was not a string array.",
                exception);
        }

        if (elements.Length > MaximumSummaryElementCount)
            throw new InvalidDataException(
                $"The OpenTelemetry trace summary field '{field}' exceeded the declared {MaximumSummaryElementCount}-element bound.");

        string? previous = null;
        foreach (var element in elements)
        {
            if (string.IsNullOrWhiteSpace(element) || element.Length > MaximumCanonicalSearchKeyCodeUnits)
                throw new InvalidDataException(
                    $"The OpenTelemetry trace summary field '{field}' contained an invalid element.");
            if (previous is not null && StringComparer.Ordinal.Compare(previous, element) >= 0)
                throw new InvalidDataException(
                    $"The OpenTelemetry trace summary field '{field}' was not strictly ordered and unique.");
            previous = element;
        }
        return elements;
    }

    internal static string CanonicalSearchKey(string value) =>
        PortableStringComparison.CreateSearchKey(
            Required(value, nameof(value)),
            PortableStringComparisonPolicy.UnicodeOrdinalIgnoreCase);

    internal static string TraceKey(string traceId)
    {
        var comparisonKey = CanonicalSearchKey(traceId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(comparisonKey)))
            .ToLowerInvariant();
    }

    internal static StorageValues Span(TelemetrySpan value) => new(new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        [V2OpenTelemetryStorageSchema.Id] = Required(value.Id, nameof(value.Id)),
        [V2OpenTelemetryStorageSchema.TraceId] = Required(value.TraceId, nameof(value.TraceId)),
        [V2OpenTelemetryStorageSchema.TraceKey] = TraceKey(value.TraceId),
        [V2OpenTelemetryStorageSchema.SpanId] = Required(value.SpanId, nameof(value.SpanId)),
        [V2OpenTelemetryStorageSchema.ResourceId] = Required(value.ResourceId, nameof(value.ResourceId)),
        [V2OpenTelemetryStorageSchema.Name] = Required(value.Name, nameof(value.Name)),
        [V2OpenTelemetryStorageSchema.Status] = (long)value.Status,
        [V2OpenTelemetryStorageSchema.StartTime] = value.StartTime,
        [V2OpenTelemetryStorageSchema.EndTime] = value.EndTime,
        [V2OpenTelemetryStorageSchema.Payload] = Serialize(value)
    });

    internal static StorageValues MetricPoint(MetricPoint value, string? serviceName) => new(new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        [V2OpenTelemetryStorageSchema.Id] = Required(value.Id, nameof(value.Id)),
        [V2OpenTelemetryStorageSchema.InstrumentId] = Required(value.InstrumentId, nameof(value.InstrumentId)),
        [V2OpenTelemetryStorageSchema.InstrumentName] = Required(value.InstrumentName, nameof(value.InstrumentName)),
        [V2OpenTelemetryStorageSchema.ResourceId] = Required(value.ResourceId, nameof(value.ResourceId)),
        [V2OpenTelemetryStorageSchema.ServiceName] = serviceName,
        [V2OpenTelemetryStorageSchema.Timestamp] = value.Timestamp,
        [V2OpenTelemetryStorageSchema.Payload] = Serialize(value)
    });

    internal static StorageValues Log(OtlpLogRecord value, string? serviceName) => new(new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        [V2OpenTelemetryStorageSchema.Id] = Required(value.Id, nameof(value.Id)),
        [V2OpenTelemetryStorageSchema.ResourceId] = Required(value.ResourceId, nameof(value.ResourceId)),
        [V2OpenTelemetryStorageSchema.ServiceName] = serviceName,
        [V2OpenTelemetryStorageSchema.TraceId] = value.TraceId,
        [V2OpenTelemetryStorageSchema.TraceKey] = string.IsNullOrWhiteSpace(value.TraceId) ? null : TraceKey(value.TraceId),
        [V2OpenTelemetryStorageSchema.SpanId] = value.SpanId,
        [V2OpenTelemetryStorageSchema.SeverityText] = Required(value.SeverityText, nameof(value.SeverityText)),
        [V2OpenTelemetryStorageSchema.SeverityNumber] = value.SeverityNumber is { } severity ? (long)severity : null,
        [V2OpenTelemetryStorageSchema.Body] = Required(value.Body, nameof(value.Body)),
        [V2OpenTelemetryStorageSchema.Timestamp] = value.Timestamp,
        [V2OpenTelemetryStorageSchema.Payload] = Serialize(value)
    });

    internal static StorageValues Resource(TelemetryResource value) => new(new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        [V2OpenTelemetryStorageSchema.Id] = Required(value.Id, nameof(value.Id)),
        [V2OpenTelemetryStorageSchema.ServiceName] = Required(value.ServiceName, nameof(value.ServiceName)),
        [V2OpenTelemetryStorageSchema.Status] = (long)value.Status,
        [V2OpenTelemetryStorageSchema.LastSeen] = value.LastSeen,
        [V2OpenTelemetryStorageSchema.Payload] = Serialize(value)
    });

    internal static StorageValues Instrument(MetricInstrument value, DateTimeOffset observedAt) => new(new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        [V2OpenTelemetryStorageSchema.Id] = Required(value.Id, nameof(value.Id)),
        [V2OpenTelemetryStorageSchema.ResourceId] = Required(value.ResourceId, nameof(value.ResourceId)),
        [V2OpenTelemetryStorageSchema.InstrumentName] = Required(value.Name, nameof(value.Name)),
        [V2OpenTelemetryStorageSchema.Kind] = (long)value.Kind,
        [V2OpenTelemetryStorageSchema.LastSeen] = observedAt,
        [V2OpenTelemetryStorageSchema.Payload] = Serialize(value)
    });

    internal static StorageValues Ledger(Elsa.Diagnostics.Persistence.Draining.DiagnosticsDrainBatchId batchId, string fingerprint) => new(new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        [V2OpenTelemetryStorageSchema.BatchId] = batchId.ToString(),
        [V2OpenTelemetryStorageSchema.Fingerprint] = fingerprint,
        [V2OpenTelemetryStorageSchema.CreatedAt] = batchId.IssuedAt,
        [V2OpenTelemetryStorageSchema.Status] = "committed"
    });

    internal static T Deserialize<T>(IReadOnlyDictionary<string, object?> row)
    {
        if (!row.TryGetValue(V2OpenTelemetryStorageSchema.Payload, out var payload) || payload is null)
            throw new InvalidDataException("The OpenTelemetry v2 row did not contain a payload.");
        var json = JsonText(payload, V2OpenTelemetryStorageSchema.Payload);
        return JsonSerializer.Deserialize<T>(json, Json) ??
               throw new InvalidDataException("The OpenTelemetry v2 payload was empty.");
    }

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static string JsonText(object value, string field) => value switch
    {
        string text => text,
        JsonElement element => element.GetRawText(),
        JsonDocument document => document.RootElement.GetRawText(),
        _ => throw new InvalidDataException(
            $"The OpenTelemetry v2 field '{field}' has an unsupported JSON representation.")
    };

    private static string[] SummaryElements(IEnumerable<string> values, string field)
    {
        ArgumentNullException.ThrowIfNull(values);
        var bounded = values.Select((value, index) =>
        {
            var required = Required(value, $"{field}[{index}]");
            if (required.Length > MaximumSummaryElementCodeUnits)
                throw new ArgumentOutOfRangeException(
                    field,
                    required.Length,
                    $"OpenTelemetry field '{field}[{index}]' exceeds the declared {MaximumSummaryElementCodeUnits}-code-unit bound.");
            return required;
        });
        var canonical = bounded
            .GroupBy(CanonicalSearchKey, StringComparer.Ordinal)
            .Select(group => group.OrderBy(value => value, StringComparer.Ordinal).First())
            .OrderBy(CanonicalSearchKey, StringComparer.Ordinal)
            .ToArray();
        if (canonical.Length > MaximumSummaryElementCount)
            throw new ArgumentOutOfRangeException(
                field,
                canonical.Length,
                $"OpenTelemetry field '{field}' exceeds the declared {MaximumSummaryElementCount}-element bound.");
        return canonical;
    }

    private static string[] CanonicalElements(IEnumerable<string> values, string field)
    {
        var canonical = values.Select((value, index) => RequiredBounded(
                value,
                MaximumCanonicalSearchKeyCodeUnits,
                $"{field}[{index}]"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (canonical.Length > MaximumSummaryElementCount)
            throw new ArgumentOutOfRangeException(
                field,
                canonical.Length,
                $"OpenTelemetry field '{field}' exceeds the declared {MaximumSummaryElementCount}-element bound.");
        return canonical;
    }

    private static string BoundedSearchKey(string value, int maximumSourceCodeUnits, string field)
    {
        var bounded = RequiredBounded(value, maximumSourceCodeUnits, field);
        return RequiredBounded(
            CanonicalSearchKey(bounded),
            checked(maximumSourceCodeUnits * 6),
            $"{field} search key");
    }

    private static string RequiredBounded(string? value, int maximumCodeUnits, string field)
    {
        var required = Required(value, field);
        if (required.Length > maximumCodeUnits)
        {
            throw new ArgumentOutOfRangeException(
                field,
                required.Length,
                $"OpenTelemetry field '{field}' exceeds the declared {maximumCodeUnits}-code-unit bound.");
        }
        return required;
    }

    private static string Required(string? value, string field) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"OpenTelemetry field '{field}' is required.", field);
}
