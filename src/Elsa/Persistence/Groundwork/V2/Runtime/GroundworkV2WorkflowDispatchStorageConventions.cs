using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current workflow-dispatch row envelope, identity, and projections.</summary>
/// <remarks>
/// Dispatch IDs are already versioned, deterministic identities owned by the runtime. The row key is
/// therefore the logical dispatch ID itself; this convention is shared by direct store operations and
/// checkpoint projection so neither path can silently address a different physical row.
/// </remarks>
internal static class GroundworkV2WorkflowDispatchStorageConventions
{
    public static string PhysicalId(string dispatchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        if (dispatchId.Length > ElsaRuntimeV2StorageManifest.IdMaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dispatchId),
                dispatchId,
                $"Groundwork workflow-dispatch identities cannot exceed {ElsaRuntimeV2StorageManifest.IdMaximumLength} characters.");
        }

        return dispatchId;
    }

    public static StorageValues Values(WorkflowDispatchRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return GroundworkRuntimeRowStore.Values(
            PhysicalId(record.DispatchId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(record),
            Projections(record));
    }

    public static WorkflowDispatchRecord Deserialize(
        IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork workflow-dispatch row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork workflow-dispatch row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork workflow-dispatch row did not contain JSON content.");

        WorkflowDispatchRecord record;
        try
        {
            record = GroundworkV2RuntimeJson.Deserialize<WorkflowDispatchRecord>(content)
                     ?? throw new InvalidDataException("Groundwork workflow-dispatch row content was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Groundwork workflow-dispatch row content was not valid current JSON.", exception);
        }

        EnsureProjection(values, ElsaRuntimeV2StorageManifest.IdField, PhysicalId(record.DispatchId));
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.CollectionField, ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.ParentWorkflowExecutionIdField, record.ParentWorkflowExecutionId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.ChildWorkflowExecutionIdField, record.ChildWorkflowExecutionId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.StatusField, record.Status.ToString());
        EnsureOptionalProjection(values, ElsaRuntimeV2StorageManifest.TestScopeIdField, record.TestScope?.ScopeId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.WorkflowDispatchCreatedAtField, record.CreatedAt);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.WorkflowDispatchIdField, record.DispatchId);
        return record;
    }

    private static IReadOnlyDictionary<string, object?> Projections(WorkflowDispatchRecord record) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
            [ElsaRuntimeV2StorageManifest.ParentWorkflowExecutionIdField] = record.ParentWorkflowExecutionId,
            [ElsaRuntimeV2StorageManifest.ChildWorkflowExecutionIdField] = record.ChildWorkflowExecutionId,
            [ElsaRuntimeV2StorageManifest.StatusField] = record.Status.ToString(),
            [ElsaRuntimeV2StorageManifest.TestScopeIdField] = record.TestScope?.ScopeId,
            [ElsaRuntimeV2StorageManifest.WorkflowDispatchCreatedAtField] = record.CreatedAt,
            [ElsaRuntimeV2StorageManifest.WorkflowDispatchIdField] = record.DispatchId
        };

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string expected)
    {
        var actual = RequiredString(values, field);
        if (!StringComparer.Ordinal.Equals(actual, expected))
        {
            throw new InvalidDataException(
                $"Groundwork workflow-dispatch row projection '{field}' does not match its current content.");
        }
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        DateTimeOffset expected)
    {
        if (!values.TryGetValue(field, out var raw) || !TryReadDateTime(raw, out var actual) || actual != expected)
        {
            throw new InvalidDataException(
                $"Groundwork workflow-dispatch row projection '{field}' does not match its current content.");
        }
    }

    private static void EnsureOptionalProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string? expected)
    {
        if (!values.TryGetValue(field, out var raw))
            throw new InvalidDataException($"Groundwork workflow-dispatch row is missing projection '{field}'.");

        if (expected is null)
        {
            if (raw is not null && raw is not JsonElement { ValueKind: JsonValueKind.Null })
            {
                throw new InvalidDataException(
                    $"Groundwork workflow-dispatch row projection '{field}' does not match its current content.");
            }

            return;
        }

        EnsureProjection(values, field, expected);
    }

    private static string RequiredString(IReadOnlyDictionary<string, object?> values, string field)
    {
        if (values.TryGetValue(field, out var raw))
        {
            if (raw is string text && !string.IsNullOrWhiteSpace(text))
                return text;
            if (raw is JsonElement { ValueKind: JsonValueKind.String } element &&
                !string.IsNullOrWhiteSpace(element.GetString()))
            {
                return element.GetString()!;
            }
        }

        throw new InvalidDataException(
            $"Groundwork workflow-dispatch row is missing required string field '{field}'.");
    }

    private static bool TryReadDateTime(object? raw, out DateTimeOffset value)
    {
        switch (raw)
        {
            case DateTimeOffset dateTimeOffset:
                value = dateTimeOffset;
                return true;
            case DateTime dateTime:
                value = new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
                return true;
            case string text when DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed):
                value = parsed;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } element when element.TryGetDateTimeOffset(out var jsonValue):
                value = jsonValue;
                return true;
            default:
                value = default;
                return false;
        }
    }
}
