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

        EnsureOptionalInt32(
            values,
            ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField,
            envelope.State.InterruptedExecution is { } interrupted ? (int)interrupted.Status : null);
        EnsureOptionalTimestamp(
            values,
            ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField,
            envelope.State.InterruptedExecution?.InterruptedAt);
        EnsureOptionalString(
            values,
            ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField,
            envelope.State.ExecutionLease?.OwnerId);
        EnsureOptionalTimestamp(
            values,
            ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField,
            envelope.State.ExecutionLease?.AcquiredAt);
        EnsureOptionalTimestamp(
            values,
            ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField,
            envelope.State.ExecutionLease?.ExpiresAt);
        EnsureOptionalString(
            values,
            ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField,
            envelope.State.Heartbeat?.OwnerId);
        EnsureOptionalTimestamp(
            values,
            ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField,
            envelope.State.Heartbeat?.RecordedAt);

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

    private static void EnsureOptionalString(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string? expected)
    {
        var actual = OptionalString(values, field);
        if (!StringComparer.Ordinal.Equals(actual, expected))
        {
            throw new InvalidDataException(
                $"Groundwork runtime liveness row projection '{field}' does not match its current envelope.");
        }
    }

    private static void EnsureOptionalInt32(
        IReadOnlyDictionary<string, object?> values,
        string field,
        int? expected)
    {
        var actual = OptionalInt32(values, field);
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"Groundwork runtime liveness row projection '{field}' does not match its current envelope.");
        }
    }

    private static void EnsureOptionalTimestamp(
        IReadOnlyDictionary<string, object?> values,
        string field,
        DateTimeOffset? expected)
    {
        var actual = OptionalTimestamp(values, field);
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"Groundwork runtime liveness row projection '{field}' does not match its current envelope.");
        }
    }

    private static string? OptionalString(IReadOnlyDictionary<string, object?> values, string field)
    {
        var value = ProjectionValue(values, field);
        if (IsNull(value))
            return null;

        if (value is string text && !string.IsNullOrWhiteSpace(text))
            return text;

        if (value is JsonElement { ValueKind: JsonValueKind.String } element &&
            !string.IsNullOrWhiteSpace(element.GetString()))
        {
            return element.GetString();
        }

        throw InvalidProjection(field, "string");
    }

    private static int? OptionalInt32(IReadOnlyDictionary<string, object?> values, string field)
    {
        var value = ProjectionValue(values, field);
        if (IsNull(value))
            return null;

        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var jsonInt))
                return jsonInt;

            throw InvalidProjection(field, "Int32");
        }

        try
        {
            return value switch
            {
                sbyte number => number,
                byte number => number,
                short number => number,
                ushort number => number,
                int number => number,
                uint number => checked((int)number),
                long number => checked((int)number),
                ulong number => checked((int)number),
                _ => throw InvalidProjection(field, "Int32")
            };
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                $"Groundwork runtime liveness row projection '{field}' is outside the Int32 range.", exception);
        }
    }

    private static DateTimeOffset? OptionalTimestamp(IReadOnlyDictionary<string, object?> values, string field)
    {
        var value = ProjectionValue(values, field);
        if (IsNull(value))
            return null;

        if (value is DateTimeOffset dateTimeOffset)
            return dateTimeOffset;

        if (value is DateTime dateTime)
        {
            var normalized = dateTime.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                : dateTime;
            return new DateTimeOffset(normalized);
        }

        if (value is JsonElement { ValueKind: JsonValueKind.String } element &&
            DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var jsonTimestamp))
        {
            return jsonTimestamp;
        }

        if (value is string text &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
        {
            return timestamp;
        }

        throw InvalidProjection(field, "DateTimeOffset");
    }

    private static object? ProjectionValue(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var value)
            ? value
            : throw new InvalidDataException($"Groundwork runtime liveness row is missing projection '{field}'.");

    private static bool IsNull(object? value) => value is null or DBNull;

    private static InvalidDataException InvalidProjection(string field, string expectedType) =>
        new($"Groundwork runtime liveness row projection '{field}' is not a {expectedType} value.");

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
