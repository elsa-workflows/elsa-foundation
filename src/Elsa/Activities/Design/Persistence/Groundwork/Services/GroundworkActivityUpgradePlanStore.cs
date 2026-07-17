using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.Groundwork.Querying;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

public sealed class GroundworkActivityUpgradePlanStore(IDocumentStore store) :
    IActivityUpgradePlanStore,
    IActivityUpgradeApplyReceiptStore
{
    private static readonly JsonSerializerOptions JsonOptions = GroundworkActivitiesDesignJson.Options;

    public async Task<ActivityUpgradePlan?> FindAsync(string planId, CancellationToken cancellationToken = default)
    {
        var envelope = await store.LoadAsync(ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind, planId, cancellationToken);
        if (envelope is null)
            return null;
        var document = JsonSerializer.Deserialize<UpgradePlanDocument>(envelope.ContentJson, JsonOptions);
        return document?.Plan ?? throw new InvalidOperationException($"Activity upgrade plan '{planId}' is unreadable.");
    }

    public async Task SaveAsync(ActivityUpgradePlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var existing = await store.LoadAsync(ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind, plan.PlanId, cancellationToken);
        if (existing is not null)
        {
            var document = JsonSerializer.Deserialize<UpgradePlanDocument>(existing.ContentJson, JsonOptions);
            if (document?.Plan is { } existingPlan && SamePlan(existingPlan, plan))
                return;
            throw new InvalidOperationException($"Activity upgrade plan '{plan.PlanId}' is immutable.");
        }
        var save = JsonDocumentStoreExtensions.ToSaveDocumentRequest(
            ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind,
            plan.PlanId,
            ActivitiesDesignStorageManifest.SchemaVersion,
            new UpgradePlanDocument(ActivitiesDesignStorageManifest.ActivityUpgradePlanCollection, plan),
            JsonOptions);
        await store.SaveAllAsync(
            DocumentCommitScope.Of(ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind),
            [save],
            cancellationToken);
    }

    public async Task LinkSuccessorAsync(
        string planId,
        string successorPlanId,
        CancellationToken cancellationToken = default)
    {
        var existing = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind,
            planId,
            cancellationToken)
            ?? throw new InvalidOperationException($"Activity upgrade plan '{planId}' was not found.");
        var document = JsonSerializer.Deserialize<UpgradePlanDocument>(existing.ContentJson, JsonOptions)
                       ?? throw new InvalidOperationException($"Activity upgrade plan '{planId}' is unreadable.");
        if (document.Plan.SuccessorPlanId is not null)
        {
            if (StringComparer.Ordinal.Equals(document.Plan.SuccessorPlanId, successorPlanId))
                return;
            throw new InvalidOperationException($"Activity upgrade plan '{planId}' already has a successor.");
        }
        var linked = document with
        {
            Plan = document.Plan with
            {
                Status = ActivityUpgradePlanStatus.Superseded,
                SuccessorPlanId = successorPlanId
            }
        };
        var save = JsonDocumentStoreExtensions.ToSaveDocumentRequest(
            ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind,
            planId,
            ActivitiesDesignStorageManifest.SchemaVersion,
            linked,
            JsonOptions);
        await store.SaveAllAsync(
            DocumentCommitScope.Of(ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind),
            [new(save.DocumentKind, save.Id, save.SchemaVersion, save.ContentJson, existing.Version)],
            cancellationToken);
    }

    Task<ActivityUpgradeApplyReceipt?> IActivityUpgradeApplyReceiptStore.FindAsync(
        string receiptId,
        CancellationToken cancellationToken) =>
        FindReceiptAsync(receiptId, cancellationToken);

    private async Task<ActivityUpgradeApplyReceipt?> FindReceiptAsync(
        string receiptId,
        CancellationToken cancellationToken = default)
    {
        var envelope = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityUpgradeApplyReceiptDocumentKind,
            receiptId,
            cancellationToken);
        if (envelope is null)
            return null;
        var document = JsonSerializer.Deserialize<ApplyReceiptDocument>(envelope.ContentJson, JsonOptions);
        return document?.Receipt
               ?? throw new InvalidOperationException($"Activity upgrade apply receipt '{receiptId}' is unreadable.");
    }

    public Task<ActivityUpgradeApplyReceipt?> FindByIdempotencyKeyAsync(
        string planId,
        string idempotencyKeyHash,
        CancellationToken cancellationToken = default) =>
        FindReceiptAsync(ActivityUpgradeApplyIdentity.CreateReceiptId(planId, idempotencyKeyHash), cancellationToken);

    public async Task<bool> TryCreateAsync(
        ActivityUpgradeApplyReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var save = JsonDocumentStoreExtensions.ToSaveDocumentRequest(
            ActivitiesDesignStorageManifest.ActivityUpgradeApplyReceiptDocumentKind,
            receipt.ReceiptId,
            ActivitiesDesignStorageManifest.SchemaVersion,
            new ApplyReceiptDocument(ActivitiesDesignStorageManifest.ActivityUpgradeApplyReceiptCollection, receipt),
            JsonOptions);
        try
        {
            await store.SaveAllAsync(
                DocumentCommitScope.Of(ActivitiesDesignStorageManifest.ActivityUpgradeApplyReceiptDocumentKind),
                [save],
                cancellationToken);
            return true;
        }
        catch (DocumentAtomicWriteException)
        {
            return false;
        }
    }

    public async Task RejectAsync(
        ActivityUpgradeApplyReceipt receipt,
        int statusCode,
        string errorCode,
        IReadOnlyList<ActivityDiagnostic> diagnostics,
        DateTimeOffset rejectedAt,
        CancellationToken cancellationToken = default)
    {
        var existing = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityUpgradeApplyReceiptDocumentKind,
            receipt.ReceiptId,
            cancellationToken)
            ?? throw new InvalidOperationException($"Activity upgrade apply receipt '{receipt.ReceiptId}' was not found.");
        var document = JsonSerializer.Deserialize<ApplyReceiptDocument>(existing.ContentJson, JsonOptions)
                       ?? throw new InvalidOperationException($"Activity upgrade apply receipt '{receipt.ReceiptId}' is unreadable.");
        if (document.Receipt.Status != ActivityUpgradeApplyReceiptStatus.Preparing)
            return;
        var rejected = document with
        {
            Receipt = document.Receipt with
            {
                Status = ActivityUpgradeApplyReceiptStatus.Rejected,
                UpdatedAt = rejectedAt,
                Revision = document.Receipt.Revision + 1,
                Diagnostics = ActivityDiagnosticOrderer.Order(diagnostics),
                RejectionStatusCode = statusCode,
                RejectionCode = errorCode
            }
        };
        var save = JsonDocumentStoreExtensions.ToSaveDocumentRequest(
            ActivitiesDesignStorageManifest.ActivityUpgradeApplyReceiptDocumentKind,
            receipt.ReceiptId,
            ActivitiesDesignStorageManifest.SchemaVersion,
            rejected,
            JsonOptions);
        await store.SaveAllAsync(
            DocumentCommitScope.Of(ActivitiesDesignStorageManifest.ActivityUpgradeApplyReceiptDocumentKind),
            [new(save.DocumentKind, save.Id, save.SchemaVersion, save.ContentJson, existing.Version)],
            cancellationToken);
    }

    private sealed record UpgradePlanDocument(string Collection, ActivityUpgradePlan Plan);
    private sealed record ApplyReceiptDocument(string Collection, ActivityUpgradeApplyReceipt Receipt);

    private static bool SamePlan(ActivityUpgradePlan left, ActivityUpgradePlan right) =>
        JsonNode.DeepEquals(
            JsonNode.Parse(JsonSerializer.Serialize(left, JsonOptions)),
            JsonNode.Parse(JsonSerializer.Serialize(right, JsonOptions)));
}
