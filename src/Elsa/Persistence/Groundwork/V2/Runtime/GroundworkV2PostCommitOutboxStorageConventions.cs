using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current Groundwork v2 post-commit outbox row envelope and projections.</summary>
/// <remarks>
/// The outbox item ID is a durable logical value. Groundwork's physical row key and the query projection use
/// the bounded identity projection, while the JSON content keeps the complete logical value for exact lookup and
/// collision detection. The checkpoint writer and the delivery store both call this type so they cannot silently
/// choose different physical identities or eligibility projections.
/// </remarks>
internal static class GroundworkV2PostCommitOutboxStorageConventions
{
    public static StorageValues Values(RuntimePostCommitOutboxItem item)
    {
        ValidateItem(item);
        return GroundworkRuntimeRowStore.Values(
            PhysicalId(item.OutboxItemId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(item),
            Projections(item));
    }

    public static RuntimePostCommitOutboxItem Deserialize(
        IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork post-commit outbox row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork post-commit outbox row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork post-commit outbox row did not contain JSON content.");

        RuntimePostCommitOutboxItem item;
        try
        {
            item = GroundworkV2RuntimeJson.Deserialize<RuntimePostCommitOutboxItem>(content)
                   ?? throw new InvalidDataException("Groundwork post-commit outbox row content was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Groundwork post-commit outbox row content was not valid current JSON.",
                exception);
        }

        ValidateItem(item);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.IdField, PhysicalId(item.OutboxItemId));
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.CollectionField, ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, item.Intent.WorkflowExecutionId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.PostCommitOutboxStatusField, (int)item.Status);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.PostCommitOutboxDeliverableAtField, DeliverableAt(item));
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.PostCommitOutboxClaimableAtField, ClaimableAt(item));
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.PostCommitOutboxRecordedAtField, item.RecordedAt);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.PostCommitOutboxItemIdField, ProjectionId(item.OutboxItemId));
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.PostCommitOutboxIntentKindField, item.Intent.Kind);
        return item;
    }

    public static string PhysicalId(string outboxItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxItemId);
        return RuntimePostCommitOutboxIdentity.CreateProjectionValue(outboxItemId);
    }

    public static string ProjectionId(string outboxItemId) => PhysicalId(outboxItemId);

    public static IReadOnlyDictionary<string, object?> Projections(RuntimePostCommitOutboxItem item)
    {
        ValidateItem(item);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = item.Intent.WorkflowExecutionId,
            [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
            [ElsaRuntimeV2StorageManifest.PostCommitOutboxStatusField] = (int)item.Status,
            [ElsaRuntimeV2StorageManifest.PostCommitOutboxDeliverableAtField] = DeliverableAt(item),
            [ElsaRuntimeV2StorageManifest.PostCommitOutboxClaimableAtField] = ClaimableAt(item),
            [ElsaRuntimeV2StorageManifest.PostCommitOutboxRecordedAtField] = item.RecordedAt,
            [ElsaRuntimeV2StorageManifest.PostCommitOutboxItemIdField] = ProjectionId(item.OutboxItemId),
            [ElsaRuntimeV2StorageManifest.PostCommitOutboxIntentKindField] = item.Intent.Kind
        };
    }

    public static DateTimeOffset? DeliverableAt(RuntimePostCommitOutboxItem item)
    {
        ValidateItem(item);
        return item.Status == RuntimePostCommitOutboxStatus.Pending ||
               item.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
               !item.RetryPolicy.IsExhaustedAfterAttempt(item.DeliveryAttemptCount)
            ? item.AvailableAt ?? DateTimeOffset.MinValue
            : null;
    }

    public static DateTimeOffset? ClaimableAt(RuntimePostCommitOutboxItem item)
    {
        ValidateItem(item);
        return item.Status == RuntimePostCommitOutboxStatus.Delivering
            ? item.DeliveryVisibleAfter
            : DeliverableAt(item);
    }

    private static void ValidateItem(RuntimePostCommitOutboxItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _ = PhysicalId(item.OutboxItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Intent.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Intent.Kind);
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string expected)
    {
        var actual = RequiredString(values, field);
        if (!StringComparer.Ordinal.Equals(actual, expected))
            throw new InvalidDataException($"Groundwork post-commit outbox row projection '{field}' does not match its current content.");
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        int expected)
    {
        var actual = values.TryGetValue(field, out var raw)
            ? raw switch
            {
                int value => value,
                long value => checked((int)value),
                JsonElement element when element.TryGetInt32(out var value) => value,
                _ => throw new InvalidDataException($"Groundwork post-commit outbox row projection '{field}' is not an integer.")
            }
            : throw new InvalidDataException($"Groundwork post-commit outbox row is missing projection '{field}'.");
        if (actual != expected)
            throw new InvalidDataException($"Groundwork post-commit outbox row projection '{field}' does not match its current content.");
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        DateTimeOffset? expected)
    {
        DateTimeOffset? actual;
        if (!values.TryGetValue(field, out var raw) || raw is null)
            actual = null;
        else if (raw is DateTimeOffset value)
            actual = value;
        else if (raw is JsonElement { ValueKind: JsonValueKind.Null })
            actual = null;
        else if (raw is JsonElement { ValueKind: JsonValueKind.String } element &&
                 DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            actual = parsed;
        else if (raw is string text &&
                 DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
            actual = parsed;
        else
            throw new InvalidDataException($"Groundwork post-commit outbox row projection '{field}' is not a timestamp.");

        // An older v2 writer omitted the nullable eligibility projections when AvailableAt was null. Treat that
        // physical omission as the same immediate-eligibility value while all new writes use MinValue explicitly;
        // this keeps replay reads deterministic without introducing a second storage path.
        if (actual is null && expected == DateTimeOffset.MinValue)
            return;
        if (actual != expected)
            throw new InvalidDataException($"Groundwork post-commit outbox row projection '{field}' does not match its current content.");
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

        throw new InvalidDataException($"Groundwork post-commit outbox row is missing required string field '{field}'.");
    }
}
