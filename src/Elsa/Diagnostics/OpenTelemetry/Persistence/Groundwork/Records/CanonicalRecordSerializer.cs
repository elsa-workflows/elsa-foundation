using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Groundwork.DiagnosticRecords;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;

/// <summary>
/// Maps immutable OpenTelemetry signals to canonical Groundwork diagnostic records and restores their domain models.
/// </summary>
public sealed class CanonicalRecordSerializer
{
    public const int SchemaVersion = 1;

    private const string TraceKind = "trace";
    private const string SpanKind = "span";
    private const string MetricPointKind = "metricPoint";
    private const string LogKind = "log";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Creates the immutable record form of a trace summary.</summary>
    /// <exception cref="RecordPayloadException">The trace contains data that cannot be represented canonically.</exception>
    public DiagnosticRecordInput ToRecord(string recordId, TelemetryTrace trace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        ArgumentNullException.ThrowIfNull(trace);

        return Map(() =>
        {
            var traceId = RequiredText(trace.TraceId, nameof(trace.TraceId));
            var resourceIds = CanonicalSet(trace.ResourceIds, nameof(trace.ResourceIds), requireValue: true);
            var workflowInstanceIds = CanonicalSet(trace.WorkflowInstanceIds, nameof(trace.WorkflowInstanceIds));
            EnumValue<SpanStatus>((int)trace.Status, nameof(trace.Status));

            return new(
                recordId,
                trace.StartTime,
                Serialize(TraceKind, new TracePayload(
                    traceId,
                    trace.RootSpanId,
                    trace.Name,
                    trace.StartTime.UtcTicks,
                    trace.EndTime.UtcTicks,
                    trace.Duration.Ticks,
                    (int)trace.Status,
                    resourceIds,
                    workflowInstanceIds,
                    trace.SpanCount)),
                Fields(
                    (RecordFields.TraceId, Strings(traceId)),
                    (RecordFields.ResourceId, Strings(resourceIds)),
                    (RecordFields.WorkflowInstanceId, Strings(workflowInstanceIds)),
                    (RecordFields.Status, Int64((long)trace.Status)),
                    (RecordFields.StartTime, Timestamp(trace.StartTime)),
                    (RecordFields.EndTime, Timestamp(trace.EndTime)),
                    (RecordFields.Name, OptionalString(trace.Name))));
        });
    }

    /// <summary>Creates the immutable record form of a span.</summary>
    /// <exception cref="RecordPayloadException">The span contains data that cannot be represented canonically.</exception>
    public DiagnosticRecordInput ToRecord(string recordId, TelemetrySpan span)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        ArgumentNullException.ThrowIfNull(span);

