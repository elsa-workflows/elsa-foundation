using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current run-health projection envelope and its query projections.</summary>
internal static class GroundworkV2WorkflowRunHealthStorageConventions
{
    public static StorageValues Values(
        string workflowExecutionId,
        string definitionId,
        WorkflowRunKind runKind,
        DateTimeOffset? startedAt,
        WorkflowExecutionStatus status,
        long incidentCount,
        long incidentBearingCount) =>
        Values(new GroundworkV2WorkflowRunHealthState(
            workflowExecutionId,
            definitionId,
            runKind,
            startedAt,
            status,
            incidentCount,
            incidentBearingCount));

    public static StorageValues Values(GroundworkV2WorkflowRunHealthState state)
    {
        Validate(state);
        return GroundworkRuntimeRowStore.Values(
            state.WorkflowExecutionId,
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(state),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthDefinitionIdField] = state.DefinitionId,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthRunKindField] = (int)state.RunKind,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthStartedAtField] = state.StartedAt,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusField] = (int)state.Status,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthIncidentCountField] = state.IncidentCount,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthIncidentBearingCountField] = state.IncidentBearingCount
            });
    }

    public static GroundworkV2WorkflowRunHealthState Deserialize(
        IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork workflow run-health row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork workflow run-health row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork workflow run-health row did not contain JSON content.");

        GroundworkV2WorkflowRunHealthState state;
        try
        {
            state = GroundworkV2RuntimeJson.Deserialize<GroundworkV2WorkflowRunHealthState>(content)
                    ?? throw new InvalidDataException("Groundwork workflow run-health row content was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Groundwork workflow run-health row content was not valid current JSON.", exception);
        }

        Validate(state);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.IdField, state.WorkflowExecutionId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.WorkflowRunHealthDefinitionIdField, state.DefinitionId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.WorkflowRunHealthRunKindField, (int)state.RunKind);
        EnsureOptionalProjection(values, ElsaRuntimeV2StorageManifest.WorkflowRunHealthStartedAtField, state.StartedAt);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusField, (int)state.Status);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.WorkflowRunHealthIncidentCountField, state.IncidentCount);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.WorkflowRunHealthIncidentBearingCountField, state.IncidentBearingCount);
        return state;
    }

    private static void Validate(GroundworkV2WorkflowRunHealthState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.DefinitionId);
        if (!Enum.IsDefined(state.RunKind) || !Enum.IsDefined(state.Status))
            throw new InvalidDataException("Groundwork workflow run-health row contains an unsupported enum projection.");
        if (state.IncidentCount < 0 || state.IncidentBearingCount is < 0 or > 1)
            throw new InvalidDataException("Groundwork workflow run-health incident counters cannot be negative.");
        if (state.IncidentBearingCount > state.IncidentCount)
            throw new InvalidDataException("Groundwork workflow run-health incident-bearing count cannot exceed incident count.");
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        object expected)
    {
        if (!values.TryGetValue(field, out var actual) || !Equivalent(actual, expected))
            throw new InvalidDataException(
                $"Groundwork workflow run-health row projection '{field}' does not match its current content.");
    }

    private static void EnsureOptionalProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        DateTimeOffset? expected)
    {
        if (!values.TryGetValue(field, out var actual))
            throw new InvalidDataException(
                $"Groundwork workflow run-health row is missing projection '{field}'.");
        if (expected is null)
        {
            if (actual is not null && actual is not JsonElement { ValueKind: JsonValueKind.Null })
                throw new InvalidDataException(
                    $"Groundwork workflow run-health row projection '{field}' does not match its current content.");
            return;
        }

        if (!Equivalent(actual, expected.Value))
            throw new InvalidDataException(
                $"Groundwork workflow run-health row projection '{field}' does not match its current content.");
    }

    private static bool Equivalent(object? actual, object expected) =>
        actual switch
        {
            JsonElement element when expected is string text =>
                element.ValueKind == JsonValueKind.String && element.GetString() == text,
            JsonElement element when expected is int number =>
                element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var actualInt) && actualInt == number,
            JsonElement element when expected is long number =>
                element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var actualLong) && actualLong == number,
            JsonElement element when expected is DateTimeOffset timestamp =>
                element.ValueKind == JsonValueKind.String && element.TryGetDateTimeOffset(out var actualTimestamp) && actualTimestamp == timestamp,
            _ when expected is DateTimeOffset timestamp => actual switch
            {
                DateTimeOffset actualTimestamp => actualTimestamp == timestamp,
                DateTime actualTimestamp => new DateTimeOffset(actualTimestamp, TimeSpan.Zero) == timestamp,
                _ => false
            },
            _ when expected is long number => actual switch
            {
                long actualLong => actualLong == number,
                int actualInt => actualInt == number,
                _ => false
            },
            _ => Equals(actual, expected)
        };

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

        throw new InvalidDataException(
            $"Groundwork workflow run-health row is missing required string field '{field}'.");
    }
}

internal sealed record GroundworkV2WorkflowRunHealthState(
    string WorkflowExecutionId,
    string DefinitionId,
    WorkflowRunKind RunKind,
    DateTimeOffset? StartedAt,
    WorkflowExecutionStatus Status,
    long IncidentCount,
    long IncidentBearingCount);
