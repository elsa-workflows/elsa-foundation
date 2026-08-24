using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>
/// Owns the current scheduler-work row envelope, projected fields, and physical identity mapping.
/// </summary>
/// <remarks>
/// Scheduler work-item IDs are application identities and are not bounded by the portable row-key
/// limit. The injective length-prefixed composite is retained whenever it fits; otherwise a stable,
/// versioned hash alias is used. The complete logical item remains in JSON, so every read validates
/// the alias and fails closed if a physical collision is ever observed.
/// </remarks>
internal static class GroundworkV2SchedulerWorkStorageConventions
{
    private const string HashedIdentityPrefix = "elsa-runtime-v2-logical-id:v1:";

    public static GroundworkV2SchedulerWorkEnvelope NewEnvelope(RuntimeSchedulerWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateWorkflowExecutionId(item.WorkflowExecutionId);
        return new GroundworkV2SchedulerWorkEnvelope(
            ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind,
            item.WorkflowExecutionId,
            OrderKey(item),
            null,
            0,
            null,
            null,
            item);
    }

    public static StorageValues Values(GroundworkV2SchedulerWorkEnvelope envelope)
    {
        ValidateEnvelope(envelope);
        return GroundworkRuntimeRowStore.Values(
            PhysicalId(envelope.Item.WorkflowExecutionId, envelope.Item.WorkItemId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(envelope),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = envelope.Item.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField] = envelope.OrderKey,
                [ElsaRuntimeV2StorageManifest.SchedulerWorkRecordedAtField] = envelope.Item.RecordedAt,
                [ElsaRuntimeV2StorageManifest.SchedulerWorkClaimOwnerIdField] = envelope.ClaimOwnerId,
                [ElsaRuntimeV2StorageManifest.SchedulerWorkFencingTokenField] = envelope.ClaimToken
            });
    }

    public static GroundworkV2SchedulerWorkEnvelope Deserialize(
        IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork scheduler-work row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork scheduler-work row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork scheduler-work row did not contain JSON content.");

        GroundworkV2SchedulerWorkEnvelope envelope;
        try
        {
            envelope = GroundworkV2RuntimeJson.Deserialize<GroundworkV2SchedulerWorkEnvelope>(content)
                       ?? throw new InvalidDataException("Groundwork scheduler-work row content was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Groundwork scheduler-work row content was not valid current JSON.", exception);
        }

        ValidateEnvelope(envelope);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.CollectionField,
            ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField,
            envelope.Item.WorkflowExecutionId);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField,
            envelope.OrderKey);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.SchedulerWorkRecordedAtField,
            envelope.Item.RecordedAt);
        EnsureOptionalProjection(
            values,
            ElsaRuntimeV2StorageManifest.SchedulerWorkClaimOwnerIdField,
            envelope.ClaimOwnerId);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.SchedulerWorkFencingTokenField,
            envelope.ClaimToken);
        return envelope;
    }

    public static void EnsurePhysicalIdentity(
        IReadOnlyDictionary<string, object?> values,
        GroundworkV2SchedulerWorkEnvelope envelope)
    {
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.IdField,
            PhysicalId(envelope.Item.WorkflowExecutionId, envelope.Item.WorkItemId));
    }

    public static string PhysicalId(string workflowExecutionId, string workItemId)
    {
        ValidateWorkflowExecutionId(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);

        var logicalId = CompositeId(workflowExecutionId, workItemId);
        return logicalId.Length <= ElsaRuntimeV2StorageManifest.IdMaximumLength
            ? logicalId
            : HashedIdentityPrefix + StableHash(logicalId);
    }

    public static string OrderKey(RuntimeSchedulerWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateWorkflowExecutionId(item.WorkflowExecutionId);
        var key = string.Concat(
            StableHash(item.WorkflowExecutionId),
            ".",
            item.RecordedAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture),
            ".",
            (item.Sequence ?? long.MaxValue).ToString("D20", CultureInfo.InvariantCulture),
            ".",
            StableHash(item.WorkItemId));
        if (key.Length > ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyProjectionLength)
        {
            throw new InvalidOperationException(
                $"Groundwork scheduler-work order key exceeds the admitted length of " +
                $"{ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyProjectionLength}.");
        }

        return key;
    }

    public static void EnsureLogicalIdentity(
        GroundworkV2SchedulerWorkEnvelope envelope,
        string workflowExecutionId,
        string workItemId)
    {
        if (!StringComparer.Ordinal.Equals(envelope.Item.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(envelope.Item.WorkItemId, workItemId))
        {
            throw new InvalidOperationException(
                $"Groundwork scheduler-work physical identity collision detected for work item '{workItemId}' " +
                $"in workflow execution '{workflowExecutionId}'.");
        }
    }

    private static void ValidateEnvelope(GroundworkV2SchedulerWorkEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!StringComparer.Ordinal.Equals(
                envelope.Collection,
                ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind))
        {
            throw new InvalidDataException("Groundwork scheduler-work row collection does not match its current unit.");
        }

        ValidateWorkflowExecutionId(envelope.Item.WorkflowExecutionId);
        if (!StringComparer.Ordinal.Equals(envelope.WorkflowExecutionId, envelope.Item.WorkflowExecutionId))
        {
            throw new InvalidDataException(
                "Groundwork scheduler-work envelope workflow identity does not match its work item.");
        }

        if (!StringComparer.Ordinal.Equals(envelope.OrderKey, OrderKey(envelope.Item)))
            throw new InvalidDataException("Groundwork scheduler-work order projection does not match its current state.");
        if (envelope.ClaimToken < 0)
            throw new InvalidDataException("Groundwork scheduler-work claim token cannot be negative.");
        if (envelope.ClaimToken == 0 &&
            (envelope.ClaimOwnerId is not null || envelope.ClaimedAt is not null || envelope.VisibleAfter is not null))
        {
            throw new InvalidDataException("Groundwork scheduler-work initial claim state is inconsistent.");
        }
        if (envelope.ClaimToken > 0 && envelope.ClaimOwnerId is null &&
            (envelope.ClaimedAt is not null || envelope.VisibleAfter is null))
        {
            throw new InvalidDataException("Groundwork scheduler-work released claim state is inconsistent.");
        }
        if (envelope.ClaimToken > 0 && envelope.ClaimOwnerId is not null &&
            (envelope.ClaimedAt is null || envelope.VisibleAfter is null))
        {
            throw new InvalidDataException("Groundwork scheduler-work claim state is incomplete.");
        }
        if (envelope.ClaimedAt is { } claimedAt && envelope.VisibleAfter is { } visibleAfter && visibleAfter <= claimedAt)
            throw new InvalidDataException("Groundwork scheduler-work visibility deadline must follow its claim time.");
    }

    private static void ValidateWorkflowExecutionId(string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (workflowExecutionId.Length > ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workflowExecutionId),
                workflowExecutionId,
                $"Groundwork scheduler-work workflow execution IDs cannot exceed " +
                $"{ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength} characters.");
        }
    }

    private static string CompositeId(string first, string second) => string.Concat(
        first.Length.ToString(CultureInfo.InvariantCulture),
        ":",
        first,
        second.Length.ToString(CultureInfo.InvariantCulture),
        ":",
        second);

    private static string StableHash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string expected)
    {
        var actual = RequiredString(values, field);
        if (!StringComparer.Ordinal.Equals(actual, expected))
            throw new InvalidDataException($"Groundwork scheduler-work row projection '{field}' does not match its current content.");
    }

    private static void EnsureOptionalProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string? expected)
    {
        var actual = OptionalString(values, field);
        if (!StringComparer.Ordinal.Equals(actual, expected))
            throw new InvalidDataException($"Groundwork scheduler-work row projection '{field}' does not match its current content.");
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        DateTimeOffset expected)
    {
        var actual = RequiredDateTime(values, field);
        if (actual != expected)
            throw new InvalidDataException($"Groundwork scheduler-work row projection '{field}' does not match its current content.");
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        long expected)
    {
        var actual = RequiredInt64(values, field);
        if (actual != expected)
            throw new InvalidDataException($"Groundwork scheduler-work row projection '{field}' does not match its current content.");
    }

    private static string RequiredString(IReadOnlyDictionary<string, object?> values, string field) =>
        OptionalString(values, field)
        ?? throw new InvalidDataException($"Groundwork scheduler-work row is missing required string field '{field}'.");

    private static string? OptionalString(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var value)
            ? value switch
            {
                string text when !string.IsNullOrWhiteSpace(text) => text,
                JsonElement { ValueKind: JsonValueKind.String } element when !string.IsNullOrWhiteSpace(element.GetString()) => element.GetString(),
                _ => null
            }
            : null;

    private static DateTimeOffset RequiredDateTime(IReadOnlyDictionary<string, object?> values, string field)
    {
        if (values.TryGetValue(field, out var value))
        {
            if (value is DateTimeOffset dateTimeOffset)
                return dateTimeOffset;
            if (value is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return parsed;
            if (value is JsonElement { ValueKind: JsonValueKind.String } element &&
                DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
            {
                return parsed;
            }
        }

        throw new InvalidDataException($"Groundwork scheduler-work row is missing required timestamp field '{field}'.");
    }

    private static long RequiredInt64(IReadOnlyDictionary<string, object?> values, string field)
    {
        if (values.TryGetValue(field, out var value))
        {
            if (value is long longValue)
                return longValue;
            if (value is int intValue)
                return intValue;
            if (value is JsonElement element && element.TryGetInt64(out var jsonValue))
                return jsonValue;
        }

        throw new InvalidDataException($"Groundwork scheduler-work row is missing required integer field '{field}'.");
    }
}

internal sealed record GroundworkV2SchedulerWorkEnvelope(
    string Collection,
    string WorkflowExecutionId,
    string OrderKey,
    string? ClaimOwnerId,
    long ClaimToken,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? VisibleAfter,
    RuntimeSchedulerWorkItem Item);
