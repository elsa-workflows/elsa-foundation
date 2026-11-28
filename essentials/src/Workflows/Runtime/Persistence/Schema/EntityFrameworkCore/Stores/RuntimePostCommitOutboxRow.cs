using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>The checked projection shared by Runtime outbox writes, reads and schema rewrites.</summary>
internal static class RuntimePostCommitOutboxRow
{
    internal static RuntimePostCommitOutboxEntity ToEntity(
        RuntimePostCommitOutboxItem item,
        string scope,
        string id,
        long revision,
        string writeVersion)
    {
        if (item.SourceScope is { } sourceScope && sourceScope.Value != scope)
            throw new InvalidOperationException("The declared recipient source does not belong to this provider write scope.");
        return new()
        {
            Id = id,
            ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope),
            ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope),
            OutboxItemId = EfRuntimeOperationalStoreSupport.Encode(item.OutboxItemId),
            OutboxItemIdHash = EfRuntimeOperationalStoreSupport.Hash(item.OutboxItemId),
            OutboxItemIdOrderKey = OutboxOrderKey(item.OutboxItemId),
            WorkflowExecutionId = item.WorkflowExecutionId is { } workflowId ? EfRuntimeOperationalStoreSupport.Encode(workflowId) : null,
            WorkflowExecutionIdHash = item.WorkflowExecutionId is { } workflowHashId ? EfRuntimeOperationalStoreSupport.Hash(workflowHashId) : null,
            WorkflowExecutionIdOrderKey = item.WorkflowExecutionId is { } workflowOrderId ? OrderKey(workflowOrderId) : null,
            SourceKind = item.SubscriptionDeliveryProtocolVersion is null ? null : (int)item.SourceKind,
            SourceId = item.SubscriptionDeliveryProtocolVersion is null ? null : EfRuntimeOperationalStoreSupport.Encode(RuntimePostCommitOutboxIdentity.CreateProjectionValue(item.OutboxItemId)),
            TriggerIdHash = item.StandaloneIngress is null ? null : EfRuntimeOperationalStoreSupport.Hash(item.StandaloneIngress.TriggerId),
            SourceKeyHash = item.StandaloneIngress is null ? null : EfRuntimeOperationalStoreSupport.Hash(item.StandaloneIngress.SourceKey),
            IntentKind = item.DeliveryKind,
            IntentKindHash = EfRuntimeOperationalStoreSupport.Hash(item.DeliveryKind),
            Status = (int)item.Status,
            RecordedAtUtcTicks = item.RecordedAt.UtcTicks,
            DeliverableAtUtcTicks = Ticks(DeliverableAt(item)),
            ClaimableAtUtcTicks = ClaimableAt(item).UtcTicks,
            ClaimableIsEligible = IsClaimable(item),
            ClaimDeliveredAtUtcTicks = Ticks(ClaimDeliveredAt(item)),
            ContentJson = RuntimeArtifactJson.Serialize(item),
            SchemaVersion = writeVersion,
            Revision = revision
        };
    }

    internal static void Copy(
        RuntimePostCommitOutboxEntity row,
        RuntimePostCommitOutboxItem item,
        string scope,
        long revision,
        string writeVersion)
    {
        var replacement = ToEntity(item, scope, row.Id, revision, writeVersion);
        row.ScopeKey = replacement.ScopeKey;
        row.ScopeKeyHash = replacement.ScopeKeyHash;
        row.OutboxItemId = replacement.OutboxItemId;
        row.OutboxItemIdHash = replacement.OutboxItemIdHash;
        row.OutboxItemIdOrderKey = replacement.OutboxItemIdOrderKey;
        row.WorkflowExecutionId = replacement.WorkflowExecutionId;
        row.WorkflowExecutionIdHash = replacement.WorkflowExecutionIdHash;
        row.WorkflowExecutionIdOrderKey = replacement.WorkflowExecutionIdOrderKey;
        row.SourceKind = replacement.SourceKind;
        row.SourceId = replacement.SourceId;
        row.TriggerIdHash = replacement.TriggerIdHash;
        row.SourceKeyHash = replacement.SourceKeyHash;
        row.IntentKind = replacement.IntentKind;
        row.IntentKindHash = replacement.IntentKindHash;
        row.Status = replacement.Status;
        row.RecordedAtUtcTicks = replacement.RecordedAtUtcTicks;
        row.DeliverableAtUtcTicks = replacement.DeliverableAtUtcTicks;
        row.ClaimableAtUtcTicks = replacement.ClaimableAtUtcTicks;
        row.ClaimableIsEligible = replacement.ClaimableIsEligible;
        row.ClaimDeliveredAtUtcTicks = replacement.ClaimDeliveredAtUtcTicks;
        row.ContentJson = replacement.ContentJson;
        row.SchemaVersion = replacement.SchemaVersion;
        row.Revision = revision;
    }

    internal static RuntimePostCommitOutboxItem ReadChecked(
        RuntimePostCommitOutboxEntity row,
        string scope,
        string? expectedOutboxItemId = null)
    {
        if (EfSchemaVersion.NotReadable(RuntimePostCommitOutboxEfModule.Chain, row.SchemaVersion) ||
            row.Revision <= 0 ||
            row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) ||
            row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope))
        {
            throw new InvalidDataException("The post-commit outbox row scope, schema, or revision projection is corrupt.");
        }

        string outboxItemId;
        try
        {
            outboxItemId = EfRuntimeOperationalStoreSupport.Decode(row.OutboxItemId);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("The post-commit outbox logical identity projection is invalid.", exception);
        }

        if (expectedOutboxItemId is not null && !StringComparer.Ordinal.Equals(outboxItemId, expectedOutboxItemId))
        {
            throw new InvalidOperationException(
                $"Post-commit outbox physical identity collision detected for '{expectedOutboxItemId}'.");
        }

        var physicalId = RuntimePostCommitOutboxIdentity.CreateProjectionValue(outboxItemId);
        if (row.Id != RowId(scope, outboxItemId) ||
            row.OutboxItemIdHash != EfRuntimeOperationalStoreSupport.Hash(outboxItemId) ||
            row.OutboxItemIdOrderKey != OutboxOrderKey(outboxItemId))
        {
            throw new InvalidDataException("The post-commit outbox logical identity projection is corrupt.");
        }

        RuntimePostCommitOutboxItem item;
        try
        {
            using var content = JsonDocument.Parse(row.ContentJson);
            foreach (var name in new[] { "savedStimulusStarts", "recipientSnapshot", "stimulusDeliveryProtocolVersion", "stimulusDeliverySourceScope", "standaloneIngress", "subscriptionDeliveryProtocolVersion", "subscriptionDelivery" })
            {
                var fields = content.RootElement.EnumerateObject()
                    .Where(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (fields.Length > 1 || fields.Any(property => property.Value.ValueKind == JsonValueKind.Null))
                    throw new InvalidDataException("Present recipient protocol state cannot be null or duplicated.");
            }
            item = RuntimeArtifactJson.Deserialize<RuntimePostCommitOutboxItem>(RuntimePostCommitOutboxEfModule.Chain.Upcast<RuntimePostCommitOutboxEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted post-commit outbox item is not valid current data.", exception);
        }

        if (item.SourceScope is { } sourceScope && sourceScope.Value != scope)
            throw new InvalidDataException("The recipient protocol scope differs from its physical source row.");

        if (!StringComparer.Ordinal.Equals(item.OutboxItemId, outboxItemId) ||
            row.Id != EfRuntimeOperationalStoreSupport.CompositeId(scope, physicalId) ||
            row.WorkflowExecutionId != (item.WorkflowExecutionId is { } workflowId ? EfRuntimeOperationalStoreSupport.Encode(workflowId) : null) ||
            row.WorkflowExecutionIdHash != (item.WorkflowExecutionId is { } workflowHashId ? EfRuntimeOperationalStoreSupport.Hash(workflowHashId) : null) ||
            row.WorkflowExecutionIdOrderKey != (item.WorkflowExecutionId is { } workflowOrderId ? OrderKey(workflowOrderId) : null) ||
            row.SourceKind != (item.SubscriptionDeliveryProtocolVersion is null ? null : (int?)item.SourceKind) ||
            row.SourceId != (item.SubscriptionDeliveryProtocolVersion is null ? null : EfRuntimeOperationalStoreSupport.Encode(RuntimePostCommitOutboxIdentity.CreateProjectionValue(item.OutboxItemId))) ||
            row.TriggerIdHash != (item.StandaloneIngress is null ? null : EfRuntimeOperationalStoreSupport.Hash(item.StandaloneIngress.TriggerId)) ||
            row.SourceKeyHash != (item.StandaloneIngress is null ? null : EfRuntimeOperationalStoreSupport.Hash(item.StandaloneIngress.SourceKey)) ||
            row.IntentKind != item.DeliveryKind ||
            row.IntentKindHash != EfRuntimeOperationalStoreSupport.Hash(item.DeliveryKind) ||
            row.Status != (int)item.Status ||
            row.RecordedAtUtcTicks != item.RecordedAt.UtcTicks ||
            row.DeliverableAtUtcTicks != Ticks(DeliverableAt(item)) ||
            row.ClaimableAtUtcTicks != ClaimableAt(item).UtcTicks ||
            row.ClaimableIsEligible != IsClaimable(item) ||
            // Null on a row written before the projection existed; the listing then simply does not return it.
            row.ClaimDeliveredAtUtcTicks is { } claimDeliveredAt && claimDeliveredAt != Ticks(ClaimDeliveredAt(item)))
        {
            throw new InvalidDataException("The post-commit outbox identity or delivery projection does not match its content.");
        }

        return item;
    }

    private static DateTimeOffset? DeliverableAt(RuntimePostCommitOutboxItem item) =>
        item.Status == RuntimePostCommitOutboxStatus.Pending ||
        item.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
        !item.RetryPolicy.IsExhaustedAfterAttempt(item.DeliveryAttemptCount)
            ? item.AvailableAt ?? DateTimeOffset.MinValue
            : null;

    private static DateTimeOffset ClaimableAt(RuntimePostCommitOutboxItem item) =>
        item.Status == RuntimePostCommitOutboxStatus.Delivering
            ? item.DeliveryVisibleAfter ?? DateTimeOffset.MaxValue
            : DeliverableAt(item) ?? DateTimeOffset.MaxValue;

    private static bool IsClaimable(RuntimePostCommitOutboxItem item) =>
        item.Status == RuntimePostCommitOutboxStatus.Delivering
            ? item.DeliveryVisibleAfter is not null
            : DeliverableAt(item) is not null;

    private static DateTimeOffset? ClaimDeliveredAt(RuntimePostCommitOutboxItem item) =>
        item.Status == RuntimePostCommitOutboxStatus.Delivered && item.DeliveryFencingToken > 0 ? item.DeliveredAt : null;

    private static long? Ticks(DateTimeOffset? value) => value?.UtcTicks;

    internal static string RowId(string scope, string outboxItemId) =>
        EfRuntimeOperationalStoreSupport.CompositeId(
            scope,
            RuntimePostCommitOutboxIdentity.CreateProjectionValue(outboxItemId));

    internal static string OutboxOrderKey(string value)
        => EfRelationalIdentity.CreateOrdinalTextOrderKey(value);

    private static string OrderKey(string value)
    {
        var physical = RuntimePostCommitOutboxIdentity.CreateProjectionValue(value);
        var prefix = physical[..Math.Min(physical.Length, RuntimePostCommitOutboxEfModule.PhysicalIdentityOrderPrefixMaximumLength)];
        return Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(
            prefix,
            RuntimePostCommitOutboxEfModule.PhysicalIdentityOrderPrefixMaximumLength)) +
            EfRelationalIdentity.Hash(physical);
    }
}
