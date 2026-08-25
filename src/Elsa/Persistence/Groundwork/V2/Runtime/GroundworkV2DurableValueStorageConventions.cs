using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current durable-value row envelope and projected identity fields.</summary>
internal static class GroundworkV2DurableValueStorageConventions
{
    public static StorageValues Values(DurableValueState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateIdentity(state.WorkflowExecutionId, state.DurableValueId);
        return GroundworkRuntimeRowStore.Values(
            GroundworkV2CompositeIdentityCodec.From(state.WorkflowExecutionId, state.DurableValueId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(state),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.DurableValueIdField] = state.DurableValueId
            });
    }

    public static DurableValueState Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork durable-value row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork durable-value row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork durable-value row did not contain JSON content.");

        DurableValueState state;
        try
        {
            state = GroundworkV2RuntimeJson.Deserialize<DurableValueState>(content)
                    ?? throw new InvalidDataException("Groundwork durable-value row content was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Groundwork durable-value row content was not valid current JSON.", exception);
        }

        ValidateIdentity(state.WorkflowExecutionId, state.DurableValueId);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.IdField,
            GroundworkV2CompositeIdentityCodec.From(state.WorkflowExecutionId, state.DurableValueId));
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, state.WorkflowExecutionId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.DurableValueIdField, state.DurableValueId);
        return state;
    }

    public static string PhysicalId(string workflowExecutionId, string durableValueId) =>
        GroundworkV2CompositeIdentityCodec.From(workflowExecutionId, durableValueId);

    private static void ValidateIdentity(string workflowExecutionId, string durableValueId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableValueId);
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string expected)
    {
        var actual = RequiredString(values, field);
        if (!StringComparer.Ordinal.Equals(actual, expected))
            throw new InvalidDataException($"Groundwork durable-value row projection '{field}' does not match its current content.");
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

        throw new InvalidDataException($"Groundwork durable-value row is missing required string field '{field}'.");
    }

}