        return Map(() =>
        {
            var id = RequiredText(span.Id, nameof(span.Id));
            var traceId = RequiredText(span.TraceId, nameof(span.TraceId));
            var spanId = RequiredText(span.SpanId, nameof(span.SpanId));
            var resourceId = RequiredText(span.ResourceId, nameof(span.ResourceId));
            var name = RequiredText(span.Name, nameof(span.Name));
            var kind = RequiredText(span.Kind, nameof(span.Kind));
            EnumValue<SpanStatus>((int)span.Status, nameof(span.Status));

            return new(
                recordId,
                span.StartTime,
                Serialize(SpanKind, new SpanPayload(
                    id,
                    traceId,
                    spanId,
                    span.ParentSpanId,
                    resourceId,
                    name,
                    kind,
                    span.StartTime.UtcTicks,
                    span.EndTime.UtcTicks,
                    (int)span.Status,
                    span.StatusDescription,
                    Canonicalize(span.Attributes),
                    Required(span.Events, nameof(span.Events)).Select(ToPayload).ToArray(),
                    Required(span.Links, nameof(span.Links)).Select(ToPayload).ToArray())),
                Fields(
                    (RecordFields.TraceId, Strings(traceId)),
                    (RecordFields.SpanId, Strings(spanId)),
                    (RecordFields.ParentSpanId, OptionalString(span.ParentSpanId)),
                    (RecordFields.ResourceId, Strings(resourceId)),
                    (RecordFields.Status, Int64((long)span.Status)),
                    (RecordFields.StartTime, Timestamp(span.StartTime)),
                    (RecordFields.EndTime, Timestamp(span.EndTime)),
                    (RecordFields.Name, Strings(name)),
                    (RecordFields.OrderKey, Strings(OrderKey(span.StartTime, spanId)))));
        });
    }

    /// <summary>Creates the immutable record form of a metric point.</summary>
    /// <exception cref="RecordPayloadException">The point contains data that cannot be represented canonically.</exception>
    public DiagnosticRecordInput ToRecord(string recordId, MetricPoint point, string? serviceName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        ArgumentNullException.ThrowIfNull(point);

        return Map(() =>
        {
            var id = RequiredText(point.Id, nameof(point.Id));
            var instrumentId = RequiredText(point.InstrumentId, nameof(point.InstrumentId));
            var instrumentName = RequiredText(point.InstrumentName, nameof(point.InstrumentName));
            var resourceId = RequiredText(point.ResourceId, nameof(point.ResourceId));
            var canonicalServiceName = serviceName is null ? null : RequiredText(serviceName, nameof(serviceName));

            return new(
                recordId,
                point.Timestamp,
                Serialize(MetricPointKind, new MetricPointPayload(
                    id,
                    instrumentId,
                    instrumentName,
                    resourceId,
                    point.Timestamp.UtcTicks,
                    ToBits(point.Value),
                    ToBits(point.Sum),
                    point.Count,
                    Canonicalize(point.Attributes),
                    point.TraceId,
                    point.SpanId)),
                Fields(
                    (RecordFields.InstrumentId, Strings(instrumentId)),
                    (RecordFields.InstrumentName, Strings(instrumentName)),
                    (RecordFields.ResourceId, Strings(resourceId)),
                    (RecordFields.ServiceName, OptionalString(canonicalServiceName)),
                    (RecordFields.Timestamp, Timestamp(point.Timestamp)),
                    (RecordFields.TraceId, OptionalString(point.TraceId)),
                    (RecordFields.SpanId, OptionalString(point.SpanId)),
                    (RecordFields.OrderKey, Strings(OrderKey(point.Timestamp, id)))));
        });
    }

    /// <summary>Creates the immutable record form of an OTLP log record.</summary>
    /// <exception cref="RecordPayloadException">The log contains data that cannot be represented canonically.</exception>
    public DiagnosticRecordInput ToRecord(string recordId, OtlpLogRecord log, string? serviceName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        ArgumentNullException.ThrowIfNull(log);

        return Map(() =>
        {
            var id = RequiredText(log.Id, nameof(log.Id));
            var resourceId = RequiredText(log.ResourceId, nameof(log.ResourceId));
            var canonicalServiceName = serviceName is null ? null : RequiredText(serviceName, nameof(serviceName));
            var severityText = Required(log.SeverityText, nameof(log.SeverityText));
            var body = Required(log.Body, nameof(log.Body));

            return new(
                recordId,
                log.Timestamp,
                Serialize(LogKind, new LogPayload(
                    id,
                    resourceId,
                    log.Timestamp.UtcTicks,
                    severityText,
                    log.SeverityNumber,
                    body,
                    log.TraceId,
                    log.SpanId,
                    Canonicalize(log.Attributes))),
                Fields(
                    (RecordFields.ResourceId, Strings(resourceId)),
                    (RecordFields.ServiceName, OptionalString(canonicalServiceName)),
                    (RecordFields.Timestamp, Timestamp(log.Timestamp)),
                    (RecordFields.SeverityText, Strings(severityText)),
                    (RecordFields.SeverityNumber, OptionalInt64(log.SeverityNumber)),
                    (RecordFields.TraceId, OptionalString(log.TraceId)),
                    (RecordFields.SpanId, OptionalString(log.SpanId)),
                    (RecordFields.Body, Strings(body)),
                    (RecordFields.OrderKey, Strings(OrderKey(log.Timestamp, id)))));
        });
    }

    /// <summary>Restores a trace summary from a durable record.</summary>
    /// <exception cref="RecordPayloadException">The record payload is malformed or has an incompatible kind or schema.</exception>
    public TelemetryTrace ToTrace(DiagnosticRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Deserialize<TracePayload, TelemetryTrace>(record.Payload, TraceKind, x => new(
            RequiredText(x.TraceId, nameof(x.TraceId)),
            x.RootSpanId,
            x.Name,
            Utc(x.StartTimeUtcTicks),
            Utc(x.EndTimeUtcTicks),
            TimeSpan.FromTicks(x.DurationTicks),
            EnumValue<SpanStatus>(x.Status, nameof(x.Status)),
            CanonicalSet(x.ResourceIds, nameof(x.ResourceIds), requireValue: true),
            CanonicalSet(x.WorkflowInstanceIds, nameof(x.WorkflowInstanceIds)),
            x.SpanCount));
    }

    /// <summary>Restores a span from a durable record.</summary>
    /// <exception cref="RecordPayloadException">The record payload is malformed or has an incompatible kind or schema.</exception>
    public TelemetrySpan ToSpan(DiagnosticRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Deserialize<SpanPayload, TelemetrySpan>(record.Payload, SpanKind, x => new(
            RequiredText(x.Id, nameof(x.Id)),
            RequiredText(x.TraceId, nameof(x.TraceId)),
            RequiredText(x.SpanId, nameof(x.SpanId)),
            x.ParentSpanId,
            RequiredText(x.ResourceId, nameof(x.ResourceId)),
            RequiredText(x.Name, nameof(x.Name)),
            RequiredText(x.Kind, nameof(x.Kind)),
            Utc(x.StartTimeUtcTicks),
            Utc(x.EndTimeUtcTicks),
            EnumValue<SpanStatus>(x.Status, nameof(x.Status)),
            x.StatusDescription,
            Restore(x.Attributes),
            Required(x.Events, nameof(x.Events)).Select(ToModel).ToArray(),
            Required(x.Links, nameof(x.Links)).Select(ToModel).ToArray()));
    }

    /// <summary>Restores a metric point from a durable record.</summary>
    /// <exception cref="RecordPayloadException">The record payload is malformed or has an incompatible kind or schema.</exception>
    public MetricPoint ToMetricPoint(DiagnosticRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Deserialize<MetricPointPayload, MetricPoint>(record.Payload, MetricPointKind, x => new(
            RequiredText(x.Id, nameof(x.Id)),
            RequiredText(x.InstrumentId, nameof(x.InstrumentId)),
            RequiredText(x.InstrumentName, nameof(x.InstrumentName)),
            RequiredText(x.ResourceId, nameof(x.ResourceId)),
            Utc(x.TimestampUtcTicks),
            FromBits(x.ValueBits),
            FromBits(x.SumBits),
            x.Count,
            Restore(x.Attributes),
            x.TraceId,
            x.SpanId));
    }

    /// <summary>Restores an OTLP log record from a durable record.</summary>
    /// <exception cref="RecordPayloadException">The record payload is malformed or has an incompatible kind or schema.</exception>
    public OtlpLogRecord ToLog(DiagnosticRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Deserialize<LogPayload, OtlpLogRecord>(record.Payload, LogKind, x => new(
            RequiredText(x.Id, nameof(x.Id)),
            RequiredText(x.ResourceId, nameof(x.ResourceId)),
            Utc(x.TimestampUtcTicks),
            Required(x.SeverityText, nameof(x.SeverityText)),
            x.SeverityNumber,
            Required(x.Body, nameof(x.Body)),
            x.TraceId,
            x.SpanId,
            Restore(x.Attributes)));
    }

    private static DiagnosticRecordInput Map(Func<DiagnosticRecordInput> map)
    {
        try
        {
            return map();
        }
        catch (RecordPayloadException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or NotSupportedException)
        {
            throw new RecordPayloadException("The OpenTelemetry record cannot be represented as a canonical payload.", exception);
        }
    }

    private static string Serialize<TPayload>(string kind, TPayload value)
    {
        try
        {
            return JsonSerializer.Serialize(new RecordEnvelope<TPayload>(SchemaVersion, kind, value), SerializerOptions);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or NotSupportedException)
        {
            throw new RecordPayloadException("The OpenTelemetry record cannot be serialized.", exception);
        }
    }

    private static TModel Deserialize<TPayload, TModel>(string payload, string expectedKind, Func<TPayload, TModel> map)
        where TPayload : class
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<RecordEnvelope<TPayload>>(payload, SerializerOptions)
                           ?? throw new RecordPayloadException("The OpenTelemetry record payload is empty.");
            if (envelope.SchemaVersion != SchemaVersion)
                throw new RecordPayloadException($"OpenTelemetry record schema '{envelope.SchemaVersion}' is not supported.");
            if (!StringComparer.Ordinal.Equals(envelope.Kind, expectedKind))
                throw new RecordPayloadException($"OpenTelemetry record kind '{envelope.Kind}' is not '{expectedKind}'.");
            if (envelope.Value is null)
                throw new RecordPayloadException("The OpenTelemetry record payload has no value.");
            return map(envelope.Value);
        }
        catch (RecordPayloadException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or NotSupportedException or OverflowException)
        {
            throw new RecordPayloadException("The OpenTelemetry record payload is malformed.", exception);
        }
    }

    private static Dictionary<string, IReadOnlyList<DiagnosticFieldValue>> Fields(
        params (string Name, IReadOnlyList<DiagnosticFieldValue> Values)[] fields)
    {
        var result = new Dictionary<string, IReadOnlyList<DiagnosticFieldValue>>(StringComparer.Ordinal);
        foreach (var (name, values) in fields)
        {
            if (values.Count > 0)
                result.Add(name, values);
        }
        return result;
    }

    private static IReadOnlyList<DiagnosticFieldValue> Strings(string value) => [DiagnosticFieldValue.String(value)];

    private static IReadOnlyList<DiagnosticFieldValue> Strings(IEnumerable<string> values) =>
        values.Select(DiagnosticFieldValue.String).ToArray();

    private static IReadOnlyList<DiagnosticFieldValue> OptionalString(string? value) =>
        value is null ? [] : Strings(value);

    private static IReadOnlyList<DiagnosticFieldValue> Int64(long value) => [DiagnosticFieldValue.Int64(value)];

    private static IReadOnlyList<DiagnosticFieldValue> OptionalInt64(long? value) =>
        value is null ? [] : Int64(value.Value);

    private static IReadOnlyList<DiagnosticFieldValue> Timestamp(DateTimeOffset value) =>
        [DiagnosticFieldValue.Timestamp(value)];

    private static string OrderKey(DateTimeOffset timestamp, string id) =>
        $"{timestamp.UtcTicks:D19}:{id}";

    private static AttributePayload[] Canonicalize(IDictionary<string, string?> attributes) =>
        Required(attributes, nameof(attributes))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new AttributePayload(RequiredText(x.Key, "attribute key"), x.Value))
            .ToArray();

    private static SpanEventPayload ToPayload(TelemetrySpanEvent? item)
    {
        var value = Required(item, "span event");
        return new(RequiredText(value.Name, nameof(value.Name)), value.Timestamp.UtcTicks, Canonicalize(value.Attributes));
    }

    private static SpanLinkPayload ToPayload(TelemetrySpanLink? item)
    {
        var value = Required(item, "span link");
        return new(
            RequiredText(value.TraceId, nameof(value.TraceId)),
            RequiredText(value.SpanId, nameof(value.SpanId)),
            Canonicalize(value.Attributes));
    }

    private static TelemetrySpanEvent ToModel(SpanEventPayload? item)
    {
        var value = Required(item, "span event");
        return new(RequiredText(value.Name, nameof(value.Name)), Utc(value.TimestampUtcTicks), Restore(value.Attributes));
    }

    private static TelemetrySpanLink ToModel(SpanLinkPayload? item)
    {
        var value = Required(item, "span link");
        return new(
            RequiredText(value.TraceId, nameof(value.TraceId)),
            RequiredText(value.SpanId, nameof(value.SpanId)),
            Restore(value.Attributes));
    }

    private static string[] CanonicalSet(
        IEnumerable<string>? values,
        string field,
        bool requireValue = false)
    {
        var result = Required(values, field)
            .Select(x => RequiredText(x, field))
            .OrderBy(x => x, StringComparer.Ordinal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requireValue && result.Length == 0)
            throw new RecordPayloadException($"The OpenTelemetry record field '{field}' requires at least one value.");

        return result;
    }

    private static Dictionary<string, string?> Restore(AttributePayload[]? attributes)
    {
        if (attributes is null)
            throw new RecordPayloadException("The OpenTelemetry record attributes are missing.");

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var attribute in attributes)
        {
            var value = Required(attribute, "attribute");
            if (value.Key is null || !result.TryAdd(value.Key, value.Value))
                throw new RecordPayloadException("The OpenTelemetry record contains an invalid or duplicate attribute key.");
        }
        return result;
    }

    private static T Required<T>(T? value, string field) where T : class =>
        value ?? throw new RecordPayloadException($"The OpenTelemetry record field '{field}' is required.");

    private static string RequiredText(string? value, string field) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new RecordPayloadException($"The OpenTelemetry record field '{field}' requires a non-empty value.");

    private static TEnum EnumValue<TEnum>(int value, string field) where TEnum : struct, Enum =>
        Enum.IsDefined(typeof(TEnum), value)
            ? (TEnum)Enum.ToObject(typeof(TEnum), value)
            : throw new RecordPayloadException($"The OpenTelemetry record field '{field}' has an unsupported value.");

    private static DateTimeOffset Utc(long ticks) => new(ticks, TimeSpan.Zero);

    private static long? ToBits(double? value) => value.HasValue ? BitConverter.DoubleToInt64Bits(value.Value) : null;

    private static double? FromBits(long? bits) => bits.HasValue ? BitConverter.Int64BitsToDouble(bits.Value) : null;

    private sealed record RecordEnvelope<TPayload>(int SchemaVersion, string Kind, TPayload Value);

    private sealed record AttributePayload(string Key, string? Value);

    private sealed record TracePayload(
        string TraceId,
        string? RootSpanId,
        string? Name,
        long StartTimeUtcTicks,
        long EndTimeUtcTicks,
        long DurationTicks,
        int Status,
        string[] ResourceIds,
        string[] WorkflowInstanceIds,
        int SpanCount);

    private sealed record SpanPayload(
        string Id,
        string TraceId,
        string SpanId,
        string? ParentSpanId,
        string ResourceId,
        string Name,
        string Kind,
        long StartTimeUtcTicks,
        long EndTimeUtcTicks,
        int Status,
        string? StatusDescription,
        AttributePayload[] Attributes,
        SpanEventPayload[] Events,
        SpanLinkPayload[] Links);

    private sealed record SpanEventPayload(string Name, long TimestampUtcTicks, AttributePayload[] Attributes);

    private sealed record SpanLinkPayload(string TraceId, string SpanId, AttributePayload[] Attributes);

    private sealed record MetricPointPayload(
        string Id,
        string InstrumentId,
        string InstrumentName,
        string ResourceId,
        long TimestampUtcTicks,
        long? ValueBits,
        long? SumBits,
        long? Count,
        AttributePayload[] Attributes,
        string? TraceId,
        string? SpanId);

    private sealed record LogPayload(
        string Id,
        string ResourceId,
        long TimestampUtcTicks,
        string SeverityText,
        int? SeverityNumber,
        string Body,
        string? TraceId,
        string? SpanId,
        AttributePayload[] Attributes);
}
