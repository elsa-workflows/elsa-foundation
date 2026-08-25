using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using System.Text.Json;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkActivityUpgradePlanStoreTests
{
    [Fact]
    public async Task Upgrade_documents_preserve_the_applied_entity_identity_projection_and_wire_shape()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var store = new GroundworkActivityUpgradePlanStore(harness.Store);
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var plan = new ActivityUpgradePlan(
            "plan-1", now, now.AddHours(1), ActivityUpgradePlanStatus.Ready, [], [], [], [], TenantId: "tenant-a");
        var receipt = new ActivityUpgradeApplyReceipt(
            "receipt-1", plan.PlanId, "stage-1", "idempotency-key-hash", "request-fingerprint", "tenant-a",
            "access-profile-fingerprint", ActivityUpgradeApplyReceiptStatus.Preparing, now, now, 1,
            LeaseExpiresAt: now.AddMinutes(5));

        await store.SaveAsync(plan);
        Assert.True(await store.TryCreateAsync(receipt));

        using var planJson = JsonDocument.Parse((await harness.Store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind, plan.PlanId))!.ContentJson);
        using var receiptJson = JsonDocument.Parse((await harness.Store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityUpgradeApplyReceiptDocumentKind, receipt.ReceiptId))!.ContentJson);
        Assert.Equal(plan.PlanId, planJson.RootElement.GetProperty("entity").GetProperty("id").GetString());
        Assert.Equal(plan.PlanId, planJson.RootElement.GetProperty("plan").GetProperty("planId").GetString());
        Assert.Equal(receipt.ReceiptId, receiptJson.RootElement.GetProperty("entity").GetProperty("id").GetString());
        Assert.Equal(receipt.ReceiptId, receiptJson.RootElement.GetProperty("receipt").GetProperty("receiptId").GetString());

        var persistedPlan = await store.FindAsync(plan.PlanId);
        var persistedReceipt = await ((IActivityUpgradeApplyReceiptStore)store).FindAsync(receipt.ReceiptId);
        Assert.Equal(plan.PlanId, persistedPlan!.PlanId);
        Assert.Equal(plan.Status, persistedPlan.Status);
        Assert.Equal(plan.CreatedAt, persistedPlan.CreatedAt);
        Assert.Equal(plan.ExpiresAt, persistedPlan.ExpiresAt);
        Assert.Empty(persistedPlan.Replacements);
        Assert.Empty(persistedPlan.ExpectedSnapshots);
        Assert.Empty(persistedPlan.Steps);
        Assert.Empty(persistedPlan.Diagnostics);
        Assert.Equal(receipt.ReceiptId, persistedReceipt!.ReceiptId);
        Assert.Equal(receipt.PlanId, persistedReceipt.PlanId);
        Assert.Equal(receipt.Status, persistedReceipt.Status);
        Assert.Equal(receipt.Revision, persistedReceipt.Revision);

        var duplicate = receipt with
        {
            Status = ActivityUpgradeApplyReceiptStatus.Rejected,
            Revision = receipt.Revision + 1,
            UpdatedAt = now.AddMinutes(1)
        };
        Assert.False(await store.TryCreateAsync(duplicate));
        var authoritative = await ((IActivityUpgradeApplyReceiptStore)store).FindAsync(receipt.ReceiptId);
        Assert.Equal(receipt.Status, authoritative!.Status);
        Assert.Equal(receipt.Revision, authoritative.Revision);
    }
}
