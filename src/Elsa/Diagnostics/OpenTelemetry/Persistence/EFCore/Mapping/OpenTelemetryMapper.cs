using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Mapping;

internal static class OpenTelemetryMapper
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static PersistedTelemetryResource ToEntity(TelemetryResource resource) => new()
    {
        Id = resource.Id,
        ServiceName = resource.ServiceName,
        ServiceInstanceId = resource.ServiceInstanceId,
        TelemetrySdkLanguage = resource.TelemetrySdkLanguage,
        AttributesJson = SerializeDictionary(resource.Attributes),
        LastSeen = resource.LastSeen,
        Status = (int)resource.Status,
    };

    public static void CopyToEntity(TelemetryResource resource, PersistedTelemetryResource entity)
    {
        entity.ServiceName = resource.ServiceName;
        entity.ServiceInstanceId = resource.ServiceInstanceId;
        entity.TelemetrySdkLanguage = resource.TelemetrySdkLanguage;
        entity.AttributesJson = SerializeDictionary(resource.Attributes);
        entity.LastSeen = resource.LastSeen;
        entity.Status = (int)resource.Status;
    }

    public static TelemetryResource ToModel(PersistedTelemetryResource entity) => new(
        entity.Id,
        entity.ServiceName,
        entity.ServiceInstanceId,
        entity.TelemetrySdkLanguage,
        DeserializeDictionary(entity.AttributesJson),
        entity.LastSeen,
        (TelemetryResourceStatus)entity.Status);

    public static PersistedTelemetryTrace ToEntity(TelemetryTrace trace) => new()
    {
        TraceId = trace.TraceId,
        RootSpanId = trace.RootSpanId,
        Name = trace.Name,
        StartTime = trace.StartTime,
        EndTime = trace.EndTime,
        DurationTicks = trace.Duration.Ticks,
        Status = (int)trace.Status,
        ResourceIdsJson = SerializeCollection(trace.ResourceIds),
        WorkflowInstanceIdsJson = SerializeCollection(trace.WorkflowInstanceIds),
        SpanCount = trace.SpanCount,
    };

    public static TelemetryTrace ToModel(PersistedTelemetryTrace entity) => new(
        entity.TraceId,
        entity.RootSpanId,
        entity.Name,
        entity.StartTime,
        entity.EndTime,
        TimeSpan.FromTicks(entity.DurationTicks),
        (SpanStatus)entity.Status,
        DeserializeCollection<string>(entity.ResourceIdsJson),
        DeserializeCollection<string>(entity.WorkflowInstanceIdsJson),
        entity.SpanCount);

    public static PersistedTelemetrySpan ToEntity(TelemetrySpan span) => new()
    {
        SpanRecordId = span.Id,
        TraceId = span.TraceId,
        SpanId = span.SpanId,
        ParentSpanId = span.ParentSpanId,
        ResourceId = span.ResourceId,
        Name = span.Name,
        Kind = span.Kind,
        StartTime = span.StartTime,
        EndTime = span.EndTime,
        Status = (int)span.Status,
        StatusDescription = span.StatusDescription,
        AttributesJson = SerializeDictionary(span.Attributes),
        EventsJson = SerializeCollection(span.Events),
        LinksJson = SerializeCollection(span.Links),
    };

    public static TelemetrySpan ToModel(PersistedTelemetrySpan entity) => new(
        entity.SpanRecordId,
        entity.TraceId,
        entity.SpanId,
        entity.ParentSpanId,
        entity.ResourceId,
        entity.Name,
        entity.Kind,
        entity.StartTime,
        entity.EndTime,
        (SpanStatus)entity.Status,
        entity.StatusDescription,
        DeserializeDictionary(entity.AttributesJson),
        DeserializeCollection<TelemetrySpanEvent>(entity.EventsJson),
        DeserializeCollection<TelemetrySpanLink>(entity.LinksJson));

    public static PersistedMetricInstrument ToEntity(MetricInstrument instrument) => new()
    {
        Id = instrument.Id,
        ResourceId = instrument.ResourceId,
        Name = instrument.Name,
        Unit = instrument.Unit,
        Description = instrument.Description,
        Kind = (int)instrument.Kind,
        AttributesJson = SerializeDictionary(instrument.Attributes),
    };

    public static void CopyToEntity(MetricInstrument instrument, PersistedMetricInstrument entity)
    {
        entity.ResourceId = instrument.ResourceId;
        entity.Name = instrument.Name;
        entity.Unit = instrument.Unit;
        entity.Description = instrument.Description;
        entity.Kind = (int)instrument.Kind;
        entity.AttributesJson = SerializeDictionary(instrument.Attributes);
    }

    public static MetricInstrument ToModel(PersistedMetricInstrument entity) => new(
        entity.Id,
        entity.ResourceId,
        entity.Name,
        entity.Unit,
        entity.Description,
        (MetricKind)entity.Kind,
        DeserializeDictionary(entity.AttributesJson));

    public static PersistedMetricPoint ToEntity(MetricPoint point) => new()
    {
        PointId = point.Id,
        InstrumentId = point.InstrumentId,
        InstrumentName = point.InstrumentName,
        ResourceId = point.ResourceId,
        Timestamp = point.Timestamp,
        Value = point.Value,
        Sum = point.Sum,
        Count = point.Count,
        AttributesJson = SerializeDictionary(point.Attributes),
        TraceId = point.TraceId,
        SpanId = point.SpanId,
    };

    public static MetricPoint ToModel(PersistedMetricPoint entity) => new(
        entity.PointId,
        entity.InstrumentId,
        entity.InstrumentName,
        entity.ResourceId,
        entity.Timestamp,
        entity.Value,
        entity.Sum,
        entity.Count,
        DeserializeDictionary(entity.AttributesJson),
        entity.TraceId,
        entity.SpanId);

    public static PersistedOtlpLogRecord ToEntity(OtlpLogRecord log) => new()
    {
        LogRecordId = log.Id,
        ResourceId = log.ResourceId,
        Timestamp = log.Timestamp,
        SeverityText = log.SeverityText,
        SeverityNumber = log.SeverityNumber,
        Body = log.Body,
        TraceId = log.TraceId,
        SpanId = log.SpanId,
        AttributesJson = SerializeDictionary(log.Attributes),
    };

    public static OtlpLogRecord ToModel(PersistedOtlpLogRecord entity) => new(
        entity.LogRecordId,
        entity.ResourceId,
        entity.Timestamp,
        entity.SeverityText,
        entity.SeverityNumber,
        entity.Body,
        entity.TraceId,
        entity.SpanId,
        DeserializeDictionary(entity.AttributesJson));

    private static string? SerializeDictionary(IDictionary<string, string?> dictionary) =>
        dictionary.Count == 0 ? null : JsonSerializer.Serialize(dictionary, SerializerOptions);

    private static string? SerializeCollection<T>(IReadOnlyCollection<T> collection) =>
        collection.Count == 0 ? null : JsonSerializer.Serialize(collection, SerializerOptions);

    private static Dictionary<string, string?> DeserializeDictionary(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return new(StringComparer.OrdinalIgnoreCase);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json, SerializerOptions) ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyCollection<T> DeserializeCollection<T>(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
