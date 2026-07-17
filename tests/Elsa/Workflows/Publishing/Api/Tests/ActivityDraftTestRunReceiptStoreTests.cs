using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ActivityDraftTestRunReceiptStoreTests
{
    [Fact]
    public async Task Same_idempotency_identity_creates_one_receipt_and_preserves_the_original_request()
    {
        var store = new InMemoryActivityDraftTestRunStore();
        var original = Receipt("fingerprint-a");
        var conflicting = Receipt("fingerprint-b");

        var first = await store.TryCreateAsync(original);
        var second = await store.TryCreateAsync(conflicting);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal("fingerprint-a", second.Receipt.RequestFingerprint);
        Assert.Equal(original.TestRunId, (await store.FindByIdempotencyKeyAsync(
            original.OperationScope,
            "draft-1",
            "rerun-42"))!.TestRunId);
    }

    [Fact]
    public async Task Groundwork_receipt_survives_store_recreation()
    {
        var documents = new InMemoryDocumentStore(PublishingGroundworkStorageManifest.Create());
        var serializer = new PublishingGroundworkDocumentSerializer();
        var firstStore = new GroundworkActivityDraftTestRunStore(
            documents,
            serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor,
            documents);
        var now = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        var receipt = Receipt("fingerprint-a", null, null) with
        {
            RequestedAt = now,
            UpdatedAt = now,
            SourceReferenceExpiresAt = now.AddMinutes(30),
            ReceiptExpiresAt = now.AddDays(7)
        };

        Assert.True((await firstStore.TryCreateAsync(receipt)).Created);

        var reopenedStore = new GroundworkActivityDraftTestRunStore(
            documents,
            serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor,
            documents);
        Assert.Equal(receipt, await reopenedStore.FindAsync(receipt.TestRunId));
        var envelope = await documents.LoadAsync(
            PublishingGroundworkStorageManifest.ActivityDraftTestRunDocumentKind,
            receipt.TestRunId);
        Assert.DoesNotContain("rerun-42", Assert.IsType<string>(envelope?.ContentJson), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Receipt_cleanup_uses_receipt_retention_instead_of_source_reference_expiry()
    {
        var store = new InMemoryActivityDraftTestRunStore();
        var receipt = Receipt("fingerprint-a");
        await store.TryCreateAsync(receipt);

        Assert.Equal(0, await store.DeleteExpiredAsync(receipt.SourceReferenceExpiresAt, 10));
        Assert.Equal(1, await store.DeleteExpiredAsync(receipt.ReceiptExpiresAt, 10));
        Assert.Null(await store.FindAsync(receipt.TestRunId));
    }

    [Fact]
    public void Same_tenant_local_draft_and_key_have_distinct_tenant_and_global_identities()
    {
        var tenantA = ActivityDraftTestRunIdentity.CreateOperationScope("tenant-a");
        var tenantB = ActivityDraftTestRunIdentity.CreateOperationScope("tenant-b");
        var tenantNamedGlobal = ActivityDraftTestRunIdentity.CreateOperationScope("global");
        var global = ActivityDraftTestRunIdentity.CreateOperationScope(null);

        Assert.Equal(ActivityDraftTestRunIdentity.GlobalOperationScope, global);
        Assert.NotEqual(global, tenantNamedGlobal);
        Assert.Equal(4, new[]
        {
            ActivityDraftTestRunIdentity.CreateTestRunId(tenantA, "draft-1", "rerun-42"),
            ActivityDraftTestRunIdentity.CreateTestRunId(tenantB, "draft-1", "rerun-42"),
            ActivityDraftTestRunIdentity.CreateTestRunId(tenantNamedGlobal, "draft-1", "rerun-42"),
            ActivityDraftTestRunIdentity.CreateTestRunId(global, "draft-1", "rerun-42")
        }.Distinct(StringComparer.Ordinal).Count());
        Assert.NotEqual(
            ActivityDraftTestRunIdentity.CreateWorkflowExecutionId(tenantA, "draft-1", "rerun-42"),
            ActivityDraftTestRunIdentity.CreateWorkflowExecutionId(tenantB, "draft-1", "rerun-42"));
    }

    private static ActivityDraftTestRunReceipt Receipt(
        string fingerprint,
        string? tenantId = "tenant-a",
        string? resourceTenantId = "tenant-a") => new(
        TestRunId: ActivityDraftTestRunIdentity.CreateTestRunId(
            ActivityDraftTestRunIdentity.CreateOperationScope(tenantId),
            "draft-1",
            "rerun-42"),
        OperationScope: ActivityDraftTestRunIdentity.CreateOperationScope(tenantId),
        IdempotencyKeyHash: ActivityDraftTestRunIdentity.HashIdempotencyKey("rerun-42"),
        DraftId: "draft-1",
        DraftRevision: 8,
        DefinitionId: "definition-1",
        TenantId: tenantId,
        ResourceTenantId: resourceTenantId,
        RequestFingerprint: fingerprint,
        WorkflowExecutionId: ActivityDraftTestRunIdentity.CreateWorkflowExecutionId(
            ActivityDraftTestRunIdentity.CreateOperationScope(tenantId),
            "draft-1",
            "rerun-42"),
        Status: ActivityDraftTestRunReceiptStatus.Preparing,
        CommandDispatchStatus: null,
        Failure: null,
        ArtifactId: null,
        SourceReferenceId: null,
        RequestedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch,
        SourceReferenceExpiresAt: DateTimeOffset.UnixEpoch.AddMinutes(30),
        ReceiptExpiresAt: DateTimeOffset.UnixEpoch.AddDays(7),
        CancellationStatus: ActivityDraftTestRunCancellationStatus.Available,
        Revision: 1);
}
