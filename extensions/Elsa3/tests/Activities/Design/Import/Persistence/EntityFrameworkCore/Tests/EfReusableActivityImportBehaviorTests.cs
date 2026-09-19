using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Stores;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support;
using Elsa3.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support.ImportFixtures;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The import behavioral harness, run against the EF Core ledger and both EF Core Design lanes
/// on one SQLite database, plus the EF-specific transaction, reconciliation, and topology guarantees.
/// </summary>
public sealed class EfReusableActivityImportBehaviorTests : IAsyncLifetime
{
    private static readonly ReusableActivityImportAccessScope Scope = new("tenant-a", "user-a");
    private readonly MutableAccess access = MutableAccess.Tenant("tenant-a");
    private SqliteImportHarness harness = null!;

    private ImportDatabase Db => harness.Database;

    public async Task InitializeAsync() => harness = await SqliteImportHarness.CreateAsync();

    public async Task DisposeAsync() => await harness.DisposeAsync();

    [Fact]
    public async Task Upload_is_bounded_immutable_scoped_cancellable_expiring_and_durable_across_restart()
    {
        var clock = new MutableTimeProvider(Now);
        var upload = await Db.Service(access, clock).UploadAsync(Json(Workflow("a", "a-v1", 1, true, Leaf("root"))), null, Scope);

        Assert.Equal(1, upload.SourceVersionCount);
        Assert.Equal(clock.GetUtcNow().AddHours(1), upload.ExpiresAt);
        await using var restarted = SqliteImportHarness.For(harness.ConnectionString);
        var service = restarted.Service(access, clock);
        Assert.Single((await service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope)).Items);
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await service.AnalyzeAsync(upload.CollectionHandle, 0, 10, new("tenant-b", "user-a")));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await service.AnalyzeAsync(upload.CollectionHandle, 0, 10, new("tenant-a", "user-b")));

        clock.Advance(TimeSpan.FromHours(2));
        await Assert.ThrowsAsync<ReusableActivityImportExpiredException>(async () =>
            await service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await service.UploadAsync(Json(Workflow("b", "b-v1", 1, true, Leaf("root"))), null, Scope, cancellation.Token));
        Assert.Equal(1, (await Db.CountAsync()).Collections);
    }

    [Fact]
    public async Task Analysis_and_closure_expansion_are_side_effect_free()
    {
        var service = Db.Service(access);
        var (upload, planId) = await UploadAsync(service, Scope,
            Workflow("a", "a-v1", 1, true, Leaf("a-root")),
            Workflow("b", "b-v1", 1, true, Reference("b-to-a", "a-v1")));
        var before = await Db.CountAsync();

        var repeated = await service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);
        var readiness = await service.ExpandSelectionAsync(upload.CollectionHandle, planId, ["b-v1"], Scope);

        Assert.Equal(planId, repeated.PlanId);
        Assert.Equal(["a-v1", "b-v1"], readiness.ExpandedSourceVersionIds);
        Assert.Equal(before, await Db.CountAsync());
    }

    [Fact]
    public async Task Apply_commits_every_context_on_one_connection_and_transaction_and_replays_idempotently()
    {
        var capture = new CommandCapture();
        var service = Db.Service(access, command: Db.Command(access, importInterceptors: [capture], activitiesInterceptors: [capture], workflowsInterceptors: [capture]));
        var (upload, planId) = await UploadAsync(service, Scope,
            Workflow("a", "a-v1", 1, true, Leaf("a-root")),
            Workflow("b", "b-v1", 1, true, Reference("b-to-a", "a-v1")));

        var applied = await service.ApplyAsync(upload.CollectionHandle, planId, ["a-v1", "b-v1"], "operation-1", Scope);
        var applyWrites = capture.Commands.Where(command => IsWrite(command.Sql)).ToArray();
        var retried = await service.ApplyAsync(upload.CollectionHandle, planId, ["a-v1", "b-v1"], "operation-1", Scope);
        var recovered = await Db.Service(access).GetStatusAsync("operation-1", Scope);

        Assert.Equal(ReusableActivityImportReceiptStatus.Applied, applied.Status);
        Assert.Equal(ReusableActivityImportReceiptStatus.AlreadyImported, retried.Status);
        Assert.Equal(applied.ReceiptId, recovered.ReceiptId);
        Assert.Equal(["a", "b"], applied.Sources.Select(x => x.SourceDefinitionId).Order(StringComparer.Ordinal));
        Assert.All(applied.Sources, source =>
        {
            Assert.Equal(ReusableActivityImportResourceDisposition.Created, source.WorkflowDisposition);
            Assert.StartsWith("/design/workflows/definitions/", source.WorkflowNavigationIdentity, StringComparison.Ordinal);
            Assert.NotNull(source.ActivityDefinitionId);
            Assert.NotNull(source.ActivityVersionNavigationIdentity);
        });
        var counts = await Db.CountAsync();
        Assert.Equal((1, 4, 2, 2, 2, 2, 2), (counts.Receipts, counts.Bindings, counts.ActivityDefinitions, counts.ActivityVersions, counts.Authoring, counts.WorkflowDefinitions, counts.WorkflowVersions));

        // One physical connection and one transaction carried the writes of all three contexts.
        Assert.Equal(3, applyWrites.Select(command => command.Context).Distinct().Count());
        Assert.Single(applyWrites.Select(command => command.Connection).Distinct());
        Assert.All(applyWrites, command => Assert.NotNull(command.Transaction));
        Assert.Single(applyWrites.Select(command => command.Transaction).Distinct());
        // The import writes only its own ledger and the two Design lanes: no Runtime templates or source references.
        Assert.Subset(await OwnedTablesAsync(), applyWrites.SelectMany(command => WrittenTables(command.Sql)).ToHashSet(StringComparer.Ordinal));

        await Assert.ThrowsAsync<ReusableActivityImportIdempotencyConflictException>(async () =>
            await service.ApplyAsync(upload.CollectionHandle, planId, ["a-v1"], "operation-1", Scope));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await service.GetStatusAsync("operation-1", new("tenant-b", "user-a")));
        Assert.Equal(1, (await Db.CountAsync()).Receipts);
    }

    [Fact]
    public async Task Replayed_receipt_rejects_selection_fingerprint_drift_without_mutating_the_workflow_catalog()
    {
        var service = Db.Service(access);
        var original = await UploadAsync(service, Scope, Workflow("fingerprint", "fingerprint-v1", 1, true, Leaf("original")));
        await service.ApplyAsync(original.Upload.CollectionHandle, original.PlanId, ["fingerprint-v1"], "fingerprint-key", Scope);
        var before = await Db.CountAsync();

        var changed = await UploadAsync(service, Scope, Workflow("fingerprint", "fingerprint-v1", 1, true, Leaf("changed")));
        await Assert.ThrowsAsync<ReusableActivityImportIdempotencyConflictException>(async () =>
            await service.ApplyAsync(changed.Upload.CollectionHandle, changed.PlanId, ["fingerprint-v1"], "fingerprint-key", Scope));

        Assert.Equal(before with { Collections = before.Collections + 1 }, await Db.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Later_valid_subset_reuses_the_definition_and_existing_versions_while_adding_new_versions(bool repeatFirstVersion)
    {
        var service = Db.Service(access);
        var (upload, planId) = await UploadAsync(service, Scope,
            Workflow("a", "a-v1", 1, true, Leaf("root-v1")),
            Workflow("a", "a-v2", 2, true, Leaf("root-v2")));

        var first = await service.ApplyAsync(upload.CollectionHandle, planId, ["a-v1"], "subset-v1", Scope);
        var later = await service.ApplyAsync(upload.CollectionHandle, planId, repeatFirstVersion ? ["a-v1", "a-v2"] : ["a-v2"],
            repeatFirstVersion ? "subset-v1-v2" : "subset-v2", Scope);

        Assert.Equal(ReusableActivityImportResourceDisposition.Created, Assert.Single(first.Sources).ActivityDefinitionDisposition);
        Assert.All(later.Sources, source => Assert.Equal(ReusableActivityImportResourceDisposition.Reused, source.ActivityDefinitionDisposition));
        var v2 = Assert.Single(later.Sources, x => x.SourceVersionId == "a-v2");
        Assert.Equal(ReusableActivityImportResourceDisposition.Created, v2.ActivityVersionDisposition);
        if (repeatFirstVersion)
            Assert.Equal(ReusableActivityImportResourceDisposition.Reused, Assert.Single(later.Sources, x => x.SourceVersionId == "a-v1").ActivityVersionDisposition);
        var counts = await Db.CountAsync();
        Assert.Equal((1, 2, 1, 2, 2), (counts.ActivityDefinitions, counts.ActivityVersions, counts.Authoring, counts.WorkflowVersions, counts.Receipts));
        Assert.Equal(v2.ActivityDefinitionVersionId, (await CurrentAuthoringAsync()).HeadVersionId);
        var projection = await CurrentProjectionAsync();
        Assert.Equal(v2.ActivityDefinitionVersionId, projection.HeadVersionId);
        // An imported version is the head before it is ever published: the projection carries no head reference.
        Assert.Null(projection.Head);
    }

    [Fact]
    public async Task Failure_after_the_design_writes_rolls_back_every_context_of_a_first_import()
    {
        var failing = Db.Command(access, activitiesInterceptors: [new SaveFailureInterceptor<ActivityManagementProjectionSnapshot>()]);
        var service = Db.Service(access, command: failing);
        var (upload, planId) = await UploadAsync(service, Scope,
            Workflow("a", "a-v1", 1, true, Leaf("a-root")),
            Workflow("b", "b-v1", 1, true, Reference("b-to-a", "a-v1")));

        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await service.ApplyAsync(upload.CollectionHandle, planId, ["a-v1", "b-v1"], "rolled-back", Scope));

        // The receipt, bindings, workflow rows, activity rows, and both design-operation ledgers were written
        // before the projection failed; none of them survived.
        Assert.True((await Db.CountAsync()).HasNoImportWrites);
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await Db.Service(access).GetStatusAsync("rolled-back", Scope));
        var clean = await Db.Service(access).ApplyAsync(upload.CollectionHandle, planId, ["a-v1", "b-v1"], "rolled-back", Scope);
        Assert.Equal(ReusableActivityImportReceiptStatus.Applied, clean.Status);
    }

    [Fact]
    public async Task Later_subset_projection_failure_rolls_back_the_head_version_and_receipt_atomically()
    {
        var (upload, planId) = await UploadAsync(Db.Service(access), Scope,
            Workflow("atomic", "atomic-v1", 1, true, Leaf("root-v1")),
            Workflow("atomic", "atomic-v2", 2, true, Leaf("root-v2")));
        var first = await Db.Service(access).ApplyAsync(upload.CollectionHandle, planId, ["atomic-v1"], "atomic-first", Scope);
        var v1 = Assert.Single(first.Sources).ActivityDefinitionVersionId;
        var failing = Db.Service(access, command: Db.Command(access, activitiesInterceptors: [new SaveFailureInterceptor<ActivityManagementProjectionSnapshot>()]));

        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await failing.ApplyAsync(upload.CollectionHandle, planId, ["atomic-v2"], "atomic-second", Scope));

        var counts = await Db.CountAsync();
        Assert.Equal((1, 1, 1, 2), (counts.ActivityVersions, counts.WorkflowVersions, counts.Receipts, counts.Bindings));
        Assert.Equal(v1, (await CurrentAuthoringAsync()).HeadVersionId);
        Assert.Equal(v1, (await CurrentProjectionAsync()).HeadVersionId);
    }

    /// <summary>
    /// The preflight reads one batch per collection instead of one row per item. A plan that mixes items which are
    /// already imported with new ones must still reuse the former and create the latter, and a colliding item sitting
    /// in the same batch as healthy siblings must still be refused with the message it carried when every item was
    /// read on its own.
    /// </summary>
    [Fact]
    public async Task Mixed_new_and_existing_items_across_every_collection_resolve_per_item_from_batched_reads()
    {
        var reusableA = Workflow("reusable-a", "reusable-a-v1", 1, true, Leaf("root-a"));
        var plainB = Workflow("plain-b", "plain-b-v1", 1, false, Leaf("root-b"));
        var service = Db.Service(access);

        var seedUpload = await service.UploadAsync(Json(reusableA, plainB), null, Scope);
        var seedAnalysis = await service.AnalyzeAsync(seedUpload.CollectionHandle, 0, 10, Scope);
        var reusedActivityDefinitionId = seedAnalysis.Items
            .Single(item => StringComparer.Ordinal.Equals(item.SourceDefinitionId, reusableA.DefinitionId)).ActivityDefinitionId!;
        await service.ApplyAsync(seedUpload.CollectionHandle, seedAnalysis.PlanId, [reusableA.Id, plainB.Id], "mixed-seed", Scope);
        var seeded = await Db.CountAsync();

        // Every collection now carries an already-imported item and a new one inside the same batched read.
        var reusableC = Workflow("reusable-c", "reusable-c-v1", 1, true, Leaf("root-c"));
        var plainD = Workflow("plain-d", "plain-d-v1", 1, false, Leaf("root-d"));
        var (mixedUpload, mixedPlan) = await UploadAsync(service, Scope, reusableA, plainB, reusableC, plainD);
        await service.ApplyAsync(mixedUpload.CollectionHandle, mixedPlan,
            [reusableA.Id, plainB.Id, reusableC.Id, plainD.Id], "mixed-apply", Scope);

        var mixed = await Db.CountAsync();
        Assert.Equal(seeded.ActivityDefinitions + 1, mixed.ActivityDefinitions);
        Assert.Equal(seeded.ActivityVersions + 1, mixed.ActivityVersions);
        Assert.Equal(seeded.Authoring + 1, mixed.Authoring);
        Assert.Equal(seeded.WorkflowDefinitions + 2, mixed.WorkflowDefinitions);
        Assert.Equal(seeded.WorkflowVersions + 2, mixed.WorkflowVersions);
        Assert.True(mixed.Bindings > seeded.Bindings, "the two new sources must add bindings");
        await using (var activities = Db.Activities())
            Assert.Equal(1, await activities.ActivityDefinitions.CountAsync(row => row.Id == reusedActivityDefinitionId));

        // A colliding item among healthy siblings: an unrelated row already owns one plan item's activity type key.
        var reusableE = Workflow("reusable-e", "reusable-e-v1", 1, true, Leaf("root-e"));
        var reusableF = Workflow("reusable-f", "reusable-f-v1", 1, true, Leaf("root-f"));
        var (collidingUpload, collidingPlan) = await UploadAsync(service, Scope, reusableE, reusableF);
        var victim = (await service.AnalyzeAsync(collidingUpload.CollectionHandle, 0, 10, Scope))
            .Items.Single(item => StringComparer.Ordinal.Equals(item.SourceDefinitionId, reusableF.DefinitionId));
        await using (var activities = Db.Activities())
        {
            activities.ActivityDefinitions.Add(new ActivityDefinition
            {
                Id = "unrelated-owner",
                TenantId = Scope.TenantId,
                ActivityTypeKey = victim.ActivityTypeKey!,
                Category = "Elsa 3 reusable workflows",
                DisplayName = reusableF.Name,
                Description = reusableF.Description,
                CreatedAt = reusableF.CreatedAt
            });
            await activities.SaveChangesAsync();
        }

        var before = await Db.CountAsync();
        var collision = await Assert.ThrowsAsync<ReusableActivityImportCollisionException>(async () =>
            await service.ApplyAsync(collidingUpload.CollectionHandle, collidingPlan, [reusableE.Id, reusableF.Id], "mixed-collision", Scope));

        Assert.Equal($"Elsa 3 activity definition identity '{victim.ActivityDefinitionId}' is already owned by a different resource.", collision.Message);
        var after = await Db.CountAsync();
        Assert.Equal(
            (before.Bindings, before.ActivityDefinitions, before.ActivityVersions, before.WorkflowDefinitions, before.WorkflowVersions),
            (after.Bindings, after.ActivityDefinitions, after.ActivityVersions, after.WorkflowDefinitions, after.WorkflowVersions));
    }

    [Fact]
    public async Task Unrelated_activity_definition_with_a_matching_shell_but_no_import_binding_fails_before_writes()
    {
        var source = Workflow("activity-owner", "activity-owner-v1", 1, true, Leaf("root"));
        var service = Db.Service(access);
        var (upload, planId) = await UploadAsync(service, Scope, source);
        var item = Assert.Single((await service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope)).Items);
        await using (var activities = Db.Activities())
        {
            activities.ActivityDefinitions.Add(new ActivityDefinition
            {
                Id = item.ActivityDefinitionId!,
                TenantId = Scope.TenantId,
                ActivityTypeKey = item.ActivityTypeKey!,
                Category = "Elsa 3 reusable workflows",
                DisplayName = source.Name,
                Description = source.Description,
                CreatedAt = source.CreatedAt
            });
            await activities.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<ReusableActivityImportCollisionException>(async () =>
            await service.ApplyAsync(upload.CollectionHandle, planId, [source.Id], "unrelated-activity", Scope));

        var counts = await Db.CountAsync();
        Assert.Equal((0, 0, 0, 0, 0, 1), (counts.Bindings, counts.ActivityVersions, counts.Authoring, counts.WorkflowDefinitions, counts.Receipts, counts.ActivityDefinitions));
    }

    [Fact]
    public async Task Unrelated_workflow_definition_with_identical_presentation_but_no_import_binding_fails_before_writes()
    {
        var source = Workflow("workflow-owner", "workflow-owner-v1", 1, false, Leaf("root"));
        var service = Db.Service(access);
        var (upload, planId) = await UploadAsync(service, Scope, source);
        var item = Assert.Single((await service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope)).Items);
        await using (var workflows = Db.Workflows())
        {
            workflows.Definitions.Add(new WorkflowDefinition
            {
                Id = item.WorkflowDefinitionId,
                TenantId = Scope.TenantId,
                Name = source.Name,
                Description = source.Description,
                CreatedAt = source.CreatedAt,
                LastModifiedAt = source.CreatedAt
            });
            await workflows.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<ReusableActivityImportCollisionException>(async () =>
            await service.ApplyAsync(upload.CollectionHandle, planId, [source.Id], "unrelated-workflow", Scope));

        var counts = await Db.CountAsync();
        Assert.Equal((0, 0, 0, 0, 1), (counts.Bindings, counts.WorkflowVersions, counts.ActivityDefinitions, counts.Receipts, counts.WorkflowDefinitions));
    }

    [Fact]
    public async Task Same_export_imported_by_two_tenants_in_one_database_is_isolated_by_the_ambient_scope()
    {
        var source = Workflow("shared", "shared-v1", 1, true, Leaf("root"));
        var tenantA = new ReusableActivityImportAccessScope("tenant-a", "user-a");
        var tenantB = new ReusableActivityImportAccessScope("tenant-b", "user-b");
        var accessB = MutableAccess.Tenant("tenant-b");
        var serviceA = Db.Service(access);
        var serviceB = Db.Service(accessB);
        var uploadA = await UploadAsync(serviceA, tenantA, source);
        var uploadB = await UploadAsync(serviceB, tenantB, source);

        var receiptA = await serviceA.ApplyAsync(uploadA.Upload.CollectionHandle, uploadA.PlanId, ["shared-v1"], "same-key", tenantA);
        var receiptB = await serviceB.ApplyAsync(uploadB.Upload.CollectionHandle, uploadB.PlanId, ["shared-v1"], "same-key", tenantB);

        Assert.NotEqual(receiptA.ReceiptId, receiptB.ReceiptId);
        Assert.Equal(ReusableActivityImportResourceDisposition.Created, receiptB.Sources[0].ActivityDefinitionDisposition);
        var definitionId = receiptA.Sources[0].ActivityDefinitionId!;
        Assert.Equal(definitionId, receiptB.Sources[0].ActivityDefinitionId);
        var counts = await Db.CountAsync();
        Assert.Equal((2, 2, 4, 2), (counts.ActivityDefinitions, counts.WorkflowDefinitions, counts.Bindings, counts.Receipts));
        Assert.Equal("tenant-a", (await new EfActivityDesignStores(Db.Activities(), access).GetAsync(definitionId)).TenantId);
        Assert.Equal("tenant-b", (await new EfActivityDesignStores(Db.Activities(), accessB).GetAsync(definitionId)).TenantId);
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () => await serviceA.GetStatusAsync("same-key", tenantB));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await serviceA.AnalyzeAsync(uploadB.Upload.CollectionHandle, 0, 10, tenantA));
    }

    [Fact]
    public async Task Same_tenant_users_reuse_tenant_design_but_own_independent_operations_and_idempotency_keys()
    {
        var source = Workflow("tenant-shared", "tenant-shared-v1", 1, true, Leaf("root"));
        var userA = new ReusableActivityImportAccessScope("tenant-a", "user-a");
        var userB = new ReusableActivityImportAccessScope("tenant-a", "user-b");
        var service = Db.Service(access);
        var uploadA = await UploadAsync(service, userA, source);
        var uploadB = await UploadAsync(service, userB, source);

        var receiptA = await service.ApplyAsync(uploadA.Upload.CollectionHandle, uploadA.PlanId, [source.Id], "same-key", userA);
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () => await service.GetStatusAsync("same-key", userB));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () =>
            await service.ApplyAsync(uploadA.Upload.CollectionHandle, uploadA.PlanId, [source.Id], "same-key", userB));
        var receiptB = await service.ApplyAsync(uploadB.Upload.CollectionHandle, uploadB.PlanId, [source.Id], "same-key", userB);

        Assert.Equal(ReusableActivityImportIdentity.Receipt("same-key", userA), receiptA.ReceiptId);
        Assert.Equal(ReusableActivityImportIdentity.Receipt("same-key", userB), receiptB.ReceiptId);
        Assert.Equal(receiptA.ReceiptId, (await service.GetStatusAsync("same-key", userA)).ReceiptId);
        var sourceA = Assert.Single(receiptA.Sources);
        var sourceB = Assert.Single(receiptB.Sources);
        Assert.Equal(ReusableActivityImportResourceDisposition.Created, sourceA.ActivityVersionDisposition);
        Assert.Equal(ReusableActivityImportResourceDisposition.Reused, sourceB.WorkflowDisposition);
        Assert.Equal(ReusableActivityImportResourceDisposition.Reused, sourceB.ActivityDefinitionDisposition);
        Assert.Equal(ReusableActivityImportResourceDisposition.Reused, sourceB.ActivityVersionDisposition);
        var counts = await Db.CountAsync();
        Assert.Equal((2, 2, 2, 1, 1, 1, 1), (counts.Collections, counts.Receipts, counts.Bindings, counts.ActivityDefinitions, counts.ActivityVersions, counts.WorkflowDefinitions, counts.WorkflowVersions));
    }

    [Fact]
    public async Task Operation_store_fails_closed_on_hash_drift_and_corrupt_rows_and_hides_scope_mismatch()
    {
        var store = Db.OperationStore(access);
        var service = Db.Service(access);
        var drifted = await service.UploadAsync(Json(Workflow("drift", "drift-v1", 1, true, Leaf("root"))), null, Scope);
        var renamed = await service.UploadAsync(Json(Workflow("renamed", "renamed-v1", 1, true, Leaf("root"))), null, Scope);
        var (upload, planId) = await UploadAsync(service, Scope, Workflow("receipt", "receipt-v1", 1, true, Leaf("root")));
        await service.ApplyAsync(upload.CollectionHandle, planId, ["receipt-v1"], "corrupt-receipt", Scope);
        await using (var import = Db.Import())
        {
            // Content edited without its hash: drift.
            await import.Collections.Where(row => row.HandleHash == EfRelationalIdentity.Hash(drifted.CollectionHandle))
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.ContentJson, row => row.ContentJson.Replace("drift", "tampered")));
            // Residual rewritten to another identity under the same lookup hash.
            await import.Collections.Where(row => row.HandleHash == EfRelationalIdentity.Hash(renamed.CollectionHandle))
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Handle, EfRelationalIdentity.Encode("another-handle")));
            // Consistent hash, but content that is no receipt at all.
            const string empty = "{\"receipt\":null}";
            await import.Receipts.ExecuteUpdateAsync(set => set
                .SetProperty(row => row.ContentJson, empty)
                .SetProperty(row => row.ContentHash, EfRelationalIdentity.Hash(empty)));
        }

        Assert.Equal("load collection", (await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await store.FindCollectionAsync(drifted.CollectionHandle, Scope))).Operation);
        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () => await store.FindCollectionAsync(renamed.CollectionHandle, Scope));
        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () => await store.FindReceiptAsync("corrupt-receipt", Scope));
        // A corrupt receipt is never mistaken for absence: a replay fails instead of writing a second import.
        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await service.ApplyAsync(upload.CollectionHandle, planId, ["receipt-v1"], "corrupt-receipt", Scope));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () => await store.FindReceiptAsync("corrupt-receipt", new("tenant-b", "user-a")));
        Assert.Null(await store.FindReceiptAsync("corrupt-receipt", new("tenant-a", "user-b")));
        Assert.Equal(1, (await Db.CountAsync()).Receipts);
    }

    [Fact]
    public async Task Collection_handle_is_append_only_and_storage_failures_are_typed()
    {
        var store = Db.OperationStore(access);
        var collection = new ReusableActivityImportCollectionHandle(
            "handle", Scope, Now, Now.AddHours(1), 1, new ReusableActivityImportCollection("handle", [Workflow("a", "a-v1", 1, true, Leaf("root"))]));

        Assert.True(await store.TryCreateCollectionAsync(collection));
        Assert.False(await store.TryCreateCollectionAsync(collection with { ContentLength = 2 }));
        Assert.Equal(1, (await store.FindCollectionAsync("handle", Scope))!.ContentLength);
        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await store.TryCreateCollectionAsync(collection with { AccessScope = new("tenant-b", "user-a") }));

        var unavailable = new EfReusableActivityImportOperationStore(
            new Elsa3ImportSqliteDbContext(SqliteImportHarness.Options<Elsa3ImportSqliteDbContext>("Data Source=/nonexistent-directory/elsa3.db;Mode=ReadOnly")),
            access);
        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () => await unavailable.FindCollectionAsync("handle", Scope));
        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () => await unavailable.TryCreateCollectionAsync(collection));
    }

    [Fact]
    public async Task Scoped_identity_collision_fails_without_writing_a_second_receipt()
    {
        var service = Db.Service(access);
        var original = await UploadAsync(service, Scope, Workflow("collision", "collision-v1", 1, true, Leaf("original")));
        await service.ApplyAsync(original.Upload.CollectionHandle, original.PlanId, ["collision-v1"], "collision-first", Scope);

        var changed = await UploadAsync(service, Scope, Workflow("collision", "collision-v1", 1, true, Leaf("changed")));
        await Assert.ThrowsAsync<ReusableActivityImportCollisionException>(async () =>
            await service.ApplyAsync(changed.Upload.CollectionHandle, changed.PlanId, ["collision-v1"], "collision-second", Scope));

        var counts = await Db.CountAsync();
        Assert.Equal((1, 1), (counts.Receipts, counts.ActivityVersions));
    }

    [Fact]
    public async Task Serializer_failures_are_wrapped_before_writes_and_same_receipt_races_converge()
    {
        var source = Workflow("race", "race-v1", 1, true, Leaf("root"));
        var mutation = await MutationAsync(Scope, "race-key", source);
        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await Db.Command(access, serializer: new ThrowingPayloadSerializer()).CommitAsync(mutation));
        Assert.True((await Db.CountAsync()).HasNoImportWrites);

        var (upload, planId) = await UploadAsync(Db.Service(access), Scope, source);
        var attempts = await Task.WhenAll(
            Db.Service(access).ApplyAsync(upload.CollectionHandle, planId, ["race-v1"], "same-race", Scope).AsTask(),
            Db.Service(access).ApplyAsync(upload.CollectionHandle, planId, ["race-v1"], "same-race", Scope).AsTask());

        Assert.Equal(attempts[0].ReceiptId, attempts[1].ReceiptId);
        Assert.Single(attempts, attempt => attempt.Status == ReusableActivityImportReceiptStatus.Applied);
        var counts = await Db.CountAsync();
        Assert.Equal((1, 1, 1), (counts.Receipts, counts.ActivityDefinitions, counts.WorkflowVersions));
    }

    [Fact]
    public async Task Stale_plan_and_non_closed_selection_write_no_receipt_or_design_rows()
    {
        var service = Db.Service(access);
        var (upload, planId) = await UploadAsync(service, Scope,
            Workflow("a", "a-v1", 1, true, Leaf("a-root")),
            Workflow("b", "b-v1", 1, true, Reference("b-to-a", "a-v1")));

        await Assert.ThrowsAsync<ReusableActivityImportValidationException>(async () =>
            await service.ApplyAsync(upload.CollectionHandle, "stale-plan", ["a-v1"], "stale", Scope));
        await Assert.ThrowsAsync<ReusableActivityImportValidationException>(async () =>
            await service.ApplyAsync(upload.CollectionHandle, planId, ["b-v1"], "open", Scope));

        Assert.True((await Db.CountAsync()).HasNoImportWrites);
    }

    [Fact]
    public async Task Commit_failure_before_the_provider_commits_is_never_reported_as_applied()
    {
        var failure = new CommitFailureInterceptor(afterCommit: false);
        var service = Db.Service(access, command: Db.Command(access, importInterceptors: [failure]));
        var (upload, planId) = await UploadAsync(service, Scope, Workflow("a", "a-v1", 1, true, Leaf("root")));

        var exception = await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await service.ApplyAsync(upload.CollectionHandle, planId, ["a-v1"], "lost-before", Scope));

        Assert.IsType<EfCommitOutcomeUnknownException>(exception.InnerException);
        Assert.Equal(0, failure.Commits);
        Assert.True((await Db.CountAsync()).HasNoImportWrites);
        // The key was not consumed: the reconciliation found no durable receipt, so a retry applies.
        var retried = await Db.Service(access).ApplyAsync(upload.CollectionHandle, planId, ["a-v1"], "lost-before", Scope);
        Assert.Equal(ReusableActivityImportReceiptStatus.Applied, retried.Status);
    }

    [Fact]
    public async Task Lost_commit_acknowledgement_reconciles_to_the_durable_receipt_without_duplicating_rows()
    {
        var failure = new CommitFailureInterceptor(afterCommit: true);
        var service = Db.Service(access, command: Db.Command(access, importInterceptors: [failure]));
        var (upload, planId) = await UploadAsync(service, Scope,
            Workflow("a", "a-v1", 1, true, Leaf("a-root")),
            Workflow("b", "b-v1", 1, true, Reference("b-to-a", "a-v1")));

        var reconciled = await service.ApplyAsync(upload.CollectionHandle, planId, ["a-v1", "b-v1"], "lost-after", Scope);
        var replayed = await Db.Service(access).ApplyAsync(upload.CollectionHandle, planId, ["a-v1", "b-v1"], "lost-after", Scope);

        Assert.Equal(1, failure.Commits);
        // This attempt's own commit landed, so the reconciled receipt is the applied one, not a replay.
        Assert.Equal(ReusableActivityImportReceiptStatus.Applied, reconciled.Status);
        Assert.Equal(ReusableActivityImportReceiptStatus.AlreadyImported, replayed.Status);
        Assert.Equal(reconciled.ReceiptId, replayed.ReceiptId);
        var counts = await Db.CountAsync();
        Assert.Equal((1, 4, 2, 2, 2, 2), (counts.Receipts, counts.Bindings, counts.ActivityDefinitions, counts.ActivityVersions, counts.WorkflowDefinitions, counts.WorkflowVersions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_racing_the_commit_is_a_cancellation_unless_the_commit_became_durable(bool afterCommit)
    {
        using var caller = new CancellationTokenSource();
        var mutation = await MutationAsync(Scope, "cancelled", Workflow("a", "a-v1", 1, true, Leaf("root")));
        var command = Db.Command(access, importInterceptors: [new CommitCancellationInterceptor(caller, afterCommit)]);

        if (!afterCommit)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await command.CommitAsync(mutation, caller.Token));
            Assert.True((await Db.CountAsync()).HasNoImportWrites);
            return;
        }

        // The provider committed before the caller's cancellation surfaced: the durable receipt is the answer.
        var result = await command.CommitAsync(mutation, caller.Token);
        Assert.False(result.NoOp);
        Assert.Equal(ReusableActivityImportReceiptStatus.Applied, result.Receipt!.Status);
        Assert.Equal(1, (await Db.CountAsync()).Receipts);
    }

    [Fact]
    public async Task Split_database_targets_are_refused_before_any_write()
    {
        var otherPath = Path.Combine(Path.GetTempPath(), $"elsa3-import-ef-split-{Guid.NewGuid():N}.db");
        var other = SqliteImportHarness.For(SqliteImportHarness.ConnectionStringFor(otherPath));
        try
        {
            await other.CreateSchemaAsync();
            var split = new EfReusableActivityImportCommand(
                Db.Import(),
                other.Activities(),
                Db.Workflows(),
                access,
                Serializer());
            var service = Db.Service(access, command: split);
            var (upload, planId) = await UploadAsync(service, Scope, Workflow("a", "a-v1", 1, true, Leaf("root")));

            var exception = await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
                await service.ApplyAsync(upload.CollectionHandle, planId, ["a-v1"], "split", Scope));

            Assert.IsType<EfSharedTransactionTargetMismatchException>(exception.InnerException);
            Assert.DoesNotContain(otherPath, exception.ToString(), StringComparison.Ordinal);
            Assert.True((await Db.CountAsync()).HasNoImportWrites);
            Assert.True((await other.CountAsync()).HasNoImportWrites);
        }
        finally
        {
            await other.DisposeAsync();
            TemporarySqliteDatabase.ClearPoolAndDeleteFiles(otherPath);
        }
    }

    [Fact]
    public async Task Provider_mismatch_between_enlisted_contexts_is_refused_before_any_connection_opens()
    {
        var mismatched = new EfReusableActivityImportCommand(
            Db.Import(),
            new ActivitiesDesignSqlServerDbContext(new DbContextOptionsBuilder<ActivitiesDesignSqlServerDbContext>()
                .UseSqlServer("Server=unreachable.invalid;Database=elsa;User Id=sa;Password=never-used;Connect Timeout=1").Options),
            Db.Workflows(),
            access,
            Serializer());
        var mutation = await MutationAsync(Scope, "mismatch", Workflow("a", "a-v1", 1, true, Leaf("root")));

        var exception = await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () => await mismatched.CommitAsync(mutation));

        Assert.IsType<EfSharedTransactionTargetMismatchException>(exception.InnerException);
        Assert.DoesNotContain("never-used", exception.ToString(), StringComparison.Ordinal);
        Assert.True((await Db.CountAsync()).HasNoImportWrites);
    }

    [Fact]
    public async Task Imported_rows_are_durable_across_restart_and_consistent_with_both_design_lanes()
    {
        var (upload, planId) = await UploadAsync(Db.Service(access), Scope,
            Workflow("a", "a-v1", 1, true, Leaf("a-root")),
            Workflow("consumer", "consumer-v1", 1, false, Reference("consumer-to-a", "a-v1")));
        var applied = await Db.Service(access).ApplyAsync(upload.CollectionHandle, planId, ["a-v1", "consumer-v1"], "durable", Scope);

        await using var restarted = SqliteImportHarness.For(harness.ConnectionString);
        var service = restarted.Service(access);
        Assert.Equal(applied.ReceiptId, (await service.GetStatusAsync("durable", Scope)).ReceiptId);
        Assert.Equal(ReusableActivityImportReceiptStatus.AlreadyImported,
            (await service.ApplyAsync(upload.CollectionHandle, planId, ["a-v1", "consumer-v1"], "durable", Scope)).Status);

        var reusable = Assert.Single(applied.Sources, x => x.SourceVersionId == "a-v1");
        var consumer = Assert.Single(applied.Sources, x => x.SourceVersionId == "consumer-v1");
        var stores = new EfActivityDesignStores(restarted.Activities(), access);
        var version = await ((IActivityDefinitionVersionStore)stores).GetAsync(reusable.ActivityDefinitionVersionId!);
        Assert.Equal("elsa.activity-graph", version.ProviderKey);
        Assert.Equal("a-v1", version.SourceId);
        var definitionProjection = await stores.FindDefinitionAsync(reusable.ActivityDefinitionId!, "tenant-a");
        Assert.NotNull(definitionProjection);
        Assert.Equal(reusable.ActivityDefinitionVersionId, definitionProjection.HeadVersionId);
        var workflows = restarted.Workflows();
        var workflowDefinitions = new EfWorkflowDefinitionStore(workflows, access);
        var workflowVersion = await new EfWorkflowDefinitionVersionStore(workflows, Serializer(), workflowDefinitions, access)
            .FindByIdAsync(consumer.WorkflowVersionId);
        Assert.NotNull(workflowVersion);
        Assert.Equal(reusable.ActivityDefinitionVersionId, workflowVersion.State.RootActivity!.ActivityVersionId);
        Assert.NotNull(await workflowDefinitions.FindByIdAsync(consumer.WorkflowDefinitionId));
    }

    [Fact]
    public async Task Global_scope_import_is_refused_before_any_write_because_workflow_design_writes_are_tenant_scoped()
    {
        var global = new MutableAccess(PersistenceAccessContext.Global);
        var scope = new ReusableActivityImportAccessScope(null, "user-a");
        var service = Db.Service(global);
        var (upload, planId) = await UploadAsync(service, scope, Workflow("a", "a-v1", 1, true, Leaf("root")));

        var exception = await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await service.ApplyAsync(upload.CollectionHandle, planId, ["a-v1"], "global", scope));

        Assert.Equal("validate persistence scope", exception.Operation);
        Assert.True((await Db.CountAsync()).HasNoImportWrites);
    }

    [Fact]
    public async Task Unscoped_mutation_is_written_in_the_ambient_tenant_and_reapplies_as_a_no_op()
    {
        var mutation = (await MutationAsync(Scope, "unused", Workflow("a", "a-v1", 1, true, Leaf("root")))) with { AccessScope = null, IdempotencyKey = null };
        foreach (var activity in mutation.Activities)
        {
            activity.Definition.TenantId = null;
            activity.Version.TenantId = null;
            activity.AuthoringState.TenantId = null;
        }

        var first = await Db.Command(access).CommitAsync(mutation);
        var second = await Db.Command(access).CommitAsync(mutation);

        Assert.False(first.NoOp);
        Assert.True(second.NoOp);
        Assert.Null(first.Receipt);
        await using var activities = Db.Activities();
        Assert.All(await activities.ActivityDefinitions.AsNoTracking().ToListAsync(), row => Assert.Equal("tenant-a", row.TenantId));
    }

    private static async Task<(ReusableActivityImportUploadResult Upload, string PlanId)> UploadAsync(
        IReusableActivityImportOperationService service,
        ReusableActivityImportAccessScope scope,
        params Elsa3WorkflowDefinition[] definitions)
    {
        var upload = await service.UploadAsync(Json(definitions), null, scope);
        return (upload, (await service.AnalyzeAsync(upload.CollectionHandle, 0, 10, scope)).PlanId);
    }

    private async Task<ActivityDefinitionAuthoringState> CurrentAuthoringAsync()
    {
        await using var activities = Db.Activities();
        return await activities.ActivityDefinitionAuthoringStates.AsNoTracking().SingleAsync();
    }

    private async Task<ActivityDefinitionManagementProjectionRevision> CurrentProjectionAsync()
    {
        await using var activities = Db.Activities();
        return await activities.ActivityDefinitionManagementProjections.AsNoTracking()
            .SingleAsync(row => row.ValidToSequenceExclusive == long.MaxValue);
    }

    private async Task<HashSet<string>> OwnedTablesAsync()
    {
        await using var import = Db.Import();
        await using var activities = Db.Activities();
        await using var workflows = Db.Workflows();
        return new DbContext[] { import, activities, workflows }
            .SelectMany(context => context.Model.GetEntityTypes())
            .Select(entity => entity.GetTableName()!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsWrite(string sql) => WrittenTables(sql).Any();

    private static IEnumerable<string> WrittenTables(string sql) =>
        Regex.Matches(sql, "(?:INSERT INTO|UPDATE|DELETE FROM)\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase).Select(match => match.Groups[1].Value);

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
}
