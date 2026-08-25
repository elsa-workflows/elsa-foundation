using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current activity-execution hierarchy envelope and query projections.</summary>
internal static class GroundworkV2ActivityExecutionHierarchyStorageConventions
{
    public static string PhysicalId(string workflowExecutionId, string activityExecutionId) =>
        GroundworkV2CompositeIdentityCodec.From(workflowExecutionId, activityExecutionId);

    public static StorageValues Values(ActivityExecutionHierarchyRecord record)
    {
        Validate(record);
        return GroundworkRuntimeRowStore.Values(
            PhysicalId(record.WorkflowExecutionId, record.ActivityExecutionId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(record),
            Projections(record));
    }

    public static IReadOnlyDictionary<string, object?> Projections(
        ActivityExecutionHierarchyRecord record)
    {
        Validate(record);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = record.WorkflowExecutionId,
            [ElsaRuntimeV2StorageManifest.ExecutionScopeIdField] = record.ExecutionScopeId,
            [ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyIsScopeRootField] =
                StringComparer.Ordinal.Equals(record.ExecutionScopeId, record.ActivityExecutionId),
            [ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyExecutionSequenceField] = record.ExecutionSequence,
            [ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyActivityExecutionIdField] = record.ActivityExecutionId
        };
    }

    public static ActivityExecutionHierarchyRecord Deserialize(
        IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork activity-execution hierarchy row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException(
                    "Groundwork activity-execution hierarchy row content is not JSON.")
            }
            : throw new InvalidDataException(
                "Groundwork activity-execution hierarchy row did not contain JSON content.");

        ActivityExecutionHierarchyRecord record;
        try
        {
            record = GroundworkV2RuntimeJson.Deserialize<ActivityExecutionHierarchyRecord>(content)
                     ?? throw new InvalidDataException(
                         "Groundwork activity-execution hierarchy row content was empty.");
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
                "Groundwork activity-execution hierarchy row content was not valid current JSON.",
                exception);
        }

        try
        {
            Validate(record);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException(
                "Groundwork activity-execution hierarchy row content failed current validation.",
                exception);
        }
        foreach (var (field, expected) in Projections(record))
            EnsureProjection(values, field, expected);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.IdField,
            PhysicalId(record.WorkflowExecutionId, record.ActivityExecutionId));
        return record;
    }

    public static void Validate(ActivityExecutionHierarchyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ExecutionScopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ActivityExecutionId);
        ArgumentNullException.ThrowIfNull(record.Item);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Item.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Item.ActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Item.ExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Item.AuthoredActivityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Item.ActivityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Item.ActivityTypeVersion);
        ArgumentNullException.ThrowIfNull(record.Item.OutcomeNames);
        ArgumentNullException.ThrowIfNull(record.Item.Metadata);
        if (!StringComparer.Ordinal.Equals(record.WorkflowExecutionId, record.Item.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ActivityExecutionId, record.Item.ActivityExecutionId) ||
            record.ExecutionSequence != record.Item.ExecutionSequence)
        {
            throw new ArgumentException(
                "Hierarchy record envelope fields must match the item.",
                nameof(record));
        }

        if (!StringComparer.Ordinal.Equals(record.Item.ParentActivityExecutionId, record.ParentActivityExecutionId))
        {
            throw new ArgumentException(
                "Hierarchy record parent identity must match the item.",
                nameof(record));
        }

        _ = PhysicalId(record.WorkflowExecutionId, record.ActivityExecutionId);
        if (record.ExecutionSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(record), "Hierarchy execution sequence cannot be negative.");
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        object? expected)
    {
        if (!values.TryGetValue(field, out var actual) || !EqualsProjected(actual, expected))
        {
            throw new InvalidDataException(
                $"Groundwork activity-execution hierarchy row projection '{field}' does not match its current content.");
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
                JsonValueKind.True => true,
                JsonValueKind.False => false,
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
            $"Groundwork activity-execution hierarchy row is missing required string field '{field}'.");
    }
}
