using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Endpoints;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Persistence.Groundwork;
using Elsa3.Activities.Design.Import.Persistence.Groundwork.Services;
using Elsa3.Activities.Design.Import.Services;
using Elsa3.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa3.Mapping.Tests;

public sealed class ReusableActivityImportOperationTests
{
    private static readonly ReusableActivityImportAccessScope Scope = new("tenant-a", "user-a");

    [Fact]
    public async Task Upload_is_bounded_immutable_scoped_cancellable_and_expiring()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-17T10:00:00Z"));
        var harness = Harness(clock, maximumBytes: 8_000);
        var payload = Json(ReusableActivityImportFixtures.Workflow(
            "a",
            "a-v1",
            1,
            true,
            ReusableActivityImportFixtures.Leaf("root")));

        var upload = await harness.Service.UploadAsync(payload, payload.Length, Scope);

        Assert.Equal(1, upload.SourceVersionCount);
        Assert.Equal(clock.GetUtcNow().AddHours(1), upload.ExpiresAt);
        var page = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);
        Assert.Single(page.Items);
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, new("tenant-b", "user-a")));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, new("tenant-a", "user-b")));

        clock.Advance(TimeSpan.FromHours(2));
        await Assert.ThrowsAsync<ReusableActivityImportExpiredException>(async () =>
            await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope));

        var oversized = new MemoryStream(new byte[8_001]);
        await Assert.ThrowsAsync<ReusableActivityImportPayloadException>(async () =>
            await harness.Service.UploadAsync(oversized, oversized.Length, Scope));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await harness.Service.UploadAsync(Json(), null, Scope, cancellation.Token));
    }

    [Fact]
    public async Task Analysis_is_deterministic_paged_side_effect_free_and_exposes_typed_paths_and_complete_cycles()
    {
        var a = ReusableActivityImportFixtures.Workflow(
            "a",
            "a-v1",
            1,
            true,
            ReusableActivityImportFixtures.Reference("a-to-b", targetVersionId: "b-v1"));
        var b = ReusableActivityImportFixtures.Workflow(
            "b",
            "b-v1",
            1,
            true,
            ReusableActivityImportFixtures.Reference("b-to-a", targetVersionId: "a-v1"));
        var missing = ReusableActivityImportFixtures.Workflow(
            "missing",
            "missing-v1",
            1,
            false,
            ReusableActivityImportFixtures.Reference("missing-ref", targetVersionId: "absent-v1"));
        var harness = Harness();
        var upload = await harness.Service.UploadAsync(Json(a, b, missing), null, Scope);
        var savesBefore = harness.Store.SaveCount;

        var first = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 2, Scope);
        var second = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 2, 2, Scope);
        var repeated = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 2, Scope);

        Assert.Equal(first.PlanId, second.PlanId);
        Assert.Equal(first.PlanId, repeated.PlanId);
        Assert.Equal(3, first.Total);
        Assert.Equal(2, first.Processed);
        Assert.Equal(2, first.NextOffset);
        Assert.True(second.IsComplete);
        Assert.Equal(savesBefore, harness.Store.SaveCount);
        var cycle = Assert.Single(first.Diagnostics.Where(x => x.Code == ReusableActivityImportDiagnosticCodes.DependencyCycle));
        Assert.Equal(["a-v1", "b-v1", "a-v1"], cycle.Cycle);
        Assert.Equal(Elsa3MigrationPathSegmentKind.SourceVersion, cycle.PathSegments[0].Kind);
        var missingDiagnostic = Assert.Single(first.Diagnostics.Where(x => x.Code == ReusableActivityImportDiagnosticCodes.MissingReference));
        Assert.Equal(
            [Elsa3MigrationPathSegmentKind.SourceVersion, Elsa3MigrationPathSegmentKind.Node, Elsa3MigrationPathSegmentKind.DependencySourceVersion],
            missingDiagnostic.PathSegments.Select(x => x.Kind));
    }

    [Fact]
    public async Task Closure_expansion_is_authoritative_and_ignores_unrelated_invalid_members()
    {
        var a = ReusableActivityImportFixtures.Workflow("a", "a-v1", 1, true, ReusableActivityImportFixtures.Leaf("a-root"));
        var b = ReusableActivityImportFixtures.Workflow("b", "b-v1", 1, true, ReusableActivityImportFixtures.Reference("b-to-a", targetVersionId: "a-v1"));
        var invalid = ReusableActivityImportFixtures.Workflow("invalid", "invalid-v1", 1, false, ReusableActivityImportFixtures.Reference("missing", targetVersionId: "absent"));
        var harness = Harness();
        var upload = await harness.Service.UploadAsync(Json(a, b, invalid), null, Scope);
        var analysis = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);

        var readiness = await harness.Service.ExpandSelectionAsync(
            upload.CollectionHandle,
            analysis.PlanId,
            ["b-v1"],
            Scope);

        Assert.True(readiness.IsReady);
        Assert.Equal(["a-v1", "b-v1"], readiness.ExpandedSourceVersionIds);
        Assert.Equal(["a-v1"], readiness.AddedDependencySourceVersionIds);
        Assert.DoesNotContain(readiness.Diagnostics, x => x.Metadata.GetValueOrDefault("SourceVersionId") == "invalid-v1");
    }

    [Fact]
    public async Task Apply_is_atomic_exact_idempotent_and_reconciles_lost_responses_with_per_source_navigation()
    {
        var a = ReusableActivityImportFixtures.Workflow("a", "a-v1", 1, true, ReusableActivityImportFixtures.Leaf("a-root"));
        var b = ReusableActivityImportFixtures.Workflow("b", "b-v1", 1, true, ReusableActivityImportFixtures.Reference("b-to-a", targetVersionId: "a-v1"));
        var harness = Harness();
        var upload = await harness.Service.UploadAsync(Json(a, b), null, Scope);
        var analysis = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);

        var applied = await harness.Service.ApplyAsync(
            upload.CollectionHandle,
            analysis.PlanId,
            ["a-v1", "b-v1"],
            "operation-1",
            Scope);
        var retried = await harness.Service.ApplyAsync(
            upload.CollectionHandle,
            analysis.PlanId,
            ["a-v1", "b-v1"],
            "operation-1",
            Scope);
        var recovered = await harness.Service.GetStatusAsync("operation-1", Scope);

        Assert.Equal(ReusableActivityImportReceiptStatus.Applied, applied.Status);
        Assert.Equal(ReusableActivityImportReceiptStatus.AlreadyImported, retried.Status);
        Assert.Equal(applied.ReceiptId, recovered.ReceiptId);
        Assert.Equal(2, applied.Sources.Count);
        Assert.Equal(["a", "b"], applied.Sources.Select(x => x.SourceDefinitionId).Order(StringComparer.Ordinal));
        Assert.Equal(["a-v1", "b-v1"], applied.Sources.Select(x => x.SourceVersionId).Order(StringComparer.Ordinal));
        Assert.All(applied.Sources, source =>
        {
            Assert.Equal(ReusableActivityImportResourceDisposition.Created, source.WorkflowDisposition);
            Assert.StartsWith("/design/workflows/definitions/", source.WorkflowNavigationIdentity, StringComparison.Ordinal);
            Assert.NotNull(source.ActivityDefinitionId);
            Assert.NotNull(source.ActivityVersionNavigationIdentity);
        });
        Assert.Single(harness.Store.Snapshot("elsa3ReusableImportReceipt"));
        Assert.Equal(2, harness.Store.Snapshot("activityDefinition").Count);
        Assert.Equal(2, harness.Store.Snapshot("workflowDefinition").Count);

        await Assert.ThrowsAsync<ReusableActivityImportIdempotencyConflictException>(async () =>
            await harness.Service.ApplyAsync(
                upload.CollectionHandle,
                analysis.PlanId,
                ["a-v1"],
                "operation-1",
                Scope));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await harness.Service.GetStatusAsync("operation-1", new("tenant-b", "user-a")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Later_valid_subset_reuses_definition_and_existing_versions_while_adding_new_versions(
        bool repeatFirstVersion)
    {
        var v1 = ReusableActivityImportFixtures.Workflow(
            "a",
            "a-v1",
            1,
            true,
            ReusableActivityImportFixtures.Leaf("root-v1"));
        var v2 = ReusableActivityImportFixtures.Workflow(
            "a",
            "a-v2",
            2,
            true,
            ReusableActivityImportFixtures.Leaf("root-v2"));
        var harness = Harness();
        var upload = await harness.Service.UploadAsync(Json(v1, v2), null, Scope);
        var analysis = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);

        var first = await harness.Service.ApplyAsync(
            upload.CollectionHandle,
            analysis.PlanId,
            ["a-v1"],
            "subset-v1",
            Scope);
        var laterSelection = repeatFirstVersion ? new[] { "a-v1", "a-v2" } : ["a-v2"];
        var later = await harness.Service.ApplyAsync(
            upload.CollectionHandle,
            analysis.PlanId,
            laterSelection,
            repeatFirstVersion ? "subset-v1-v2" : "subset-v2",
            Scope);

        Assert.Equal(ReusableActivityImportResourceDisposition.Created, Assert.Single(first.Sources).ActivityDefinitionDisposition);
        Assert.All(later.Sources, source =>
            Assert.Equal(ReusableActivityImportResourceDisposition.Reused, source.ActivityDefinitionDisposition));
        var v2Receipt = Assert.Single(later.Sources.Where(x => x.SourceVersionId == "a-v2"));
        Assert.Equal(ReusableActivityImportResourceDisposition.Created, v2Receipt.ActivityVersionDisposition);
        if (repeatFirstVersion)
        {
            var v1Receipt = Assert.Single(later.Sources.Where(x => x.SourceVersionId == "a-v1"));
            Assert.Equal(ReusableActivityImportResourceDisposition.Reused, v1Receipt.ActivityVersionDisposition);
        }

        Assert.Single(harness.Store.Snapshot("activityDefinition"));
        Assert.Equal(2, harness.Store.Snapshot("activityDefinitionVersion").Count);
        Assert.Single(harness.Store.Snapshot("activityDefinitionAuthoringState"));
        Assert.Equal(2, harness.Store.Snapshot("workflowDefinitionVersion").Count);
        Assert.Equal(2, harness.Store.Snapshot("elsa3ReusableImportReceipt").Count);
        Assert.Equal(v2Receipt.ActivityDefinitionVersionId, CurrentManagementProjection(harness.Store).HeadVersionId);
    }

    [Fact]
    public async Task Later_subset_projection_failure_rolls_back_head_version_and_receipt_atomically()
    {
        var v1 = ReusableActivityImportFixtures.Workflow(
            "atomic",
            "atomic-v1",
            1,
            true,
            ReusableActivityImportFixtures.Leaf("root-v1"));
        var v2 = ReusableActivityImportFixtures.Workflow(
            "atomic",
            "atomic-v2",
            2,
            true,
            ReusableActivityImportFixtures.Leaf("root-v2"));
        var inner = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var store = new FailingAtomicDocumentStore(inner);
        var service = Harness(store, store, inner).Service;
        var upload = await service.UploadAsync(Json(v1, v2), null, Scope);
        var analysis = await service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);
        var first = await service.ApplyAsync(
            upload.CollectionHandle,
            analysis.PlanId,
            ["atomic-v1"],
            "atomic-first",
            Scope);
        var v1Receipt = Assert.Single(first.Sources);
        store.FailOnDocumentKind = ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind;

        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await service.ApplyAsync(
                upload.CollectionHandle,
                analysis.PlanId,
                ["atomic-v2"],
                "atomic-second",
                Scope));

        Assert.Single(inner.Snapshot("activityDefinitionVersion"));
        Assert.Single(inner.Snapshot("workflowDefinitionVersion"));
        Assert.Single(inner.Snapshot("elsa3ReusableImportReceipt"));
        Assert.Equal(v1Receipt.ActivityDefinitionVersionId, CurrentAuthoring(inner).HeadVersionId);
        Assert.Equal(v1Receipt.ActivityDefinitionVersionId, CurrentManagementProjection(inner).HeadVersionId);
    }

    [Fact]
    public async Task Unrelated_activity_definition_with_matching_shell_but_no_import_binding_fails_before_writes()
    {
        var source = ReusableActivityImportFixtures.Workflow(
            "activity-owner",
            "activity-owner-v1",
            1,
            true,
            ReusableActivityImportFixtures.Leaf("root"));
        var harness = Harness();
        var upload = await harness.Service.UploadAsync(Json(source), null, Scope);
        var analysis = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);
        var item = Assert.Single(analysis.Items);
        await SaveActivityDefinitionAsync(harness.Store, new()
        {
            Id = item.ActivityDefinitionId!,
            TenantId = Scope.TenantId,
            ActivityTypeKey = item.ActivityTypeKey!,
            Category = "Elsa 3 reusable workflows",
            DisplayName = source.Name,
            Description = source.Description,
            CreatedAt = source.CreatedAt,
            LastModifiedAt = source.CreatedAt
        });

        await Assert.ThrowsAsync<ReusableActivityImportCollisionException>(async () =>
            await harness.Service.ApplyAsync(
                upload.CollectionHandle,
                analysis.PlanId,
                [source.Id],
                "unrelated-activity",
                Scope));

        Assert.Empty(harness.Store.Snapshot(Elsa3ImportStorageManifest.DefinitionBindingDocumentKind));
        Assert.Empty(harness.Store.Snapshot("activityDefinitionVersion"));
        Assert.Empty(harness.Store.Snapshot("activityDefinitionAuthoringState"));
        Assert.Empty(harness.Store.Snapshot("workflowDefinition"));
        Assert.Empty(harness.Store.Snapshot("workflowDefinitionVersion"));
        Assert.Empty(harness.Store.Snapshot("elsa3ReusableImportReceipt"));
    }

    [Fact]
    public async Task Unrelated_workflow_definition_with_identical_presentation_but_no_import_binding_fails_before_writes()
    {
        var source = ReusableActivityImportFixtures.Workflow(
            "workflow-owner",
            "workflow-owner-v1",
            1,
            false,
            ReusableActivityImportFixtures.Leaf("root"));
        var harness = Harness();
        var upload = await harness.Service.UploadAsync(Json(source), null, Scope);
        var analysis = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);
        var item = Assert.Single(analysis.Items);
        await SaveWorkflowDefinitionAsync(harness.Store, new()
        {
            Id = item.WorkflowDefinitionId,
            TenantId = Scope.TenantId,
            Name = source.Name,
            Description = source.Description,
            CreatedAt = source.CreatedAt,
            LastModifiedAt = source.CreatedAt
        });

        await Assert.ThrowsAsync<ReusableActivityImportCollisionException>(async () =>
            await harness.Service.ApplyAsync(
                upload.CollectionHandle,
                analysis.PlanId,
                [source.Id],
                "unrelated-workflow",
                Scope));

        Assert.Empty(harness.Store.Snapshot(Elsa3ImportStorageManifest.DefinitionBindingDocumentKind));
        Assert.Empty(harness.Store.Snapshot("workflowDefinitionVersion"));
        Assert.Empty(harness.Store.Snapshot("activityDefinition"));
        Assert.Empty(harness.Store.Snapshot("activityDefinitionVersion"));
        Assert.Empty(harness.Store.Snapshot("elsa3ReusableImportReceipt"));
    }

    [Fact]
    public async Task Same_export_imported_by_different_tenants_is_isolated_by_ambient_persistence_scope()
    {
        var source = ReusableActivityImportFixtures.Workflow(
            "shared",
            "shared-v1",
            1,
            true,
            ReusableActivityImportFixtures.Leaf("root"));
        var tenantA = new ReusableActivityImportAccessScope("tenant-a", "user-a");
        var tenantB = new ReusableActivityImportAccessScope("tenant-b", "user-b");
        var a = Harness(scope: tenantA);
        var b = Harness(scope: tenantB);

        var uploadA = await a.Service.UploadAsync(Json(source), null, tenantA);
        var uploadB = await b.Service.UploadAsync(Json(source), null, tenantB);
        var analysisA = await a.Service.AnalyzeAsync(uploadA.CollectionHandle, 0, 10, tenantA);
        var analysisB = await b.Service.AnalyzeAsync(uploadB.CollectionHandle, 0, 10, tenantB);
        var receiptA = await a.Service.ApplyAsync(uploadA.CollectionHandle, analysisA.PlanId, ["shared-v1"], "same-key", tenantA);
        var receiptB = await b.Service.ApplyAsync(uploadB.CollectionHandle, analysisB.PlanId, ["shared-v1"], "same-key", tenantB);

        Assert.NotEqual(receiptA.ReceiptId, receiptB.ReceiptId);
        Assert.Equal(receiptA.Sources[0].ActivityDefinitionId, receiptB.Sources[0].ActivityDefinitionId);
        Assert.Single(a.Store.Snapshot("activityDefinition"));
        Assert.Single(b.Store.Snapshot("activityDefinition"));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await a.Service.GetStatusAsync("same-key", tenantB));
    }

    [Fact]
    public async Task Same_tenant_users_reuse_tenant_design_but_own_independent_operations_and_idempotency_keys()
    {
        var source = ReusableActivityImportFixtures.Workflow(
            "tenant-shared",
            "tenant-shared-v1",
            1,
            true,
            ReusableActivityImportFixtures.Leaf("root"));
        var userA = new ReusableActivityImportAccessScope("tenant-shared", "user-a");
        var userB = new ReusableActivityImportAccessScope("tenant-shared", "user-b");
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var harness = Harness(
            store,
            store,
            store,
            GroundworkTestAccess.AccessContext("tenant-shared"));
        var uploadA = await harness.Service.UploadAsync(Json(source), null, userA);
        var uploadB = await harness.Service.UploadAsync(Json(source), null, userB);
        var analysisA = await harness.Service.AnalyzeAsync(uploadA.CollectionHandle, 0, 10, userA);
        var analysisB = await harness.Service.AnalyzeAsync(uploadB.CollectionHandle, 0, 10, userB);

        var receiptA = await harness.Service.ApplyAsync(
            uploadA.CollectionHandle,
            analysisA.PlanId,
            [source.Id],
            "same-key",
            userA);

        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await harness.Service.GetStatusAsync("same-key", userB));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await harness.Service.AnalyzeAsync(uploadA.CollectionHandle, 0, 10, userB));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await harness.Service.AnalyzeAsync(uploadB.CollectionHandle, 0, 10, userA));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await harness.Service.ApplyAsync(
                uploadA.CollectionHandle,
                analysisA.PlanId,
                [source.Id],
                "same-key",
                userB));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await harness.Service.ApplyAsync(
                uploadB.CollectionHandle,
                analysisB.PlanId,
                [source.Id],
                "user-a-cross-replay",
                userA));

        var receiptB = await harness.Service.ApplyAsync(
            uploadB.CollectionHandle,
            analysisB.PlanId,
            [source.Id],
            "same-key",
            userB);
        var recoveredA = await harness.Service.GetStatusAsync("same-key", userA);
        var recoveredB = await harness.Service.GetStatusAsync("same-key", userB);

        Assert.Equal(ReusableActivityImportReceiptStatus.Applied, receiptA.Status);
        Assert.Equal(ReusableActivityImportReceiptStatus.Applied, receiptB.Status);
        Assert.Equal(Elsa3ImportStorageManifest.ReceiptId("same-key", userA), receiptA.ReceiptId);
        Assert.Equal(Elsa3ImportStorageManifest.ReceiptId("same-key", userB), receiptB.ReceiptId);
        Assert.NotEqual(receiptA.ReceiptId, receiptB.ReceiptId);
        Assert.Equal(receiptA.ReceiptId, recoveredA.ReceiptId);
        Assert.Equal(receiptB.ReceiptId, recoveredB.ReceiptId);
        Assert.NotEqual(recoveredA.ReceiptId, recoveredB.ReceiptId);
        var sourceA = Assert.Single(receiptA.Sources);
        var sourceB = Assert.Single(receiptB.Sources);
        Assert.Equal(ReusableActivityImportResourceDisposition.Created, sourceA.WorkflowDisposition);
        Assert.Equal(ReusableActivityImportResourceDisposition.Created, sourceA.ActivityDefinitionDisposition);
        Assert.Equal(ReusableActivityImportResourceDisposition.Created, sourceA.ActivityVersionDisposition);
        Assert.Equal(ReusableActivityImportResourceDisposition.Reused, sourceB.WorkflowDisposition);
        Assert.Equal(ReusableActivityImportResourceDisposition.Reused, sourceB.ActivityDefinitionDisposition);
        Assert.Equal(ReusableActivityImportResourceDisposition.Reused, sourceB.ActivityVersionDisposition);
        Assert.Equal(2, store.Snapshot(Elsa3ImportStorageManifest.CollectionDocumentKind).Count);
        Assert.Equal(2, store.Snapshot(Elsa3ImportStorageManifest.ReceiptDocumentKind).Count);
        Assert.Equal(2, store.Snapshot(Elsa3ImportStorageManifest.DefinitionBindingDocumentKind).Count);
        Assert.Single(store.Snapshot("activityDefinition"));
        Assert.Single(store.Snapshot("activityDefinitionVersion"));
        Assert.Single(store.Snapshot("workflowDefinition"));
        Assert.Single(store.Snapshot("workflowDefinitionVersion"));
    }

    [Fact]
    public async Task Upload_wraps_stream_failure_and_handle_exhaustion_in_typed_outcomes()
    {
        var failingStreamHarness = Harness();
        await Assert.ThrowsAsync<ReusableActivityImportPayloadException>(async () =>
            await failingStreamHarness.Service.UploadAsync(new ThrowingReadStream(), null, Scope));

        var exhausted = OperationService(new ExhaustedOperationStore());
        var exception = await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await exhausted.UploadAsync(Json(
                ReusableActivityImportFixtures.Workflow(
                    "a",
                    "a-v1",
                    1,
                    true,
                    ReusableActivityImportFixtures.Leaf("root"))), null, Scope));
        Assert.Equal("create collection", exception.Operation);
    }

    [Fact]
    public async Task Operation_store_wraps_storage_and_malformed_documents_and_rejects_scope_mismatch()
    {
        var inner = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var accessor = GroundworkTestAccess.AccessContext(Scope.TenantId!);
        var throwing = new GroundworkReusableActivityImportOperationStore(
            new ThrowingDocumentStore(inner, throwOnLoad: true),
            accessor);
        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await throwing.FindCollectionAsync("handle", Scope));
        var now = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        var collection = new ReusableActivityImportCollectionHandle(
            "save-failure",
            Scope,
            now,
            now.AddHours(1),
            1,
            ReusableActivityImportFixtures.Collection(
                ReusableActivityImportFixtures.Workflow(
                    "save",
                    "save-v1",
                    1,
                    true,
                    ReusableActivityImportFixtures.Leaf("root"))));
        var saveFailure = new GroundworkReusableActivityImportOperationStore(
            new ThrowingDocumentStore(inner, throwOnSave: true),
            accessor);
        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await saveFailure.TryCreateCollectionAsync(collection));

        await inner.SaveAsync(new SaveDocumentRequest(
            "elsa3ReusableImportCollection",
            "malformed-collection",
            "1.0.0",
            "{",
            0));
        var receiptId = Elsa3.Activities.Design.Import.Persistence.Groundwork.Elsa3ImportStorageManifest.ReceiptId("bad", Scope);
        await inner.SaveAsync(new SaveDocumentRequest(
            "elsa3ReusableImportReceipt",
            receiptId,
            "1.0.0",
            "{\"receipt\":null}",
            0));
        var store = new GroundworkReusableActivityImportOperationStore(inner, accessor);

        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await store.FindCollectionAsync("malformed-collection", Scope));
        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await store.FindReceiptAsync("bad", Scope));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await store.FindReceiptAsync("bad", new("tenant-b", "user-a")));
    }

    [Fact]
    public async Task Scoped_identity_collision_fails_without_writing_a_second_receipt()
    {
        var original = ReusableActivityImportFixtures.Workflow(
            "collision",
            "collision-v1",
            1,
            true,
            ReusableActivityImportFixtures.Leaf("original"));
        var changed = ReusableActivityImportFixtures.Workflow(
            "collision",
            "collision-v1",
            1,
            true,
            ReusableActivityImportFixtures.Leaf("changed"));
        var harness = Harness();
        var originalUpload = await harness.Service.UploadAsync(Json(original), null, Scope);
        var originalAnalysis = await harness.Service.AnalyzeAsync(originalUpload.CollectionHandle, 0, 10, Scope);
        await harness.Service.ApplyAsync(
            originalUpload.CollectionHandle,
            originalAnalysis.PlanId,
            ["collision-v1"],
            "collision-first",
            Scope);

        var changedUpload = await harness.Service.UploadAsync(Json(changed), null, Scope);
        var changedAnalysis = await harness.Service.AnalyzeAsync(changedUpload.CollectionHandle, 0, 10, Scope);
        await Assert.ThrowsAsync<ReusableActivityImportCollisionException>(async () =>
            await harness.Service.ApplyAsync(
                changedUpload.CollectionHandle,
                changedAnalysis.PlanId,
                ["collision-v1"],
                "collision-second",
                Scope));

        Assert.Single(harness.Store.Snapshot("elsa3ReusableImportReceipt"));
        Assert.Single(harness.Store.Snapshot("activityDefinitionVersion"));
    }

    [Fact]
    public async Task Atomic_command_wraps_serializer_failures_and_same_receipt_races_converge()
    {
        var source = ReusableActivityImportFixtures.Workflow(
            "race",
            "race-v1",
            1,
            true,
            ReusableActivityImportFixtures.Leaf("root"));
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var collection = ReusableActivityImportFixtures.Collection(source);
        var plan = await analyzer.AnalyzeAsync(collection);
        var item = Assert.Single(plan.Items);
        var mutation = await ReusableActivityImportFixtures.Materializer()
            .MaterializeAsync(collection, plan, [item]);
        foreach (var activity in mutation.Activities)
        {
            activity.Definition.TenantId = Scope.TenantId;
            activity.Version.TenantId = Scope.TenantId;
            activity.AuthoringState.TenantId = Scope.TenantId;
        }
        foreach (var workflow in mutation.Workflows)
        {
            workflow.Definition.TenantId = Scope.TenantId;
            workflow.Version.TenantId = Scope.TenantId;
        }
        mutation = mutation with
        {
            AccessScope = Scope,
            IdempotencyKey = "race-key"
        };

        var serializerFailureStore = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var serializerFailure = new GroundworkReusableActivityImportCommand(
            serializerFailureStore,
            new ThrowingPayloadSerializer(),
            new GroundworkActivityManagementProjectionWriter(serializerFailureStore, new ImmediateLockProvider(), serializerFailureStore),
            GroundworkTestAccess.AccessContext(Scope.TenantId!));
        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await serializerFailure.CommitAsync(mutation));

        var harness = Harness();
        var upload = await harness.Service.UploadAsync(Json(source), null, Scope);
        var analysis = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);
        var attempts = await Task.WhenAll(
            harness.Service.ApplyAsync(upload.CollectionHandle, analysis.PlanId, ["race-v1"], "same-race", Scope).AsTask(),
            harness.Service.ApplyAsync(upload.CollectionHandle, analysis.PlanId, ["race-v1"], "same-race", Scope).AsTask());

        Assert.Equal(attempts[0].ReceiptId, attempts[1].ReceiptId);
        Assert.Single(harness.Store.Snapshot("elsa3ReusableImportReceipt"));
        Assert.Contains(attempts, x => x.Status == ReusableActivityImportReceiptStatus.Applied);
    }

    [Fact]
    public async Task Stale_plan_and_non_closed_selection_write_no_receipt_or_design_documents()
    {
        var a = ReusableActivityImportFixtures.Workflow("a", "a-v1", 1, true, ReusableActivityImportFixtures.Leaf("a-root"));
        var b = ReusableActivityImportFixtures.Workflow("b", "b-v1", 1, true, ReusableActivityImportFixtures.Reference("b-to-a", targetVersionId: "a-v1"));
        var harness = Harness();
        var upload = await harness.Service.UploadAsync(Json(a, b), null, Scope);
        var analysis = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);

        await Assert.ThrowsAsync<ReusableActivityImportValidationException>(async () =>
            await harness.Service.ApplyAsync(upload.CollectionHandle, "stale-plan", ["a-v1"], "stale", Scope));
        await Assert.ThrowsAsync<ReusableActivityImportValidationException>(async () =>
            await harness.Service.ApplyAsync(upload.CollectionHandle, analysis.PlanId, ["b-v1"], "open", Scope));

        Assert.Empty(harness.Store.Snapshot("elsa3ReusableImportReceipt"));
        Assert.Empty(harness.Store.Snapshot("activityDefinition"));
        Assert.Empty(harness.Store.Snapshot("workflowDefinition"));
    }

    [Fact]
    public async Task Public_bounds_and_invalid_requests_fail_before_mutation()
    {
        var harness = Harness(maximumBytes: 2_000);
        await Assert.ThrowsAsync<ReusableActivityImportPayloadException>(async () =>
            await harness.Service.UploadAsync(new MemoryStream("null"u8.ToArray()), null, Scope));
        await Assert.ThrowsAsync<ReusableActivityImportPayloadException>(async () =>
            await harness.Service.UploadAsync(new MemoryStream("not-json"u8.ToArray()), null, Scope));
        await Assert.ThrowsAsync<ReusableActivityImportPayloadException>(async () =>
            await harness.Service.UploadAsync(new MemoryStream("[]"u8.ToArray()), null, Scope));
        var countHarness = Harness(maximumBytes: 1024 * 1024);
        await Assert.ThrowsAsync<ReusableActivityImportPayloadException>(async () =>
            await countHarness.Service.UploadAsync(Json(
                Enumerable.Range(0, 101)
                    .Select(x => ReusableActivityImportFixtures.Workflow(
                        $"d-{x}",
                        $"v-{x}",
                        1,
                        false,
                        ReusableActivityImportFixtures.Leaf($"n-{x}")))
                    .ToArray()), null, Scope));

        var valid = ReusableActivityImportFixtures.Workflow("valid", "valid-v1", 1, true, ReusableActivityImportFixtures.Leaf("root"));
        var upload = await harness.Service.UploadAsync(Json(valid), null, Scope);
        await Assert.ThrowsAsync<ReusableActivityImportPayloadException>(async () =>
            await harness.Service.UploadAsync(Json(valid), -1, Scope));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await harness.Service.AnalyzeAsync(upload.CollectionHandle, -1, 10, Scope));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 0, Scope));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 51, Scope));
        var analysis = await harness.Service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);
        var unknown = await harness.Service.ExpandSelectionAsync(upload.CollectionHandle, analysis.PlanId, ["unknown"], Scope);
        var empty = await harness.Service.ExpandSelectionAsync(upload.CollectionHandle, analysis.PlanId, [], Scope);
        Assert.False(unknown.IsReady);
        Assert.False(empty.IsReady);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await harness.Service.ApplyAsync(upload.CollectionHandle, analysis.PlanId, ["valid-v1"], new string('x', 201), Scope));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await harness.Service.GetStatusAsync("missing", Scope));
        Assert.Empty(harness.Store.Snapshot("elsa3ReusableImportReceipt"));
        Assert.Empty(harness.Store.Snapshot("activityDefinition"));
    }

    private static HarnessState Harness(
        MutableTimeProvider? clock = null,
        long maximumBytes = 64 * 1024,
        ReusableActivityImportAccessScope? scope = null)
    {
        scope ??= Scope;
        clock ??= new(DateTimeOffset.Parse("2026-07-17T10:00:00Z"));
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var accessContext = scope.TenantId is null
            ? new FixedAccessContextAccessor(PersistenceAccessContext.Global)
            : GroundworkTestAccess.AccessContext(scope.TenantId);
        return Harness(store, store, store, accessContext, clock, maximumBytes);
    }

    private static HarnessState Harness(
        IDocumentStore store,
        IBoundedDocumentStore boundedStore,
        InMemoryDocumentStore snapshotStore,
        IPersistenceAccessContextAccessor? accessContext = null,
        MutableTimeProvider? clock = null,
        long maximumBytes = 64 * 1024)
    {
        clock ??= new(DateTimeOffset.Parse("2026-07-17T10:00:00Z"));
        accessContext ??= GroundworkTestAccess.AccessContext(Scope.TenantId!);
        var operationStore = new GroundworkReusableActivityImportOperationStore(store, accessContext);
        var command = new GroundworkReusableActivityImportCommand(
            store,
            Serializer(),
            new GroundworkActivityManagementProjectionWriter(store, new ImmediateLockProvider(), boundedStore),
            accessContext,
            clock);
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var importer = new ReusableActivityCollectionImporter(analyzer, ReusableActivityImportFixtures.Materializer(), command);
        var options = Options.Create(new ReusableActivityImportOptions
        {
            MaximumUploadBytes = maximumBytes,
            MaximumSourceVersions = 100,
            DefaultPageSize = 10,
            MaximumPageSize = 50,
            CollectionLifetime = TimeSpan.FromHours(1)
        });
        return new(
            new ReusableActivityImportOperationService(operationStore, importer, options, clock),
            snapshotStore);
    }

    private static async Task SaveActivityDefinitionAsync(
        IDocumentStore store,
        ActivityDefinition definition)
    {
        var request = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            definition,
            GroundworkActivitiesDesignJson.Options,
            GroundworkTestAccess.AccessContext(Scope.TenantId!).Current);
        await store.SaveAsync(request with { ExpectedVersion = 0 });
    }

    private static async Task SaveWorkflowDefinitionAsync(
        IDocumentStore store,
        WorkflowDefinition definition)
    {
        var request = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            "workflowDefinition",
            "workflowDefinition",
            "1.0.0",
            definition,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            GroundworkTestAccess.AccessContext(Scope.TenantId!).Current);
        await store.SaveAsync(request with { ExpectedVersion = 0 });
    }

    private static ActivityDefinitionAuthoringState CurrentAuthoring(InMemoryDocumentStore store) =>
        ReadGroundworkEntity<ActivityDefinitionAuthoringState>(
            Assert.Single(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind)));

    private static ActivityDefinitionManagementProjectionRevision CurrentManagementProjection(
        InMemoryDocumentStore store) =>
        store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind)
            .Select(ReadGroundworkEntity<ActivityDefinitionManagementProjectionRevision>)
            .Single(x => x.ValidToSequenceExclusive == long.MaxValue);

    private static TEntity ReadGroundworkEntity<TEntity>(DocumentEnvelope envelope)
        where TEntity : Elsa.Primitives.Entities.Entity =>
        JsonSerializer.Deserialize<GroundworkDocument<TEntity>>(
            envelope.ContentJson,
            GroundworkActivitiesDesignJson.Options)!.Entity;

    private static IReusableActivityImportOperationService OperationService(
        IReusableActivityImportOperationStore operationStore)
    {
        var options = Options.Create(new ReusableActivityImportOptions
        {
            MaximumUploadBytes = 64 * 1024,
            MaximumSourceVersions = 100,
            DefaultPageSize = 10,
            MaximumPageSize = 50,
            CollectionLifetime = TimeSpan.FromHours(1)
        });
        return new ReusableActivityImportOperationService(
            operationStore,
            new UnusedImporter(),
            options,
            new MutableTimeProvider(DateTimeOffset.Parse("2026-07-17T10:00:00Z")));
    }

    private static MemoryStream Json(params Elsa3.Models.Elsa3WorkflowDefinition[] definitions) =>
        new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(definitions, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private static IPayloadSerializer Serializer() => new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

    private sealed record HarnessState(IReusableActivityImportOperationService Service, InMemoryDocumentStore Store);

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class ExhaustedOperationStore : IReusableActivityImportOperationStore
    {
        public ValueTask<bool> TryCreateCollectionAsync(ReusableActivityImportCollectionHandle collection, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask<ReusableActivityImportCollectionHandle?> FindCollectionAsync(
            string handle,
            ReusableActivityImportAccessScope accessScope,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ReusableActivityImportCollectionHandle?>(null);

        public ValueTask<ReusableActivityImportReceipt?> FindReceiptAsync(
            string idempotencyKey,
            ReusableActivityImportAccessScope accessScope,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ReusableActivityImportReceipt?>(null);
    }

    private sealed class UnusedImporter : IReusableActivityCollectionImporter
    {
        public ValueTask<ReusableActivityImportPlan> AnalyzeAsync(
            ReusableActivityImportCollection collection,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ReusableActivityImportApplyResult> ApplyAsync(
            ReusableActivityImportApplyRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("read failed");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("read failed"));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ThrowingPayloadSerializer : IPayloadSerializer
    {
        private static Exception Failure() => new NotSupportedException("serializer failed");
        public string Serialize(object payload) => throw Failure();
        public JsonElement SerializeToElement(object payload) => throw Failure();
        public object Deserialize(string serializedData) => throw Failure();
        public object Deserialize(string serializedData, Type type) => throw Failure();
        public object Deserialize(JsonElement serializedData) => throw Failure();
        public T Deserialize<T>(string serializedData) => throw Failure();
        public T Deserialize<T>(JsonElement serializedData) => throw Failure();
        public JsonSerializerOptions GetOptions() => throw Failure();
    }

#pragma warning disable GW0004 // Required legacy IDocumentStore members on the failure-injection test double.
    private sealed class FailingAtomicDocumentStore
        : IDocumentStore, IBoundedDocumentStore
    {
        private readonly InMemoryDocumentStore inner;

        public FailingAtomicDocumentStore(InMemoryDocumentStore inner)
        {
            this.inner = inner;
        }

        public string? FailOnDocumentKind { get; set; }
        public DocumentStoreAccess Access => inner.Access;
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
            DocumentStoreQuery query,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<long> CountAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.CountAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);

        public async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new FailingUnitOfWork(
                await inner.BeginAsync(scope, cancellationToken),
                () => FailOnDocumentKind);

        private sealed class FailingUnitOfWork(
            IDocumentUnitOfWork inner,
            Func<string?> failOnDocumentKind) : IDocumentUnitOfWork
        {
            public Task<DocumentStoreWriteResult> SaveAsync(
                SaveDocumentRequest request,
                CancellationToken cancellationToken = default) =>
                StringComparer.Ordinal.Equals(request.DocumentKind, failOnDocumentKind())
                    ? Task.FromException<DocumentStoreWriteResult>(new IOException("atomic projection failpoint"))
                    : inner.SaveAsync(request, cancellationToken);

            public Task<DocumentStoreWriteResult> DeleteAsync(
                DeleteDocumentRequest request,
                CancellationToken cancellationToken = default) =>
                inner.DeleteAsync(request, cancellationToken);

            public Task<DocumentEnvelope?> LoadAsync(
                string documentKind,
                string id,
                CancellationToken cancellationToken = default) =>
                inner.LoadAsync(documentKind, id, cancellationToken);

            public Task CommitAsync(CancellationToken cancellationToken = default) =>
                inner.CommitAsync(cancellationToken);

            public Task RollbackAsync(CancellationToken cancellationToken = default) =>
                inner.RollbackAsync(cancellationToken);

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    private sealed class ThrowingDocumentStore(
        InMemoryDocumentStore inner,
        bool throwOnLoad = false,
        bool throwOnSave = false) : IDocumentStore
    {
        public DocumentStoreAccess Access => inner.Access;
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            throwOnSave
                ? Task.FromException<DocumentStoreWriteResult>(new IOException("save failed"))
                : inner.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            throwOnLoad
                ? Task.FromException<DocumentEnvelope?>(new IOException("load failed"))
                : inner.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
            DocumentStoreQuery query,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            inner.BeginAsync(scope, cancellationToken);
    }
#pragma warning restore GW0004

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private sealed class ImmediateLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) => new Handle();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
