using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Runtime;

internal static class GroundworkV2WorkflowExecutionStorageConventions
{
    public static StorageValues Values(WorkflowExecutionState state)
    {
        Validate(state);
        return GroundworkRuntimeRowStore.Values(
            state.WorkflowExecutionId,
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(state),
            Projections(state));
    }

    public static IReadOnlyDictionary<string, object?> Projections(WorkflowExecutionState state)
    {
        Validate(state);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.CollectionField] =
                ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind,
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistorySortTicksField] =
                WorkflowExecutionStateHistory.SortTimestamp(state).UtcTicks,
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField] =
                state.WorkflowExecutionId,
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryTenantIdField] = state.TenantId,
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryAuthorityPartitionField] =
                state.Authority is { } authority
                    ? WorkflowExecutionAuthoritySnapshot.PartitionKey(
                        authority.SystemIdentity,
                        authority.RootInitiator,
                        authority.Metadata)
                    : null,
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryDefinitionIdField] =
                state.PinnedSource?.DefinitionId ?? state.PinnedExecutable.DefinitionId,
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryStatusField] = (int)state.Status,
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryRunKindField] = (int)state.RunKind,
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryCorrelationIdField] = state.CorrelationId,
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryArtifactIdField] =
                state.PinnedExecutable.ArtifactId,
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryArtifactTimestampField] =
                WorkflowExecutionStateHistory.SortTimestamp(state).ToUniversalTime()
        };
    }

    public static WorkflowExecutionState Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork workflow-execution row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork workflow-execution row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork workflow-execution row did not contain JSON content.");

        WorkflowExecutionState state;
        try
        {
            state = GroundworkV2RuntimeJson.Deserialize<WorkflowExecutionState>(content)
                    ?? throw new InvalidDataException(
                        "Groundwork workflow-execution row content deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Groundwork workflow-execution row content was not valid current JSON.",
                exception);
        }

        Validate(state);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.IdField, state.WorkflowExecutionId);
        foreach (var (field, expected) in Projections(state))
            EnsureProjection(values, field, expected);
        return state;
    }

    public static void Validate(WorkflowExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentNullException.ThrowIfNull(state.PinnedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.PinnedExecutable.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.PinnedExecutable.DefinitionId);
        if (state.TenantId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(state.TenantId);
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        object? expected)
    {
        if (!values.TryGetValue(field, out var actual) || !EqualsProjected(actual, expected))
        {
            throw new InvalidDataException(
                $"Groundwork workflow-execution row projection '{field}' does not match its current content.");
        }
    }

    private static bool EqualsProjected(object? actual, object? expected)
    {
        if (actual is JsonElement element)
        {
            actual = element.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
                _ => actual
            };
        }

        if (actual is int intValue && expected is long longValue)
            return intValue == longValue;
        if (actual is long actualLong && expected is int expectedInt)
            return actualLong == expectedInt;
        return Equals(actual, expected);
    }

    private static string RequiredString(IReadOnlyDictionary<string, object?> values, string field)
    {
        if (values.TryGetValue(field, out var value))
        {
            if (value is string text)
                return text;
            if (value is JsonElement { ValueKind: JsonValueKind.String } element)
                return element.GetString() ?? string.Empty;
        }

        throw new InvalidDataException(
            $"Groundwork workflow-execution row is missing required string field '{field}'.");
    }
}
