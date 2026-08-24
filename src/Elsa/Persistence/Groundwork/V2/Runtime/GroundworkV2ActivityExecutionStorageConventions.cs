using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current activity-execution row envelope and query projections.</summary>
internal static class GroundworkV2ActivityExecutionStorageConventions
{
    public static string PhysicalId(string workflowExecutionId, string activityExecutionId) =>
        GroundworkV2CompositeIdentityCodec.From(workflowExecutionId, activityExecutionId);

    public static StorageValues Values(ActivityExecutionState state)
    {
        Validate(state);
        return GroundworkRuntimeRowStore.Values(
            PhysicalId(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(state),
            Projections(state));
    }

    public static IReadOnlyDictionary<string, object?> Projections(ActivityExecutionState state)
    {
        Validate(state);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.Execution.WorkflowExecutionId,
            [ElsaRuntimeV2StorageManifest.ActivityExecutionIdField] = state.Execution.ActivityExecutionId,
            [ElsaRuntimeV2StorageManifest.ParentActivityExecutionIdField] = state.ParentActivityExecutionId,
            [ElsaRuntimeV2StorageManifest.ExecutionScopeIdField] = EffectiveExecutionScope(state),
            [ElsaRuntimeV2StorageManifest.StatusField] = state.Status.ToString()
        };
    }

    public static ActivityExecutionState Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork activity-execution row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork activity-execution row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork activity-execution row did not contain JSON content.");

        ActivityExecutionState state;
        try
        {
            state = GroundworkV2RuntimeJson.Deserialize<ActivityExecutionState>(content)
                    ?? throw new InvalidDataException("Groundwork activity-execution row content was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Groundwork activity-execution row content was not valid current JSON.",
                exception);
        }

        Validate(state);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.IdField,
            PhysicalId(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId));
        foreach (var (field, expected) in Projections(state))
            EnsureProjection(values, field, expected);
        return state;
    }

    public static void Validate(ActivityExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Execution);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.ActivityExecutionId);
        _ = PhysicalId(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId);
        state.EnsureValueFlowCompatible();
        state.EnsureSupersessionCompatible();
    }

    private static string? EffectiveExecutionScope(ActivityExecutionState state) =>
        string.IsNullOrWhiteSpace(state.ExecutionScopeId)
            ? state.Provenance.ExecutionScopeId
            : state.ExecutionScopeId;

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        object? expected)
    {
        if (!values.TryGetValue(field, out var actual) || !EqualsProjected(actual, expected))
        {
            throw new InvalidDataException(
                $"Groundwork activity-execution row projection '{field}' does not match its current content.");
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

        return Equals(actual, expected);
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

        throw new InvalidDataException(
            $"Groundwork activity-execution row is missing required string field '{field}'.");
    }
}
