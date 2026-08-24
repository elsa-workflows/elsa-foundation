using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current workflow-hold envelope, identity, and query projections.</summary>
internal static class GroundworkV2WorkflowHoldStateStorageConventions
{
    public static StorageValues Values(WorkflowHoldState state)
    {
        Validate(state);
        return GroundworkRuntimeRowStore.Values(
            state.ControlPlaneStateId,
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(state),
            Projections(state));
    }

    public static IReadOnlyDictionary<string, object?> Projections(WorkflowHoldState state)
    {
        Validate(state);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.CollectionField] =
                ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind,
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.WorkflowExecutionId
        };
    }

    public static WorkflowHoldState Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork workflow-hold row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork workflow-hold row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork workflow-hold row did not contain JSON content.");

        WorkflowHoldState state;
        try
        {
            state = GroundworkV2RuntimeJson.Deserialize<WorkflowHoldState>(content)
                    ?? throw new InvalidDataException("Groundwork workflow-hold row content was empty.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException
                                          or NotSupportedException
                                          or ArgumentException
                                          or InvalidOperationException
                                          or KeyNotFoundException
                                          or FormatException
                                          or OverflowException)
        {
            throw new InvalidDataException(
                "Groundwork workflow-hold row content was not valid current JSON.",
                exception);
        }

        Validate(state);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.IdField,
            state.ControlPlaneStateId);
        foreach (var (field, expected) in Projections(state))
            EnsureProjection(values, field, expected);
        return state;
    }

    public static void Validate(WorkflowHoldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ControlPlaneStateId);
        if (state.WorkflowExecutionId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        object? expected)
    {
        if (!values.TryGetValue(field, out var actual) || !EqualsProjected(actual, expected))
        {
            throw new InvalidDataException(
                $"Groundwork workflow-hold row projection '{field}' does not match its current content.");
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
                _ => actual
            };
        }

        return Equals(actual, expected);
    }

    private static string RequiredString(
        IReadOnlyDictionary<string, object?> values,
        string field)
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
            $"Groundwork workflow-hold row is missing required string field '{field}'.");
    }
}
