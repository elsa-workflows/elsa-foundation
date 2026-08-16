using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current runtime liveness row envelope and its projected recovery fields.</summary>
internal static class GroundworkV2RuntimeLivenessCodec
{
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    public static StorageValues Values(ExecutionLivenessState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var hasOwner = state.ExecutionLease is not null || state.Heartbeat is not null;
        var envelope = new RuntimeLivenessEnvelope(
            ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind,
            state.WorkflowExecutionId,
            hasOwner,
            state);

        return GroundworkRuntimeRowStore.Values(
            Identity(state.WorkflowExecutionId, state.OperationalStateId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            JsonSerializer.Serialize(envelope, Json),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind,
                [ElsaRuntimeV2StorageManifest.ExecutionLivenessOperationalStateIdField] = state.OperationalStateId,
                [ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField] = state.InterruptedExecution is { } interrupted
                    ? (int)interrupted.Status
                    : null,
                [ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField] = state.InterruptedExecution?.InterruptedAt,
                [ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField] = state.ExecutionLease?.OwnerId,
                [ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField] = state.ExecutionLease?.AcquiredAt,
                [ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField] = state.ExecutionLease?.ExpiresAt,
                [ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField] = state.Heartbeat?.OwnerId,
                [ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField] = state.Heartbeat?.RecordedAt,
                [ElsaRuntimeV2StorageManifest.RecoveryHasOperationalOwnerField] = hasOwner
            });
    }

    public static ExecutionLivenessState Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork runtime liveness row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork runtime liveness row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork runtime liveness row did not contain JSON content.");

        RuntimeLivenessEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<RuntimeLivenessEnvelope>(content, Json) ??
                       throw new InvalidDataException("Groundwork runtime liveness row content was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Groundwork runtime liveness row content was not a valid current envelope.", exception);
        }

        if (envelope.State is null)
            throw new InvalidDataException("Groundwork runtime liveness row content did not contain a state.");

        if (!StringComparer.Ordinal.Equals(
                envelope.Collection,
                ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind) ||
            !StringComparer.Ordinal.Equals(envelope.WorkflowExecutionId, envelope.State.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(envelope.State.WorkflowExecutionId,
                RequiredString(values, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField)) ||
            !StringComparer.Ordinal.Equals(envelope.State.OperationalStateId,
                RequiredString(values, ElsaRuntimeV2StorageManifest.ExecutionLivenessOperationalStateIdField)))
        {
            throw new InvalidDataException("Groundwork runtime liveness row envelope does not match its current state.");
        }

        var rowId = RequiredString(values, ElsaRuntimeV2StorageManifest.IdField);
        if (!StringComparer.Ordinal.Equals(rowId, Identity(envelope.State.WorkflowExecutionId, envelope.State.OperationalStateId)))
            throw new InvalidDataException("Groundwork runtime liveness row identity does not match its current state.");

        var rowCollection = RequiredString(values, ElsaRuntimeV2StorageManifest.CollectionField);
        if (!StringComparer.Ordinal.Equals(rowCollection, envelope.Collection))
            throw new InvalidDataException("Groundwork runtime liveness row collection does not match its current envelope.");

        var envelopeHasOwner = envelope.State.ExecutionLease is not null || envelope.State.Heartbeat is not null;
        if (envelope.HasOperationalOwner != envelopeHasOwner ||
            RequiredBoolean(values, ElsaRuntimeV2StorageManifest.RecoveryHasOperationalOwnerField) != envelopeHasOwner)
        {
            throw new InvalidDataException("Groundwork runtime liveness row owner projection does not match its current envelope.");
        }

        return envelope.State;
    }

    public static string Identity(string workflowExecutionId, string operationalStateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationalStateId);
        return string.Concat(
            workflowExecutionId.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            workflowExecutionId,
            operationalStateId);
    }

    private static string RequiredString(IReadOnlyDictionary<string, object?> values, string field)
    {
        if (values.TryGetValue(field, out var value))
        {
            if (value is string text && !string.IsNullOrWhiteSpace(text))
                return text;

            if (value is JsonElement { ValueKind: JsonValueKind.String } element &&
                !string.IsNullOrWhiteSpace(element.GetString()))
            {
                return element.GetString()!;
            }
        }

        throw new InvalidDataException($"Groundwork runtime liveness row is missing required string field '{field}'.");
    }

    private static bool RequiredBoolean(IReadOnlyDictionary<string, object?> values, string field)
    {
        if (values.TryGetValue(field, out var value))
        {
            if (value is bool boolean)
                return boolean;

            if (value is JsonElement { ValueKind: JsonValueKind.True })
                return true;

            if (value is JsonElement { ValueKind: JsonValueKind.False })
                return false;
        }

        throw new InvalidDataException($"Groundwork runtime liveness row is missing required boolean field '{field}'.");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record RuntimeLivenessEnvelope(
        string Collection,
        string WorkflowExecutionId,
        bool HasOperationalOwner,
        ExecutionLivenessState State);
}
