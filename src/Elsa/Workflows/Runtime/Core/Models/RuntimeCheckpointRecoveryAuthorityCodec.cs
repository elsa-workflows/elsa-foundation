using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimeCheckpointRecoveryAuthorityCodec
{
    public const string DomainSeparator = "elsa.runtime.scheduler-work-authority:v1";

    private const int MaxIdentityLength = 450;
    private const int MaxMetadataEntries = 64;
    private const int MaxMetadataKeyLength = 128;
    private const int MaxMetadataValueLength = 4096;
    private const int MaxPayloadBytes = 256 * 1024;
    private const int MaxPayloadDepth = 64;
    private const int MaxCanonicalInputBytes = 512 * 1024;

    public RuntimeCheckpointRecoveryAuthority Encode(RuntimeSchedulerWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        ValidateIdentity(item.WorkItemId, nameof(item.WorkItemId));
        ValidateIdentity(item.WorkflowExecutionId, nameof(item.WorkflowExecutionId));
        ValidateIdentity(item.CommandId, nameof(item.CommandId));
        ValidateIdentity(item.EnvelopeId, nameof(item.EnvelopeId));
        ValidateIdentity(item.IdempotencyKey, nameof(item.IdempotencyKey));
        ValidateOptionalIdentity(item.ExecutionScopeId, nameof(item.ExecutionScopeId));
        if (item.Attempt is { } attempt)
        {
            ValidateIdentity(attempt.FirstAttemptActivityExecutionId, nameof(attempt.FirstAttemptActivityExecutionId));
            ValidateOptionalIdentity(attempt.PreviousAttemptActivityExecutionId, nameof(attempt.PreviousAttemptActivityExecutionId));
        }

        ValidateMetadata(item.CommandMetadata, nameof(item.CommandMetadata));
        ValidateMetadata(item.EnvelopeMetadata, nameof(item.EnvelopeMetadata));
        ValidatePayload(item.Payload);

        var canonicalDocument = WriteCanonicalDocument(item);
        var prefix = Encoding.UTF8.GetBytes(DomainSeparator + "\n");
        var inputLength = checked(prefix.Length + canonicalDocument.Length);
        if (inputLength > MaxCanonicalInputBytes)
            throw new ArgumentException($"Canonical scheduler-work authority input exceeds {MaxCanonicalInputBytes} UTF-8 bytes.", nameof(item));

        var input = new byte[inputLength];
        prefix.CopyTo(input, 0);
        canonicalDocument.CopyTo(input, prefix.Length);
        var fingerprint = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(input))}";
        return new RuntimeCheckpointRecoveryAuthority(1, "runtime.scheduler-work", item.WorkflowExecutionId, item.WorkItemId, fingerprint);
    }

    private static byte[] WriteCanonicalDocument(RuntimeSchedulerWorkItem item)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("workItemId", item.WorkItemId);
            writer.WriteString("workflowExecutionId", item.WorkflowExecutionId);
            writer.WriteString("commandId", item.CommandId);
            writer.WriteNumber("commandKind", (int)item.CommandKind);
            writer.WriteString("envelopeId", item.EnvelopeId);
            writer.WriteString("idempotencyKey", item.IdempotencyKey);
            writer.WriteNumber("enqueuedAtUtcTicks", item.EnqueuedAt.UtcTicks);
            writer.WriteNumber("recordedAtUtcTicks", item.RecordedAt.UtcTicks);
            if (item.Sequence is { } sequence) writer.WriteNumber("sequence", sequence); else writer.WriteNull("sequence");
            if (item.ExecutionScopeId is { } executionScopeId) writer.WriteString("executionScopeId", executionScopeId); else writer.WriteNull("executionScopeId");
            writer.WritePropertyName("attempt");
            WriteAttempt(writer, item.Attempt);
            writer.WritePropertyName("payload");
            WriteCanonical(writer, item.Payload, 0);
            writer.WritePropertyName("commandMetadata");
            WriteMetadata(writer, item.CommandMetadata);
            writer.WritePropertyName("envelopeMetadata");
            WriteMetadata(writer, item.EnvelopeMetadata);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void ValidatePayload(JsonElement? payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, payload, 0);
        if (stream.Length > MaxPayloadBytes)
            throw new ArgumentException($"Scheduler-work payload exceeds {MaxPayloadBytes} canonical UTF-8 bytes.", nameof(payload));
    }

    private static void WriteAttempt(Utf8JsonWriter writer, ActivityExecutionAttemptLineage? attempt)
    {
        if (attempt is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteNumber("attemptNumber", attempt.AttemptNumber);
        writer.WriteString("firstAttemptActivityExecutionId", attempt.FirstAttemptActivityExecutionId);
        if (attempt.PreviousAttemptActivityExecutionId is { } previous) writer.WriteString("previousAttemptActivityExecutionId", previous); else writer.WriteNull("previousAttemptActivityExecutionId");
        writer.WriteEndObject();
    }

    private static void WriteMetadata(Utf8JsonWriter writer, IReadOnlyDictionary<string, string> metadata)
    {
        writer.WriteStartObject();
        foreach (var (key, value) in metadata.OrderBy(x => x.Key, StringComparer.Ordinal))
            writer.WriteString(key, value);
        writer.WriteEndObject();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement? element, int depth)
    {
        if (element is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (depth > MaxPayloadDepth)
            throw new ArgumentException($"Scheduler-work payload exceeds depth {MaxPayloadDepth}.", nameof(element));

        switch (element.Value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.Value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value, depth + 1);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var child in element.Value.EnumerateArray())
                    WriteCanonical(writer, child, depth + 1);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                if (element.Value.TryGetInt64(out var integer)) writer.WriteNumberValue(integer);
                else if (element.Value.TryGetDecimal(out var number)) writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
                else writer.WriteRawValue(element.Value.GetDouble().ToString("R", CultureInfo.InvariantCulture));
                break;
            default:
                element.Value.WriteTo(writer);
                break;
        }
    }

    private static void ValidateIdentity(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxIdentityLength)
            throw new ArgumentException($"{parameterName} must contain between 1 and {MaxIdentityLength} UTF-16 code units.", parameterName);
    }

    private static void ValidateOptionalIdentity(string? value, string parameterName)
    {
        if (value is not null)
            ValidateIdentity(value, parameterName);
    }

    private static void ValidateMetadata(IReadOnlyDictionary<string, string> metadata, string parameterName)
    {
        if (metadata.Count > MaxMetadataEntries)
            throw new ArgumentException($"{parameterName} exceeds {MaxMetadataEntries} entries.", parameterName);
        foreach (var (key, value) in metadata)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > MaxMetadataKeyLength)
                throw new ArgumentException($"{parameterName} contains an invalid key.", parameterName);
            if (value.Length > MaxMetadataValueLength)
                throw new ArgumentException($"{parameterName} contains an oversized value.", parameterName);
        }
    }
}
