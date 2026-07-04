using System.Text.Json;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Entities;
using Microsoft.Extensions.Logging;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Mapping;

/// <summary>
/// Converts between the transport-neutral <see cref="StructuredLogEntry"/> and its persisted row form.
/// Complex members are serialized as JSON text. Deserialization is defensive: a malformed JSON column
/// yields an empty collection / null rather than throwing, so one bad row cannot break a history query.
/// </summary>
internal static class StructuredLogEntryMapper
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static PersistedStructuredLogEntry ToEntity(StructuredLogEntry entry) => new()
    {
        Sequence = entry.Sequence,
        Timestamp = entry.Timestamp,
        Level = (int)entry.Level,
        Category = entry.Category,
        EventId = entry.EventId,
        EventName = entry.EventName,
        Message = entry.Message,
        MessageTemplate = entry.MessageTemplate,
        SourceId = entry.SourceId,
        PropertiesJson = Serialize(entry.Properties),
        ScopesJson = Serialize(entry.Scopes),
        ExceptionJson = entry.Exception is null ? null : JsonSerializer.Serialize(entry.Exception, SerializerOptions),
    };

    public static StructuredLogEntry ToModel(PersistedStructuredLogEntry row) => new()
    {
        Sequence = row.Sequence,
        Timestamp = row.Timestamp,
        Level = (LogLevel)row.Level,
        Category = row.Category,
        EventId = row.EventId,
        EventName = row.EventName,
        Message = row.Message,
        MessageTemplate = row.MessageTemplate,
        SourceId = row.SourceId,
        Properties = Deserialize<LogProperty>(row.PropertiesJson),
        Scopes = Deserialize<LogScope>(row.ScopesJson),
        Exception = DeserializeException(row.ExceptionJson),
    };

    private static string? Serialize<T>(IReadOnlyList<T> items) =>
        items.Count == 0 ? null : JsonSerializer.Serialize(items, SerializerOptions);

    private static IReadOnlyList<T> Deserialize<T>(string? json)
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

    private static LogExceptionDetails? DeserializeException(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<LogExceptionDetails>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
