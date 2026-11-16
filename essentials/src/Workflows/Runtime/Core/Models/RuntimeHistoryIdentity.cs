using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Buffers.Binary;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Provider-neutral semantic replay identity, excluding provider-assigned positions.</summary>
public static class RuntimeHistoryIdentity
{
    private static readonly JsonSerializerOptions ExactOptions = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ExactStringWriter());
        return options;
    }
    private sealed class ExactStringWriter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(Encode(value));
        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WritePropertyName(Encode(value));
        private static string Encode(string value)
        {
            var bytes = new byte[checked(value.Length * 2)];
            for (var i = 0; i < value.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2, 2), value[i]);
            return Convert.ToBase64String(bytes);
        }
    }
    public static string CreateId(string logicalCheckpointId, string name, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalCheckpointId); ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentNullException.ThrowIfNull(subject);
        return "history:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { logicalCheckpointId, name, subject }, ExactOptions)));
    }

    /// <summary>Hashes ordered nullable query/access fields without replacing opaque UTF-16 code units.</summary>
    public static string CreateBinding(IReadOnlyList<string?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(fields, ExactOptions)));
    }

    public static string Compute(RuntimeHistoryEvent e, int? checkpointOrdinal = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            e.HistoryEventId, e.WorkflowExecutionId, e.Category, e.Name, e.OccurredAt,
            e.ActivityExecutionId, e.ExecutableNodeId, e.CheckpointId, e.IncidentId,
            CheckpointOrdinal = checkpointOrdinal ?? e.CheckpointOrdinal,
            e.SchemaVersion, e.Authority, e.BookmarkId, e.AttemptId, e.ParentActivityExecutionId,
            e.ExecutionScopeId, e.IterationId, e.Causation, e.Payload,
            Metadata = e.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray()
        }, ExactOptions);
        if (e.ActivityAncestryKnown is { } known)
            bytes = JsonSerializer.SerializeToUtf8Bytes(new { AncestryVersion = 1, Event = Convert.ToHexStringLower(SHA256.HashData(bytes)), Known = known }, ExactOptions);
        if (e.OccurredAtKnown is { } timeKnown)
            bytes = JsonSerializer.SerializeToUtf8Bytes(new { OccurrenceVersion = 1, Event = Convert.ToHexStringLower(SHA256.HashData(bytes)), Known = timeKnown }, ExactOptions);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static string Compute(RuntimeHistoryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Event = Compute(candidate.Event, candidate.CheckpointOrdinal),
            candidate.BookmarkMutation
        }, ExactOptions);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static RuntimeHistoryEvent Position(RuntimeHistoryEvent e, long sequence, string physicalCommitId, DateTimeOffset recordedAt, int? checkpointOrdinal = null)
    {
        ArgumentNullException.ThrowIfNull(e); ArgumentException.ThrowIfNullOrWhiteSpace(physicalCommitId);
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        return new(e.HistoryEventId, e.WorkflowExecutionId, sequence, e.Category, e.Name, e.OccurredAt,
            e.ActivityExecutionId, e.ExecutableNodeId, e.CheckpointId, e.IncidentId, e.Payload, e.Metadata)
        {
            CheckpointOrdinal = checkpointOrdinal ?? e.CheckpointOrdinal, OccurredAtKnown = e.OccurredAtKnown,
            SchemaVersion = e.SchemaVersion, Authority = e.Authority, BookmarkId = e.BookmarkId, AttemptId = e.AttemptId,
            ParentActivityExecutionId = e.ParentActivityExecutionId, ActivityAncestryKnown = e.ActivityAncestryKnown, ExecutionScopeId = e.ExecutionScopeId,
            IterationId = e.IterationId, Causation = e.Causation, PhysicalCommitId = physicalCommitId, RecordedAt = recordedAt
        };
    }
}
