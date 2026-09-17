using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

using static Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.ActivityPublicationTestMaterial;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// ADR 0066 over EF: reusable-activity publication is an ordered, forward-converging sequence across three
/// contexts, each in its own database here, exactly as on a host that splits the lanes. The crash tests cut the
/// sequence at each phase boundary at the storage seam and assert what a host dying there leaves, and that a
/// retry of the same commit converges without redoing a phase.
/// </summary>
public sealed class EfActivityPublicationCommandTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Seeded = ActivityPublicationTestMaterial.Seeded;
    private static readonly DateTimeOffset Published = ActivityPublicationTestMaterial.Published;
    private SqliteTestDatabase publishing = null!;
    private SqliteTestDatabase design = null!;
    private SqliteTestDatabase runtime = null!;

    public async Task InitializeAsync()
    {
        publishing = await SqliteTestDatabase.CreateAsync<PublishingSnapshotReviewSqliteDbContext>(options => new(options));
        // Publication runs the design module's duplicate probes. EF reports a Skip or Take without an OrderBy only as
        // query-time warning 10102 when a query first compiles, so the design database raises it as an error.
        design = await SqliteTestDatabase.CreateAsync<ActivitiesDesignSqliteDbContext>(
            options => new(options),
            options => options.ConfigureWarnings(warnings => warnings.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning)));
        runtime = await SqliteTestDatabase.CreateAsync<BookmarkStateSqliteDbContext>(options => new(options));
        await SeedAsync("definition-1", "draft-1");
    }

    public async Task DisposeAsync()
    {
        await publishing.DisposeAsync();
        await design.DisposeAsync();
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task Publication_commits_runtime_design_and_receipt_and_leaves_nothing_tracked()
    {
        var commit = Commit();
        await using (var scope = Open())
        {
            var result = await scope.Command.ExecuteAsync(commit);
            Assert.Equal(new ActivityPublicationResult("definition-1", "version-1", "draft-1", commit.ExecutableTemplate.TemplateId, "source-ref-1", Published), result);
            Assert.Empty(scope.Publishing.ChangeTracker.Entries());
            Assert.Empty(scope.Design.ChangeTracker.Entries());
            Assert.Empty(scope.Runtime.ChangeTracker.Entries());
        }

        await using var verify = Open();
        Assert.NotNull(await verify.Templates.FindAsync(commit.ExecutableTemplate.TemplateId));
        Assert.NotNull(await verify.SourceReferences.FindAsync("source-ref-1"));
        var version = await ((IActivityDefinitionVersionStore)verify.DesignStores).GetAsync("version-1");
        Assert.Equal(commit.Design.CatalogVersion.DescriptorPayload.GetRawText(), version.DescriptorPayload.GetRawText());
        Assert.NotNull(await ((IActivityDefinitionVersionPublicationStore)verify.DesignStores).FindAsync("version-1"));
        Assert.NotNull(await verify.DesignStores.FindVersionLayoutAsync("version-1"));
        var draft = await ((IActivityDefinitionDraftStore)verify.DesignStores).FindAsync("draft-1");
        Assert.Equal(ActivityDefinitionDraftStatus.Published, draft!.Status);
        Assert.Equal("version-1", draft.PublishedVersionId);
        var authoring = await ((IActivityDefinitionAuthoringStore)verify.DesignStores).FindAsync("definition-1");
        Assert.Equal("version-1", authoring!.HeadVersionId);
        Assert.Equal("version-1", authoring.RecommendedVersionId);
        var projection = await verify.Design.ActivityDependencyProjections.AsNoTracking().SingleAsync();
        Assert.Equal(1, projection.Sequence);
        Assert.Empty(projection.Items);
        var versions = await ((IActivityDefinitionManagementProjectionStore)verify.DesignStores)
            .ReadVersionsAsync("definition-1", new ActivityManagementProjectionPageQuery(null, null, 0, 10));
        Assert.Equal("version-1", Assert.Single(versions.Items).DefinitionVersionId);
        ReceiptAssert.Equivalent(commit.Receipt, await verify.Receipts.FindAsync(null, "publish-operation-1"));
    }

    [Fact]
    public async Task A_tenant_publishing_an_authorized_global_resource_keeps_the_resource_global()
    {
        var commit = Commit(operationTenant: "tenant-a");
        await using (var scope = Open("tenant-a"))
            await scope.Command.ExecuteAsync(commit);

        await using var verify = Open("tenant-a");
        Assert.Equal("tenant-a", (await verify.Receipts.FindAsync("tenant-a", "publish-operation-1"))!.TenantId);
        Assert.Null((await ((IActivityDefinitionVersionPublicationStore)verify.DesignStores).FindAsync("version-1"))!.TenantId);
        Assert.Null((await ((IActivityDefinitionDraftStore)verify.DesignStores).FindAsync("draft-1"))!.TenantId);
    }

    [Fact]
    public async Task A_crash_between_the_runtime_and_design_phases_leaves_only_runtime_material_and_a_retry_adopts_it()
    {
        var commit = Commit();
        var crash = new RefuseSaveInterceptor(context => context.ChangeTracker.Entries<ActivityDefinitionVersionPublication>().Any(entry => entry.State == EntityState.Added));
        await using (var crashed = Open(designInterceptors: [crash]))
            await Assert.ThrowsAsync<InjectedCrashException>(() => crashed.Command.ExecuteAsync(commit));
        Assert.Equal(1, crash.Refused);

        RuntimeRows before;
        await using (var inspect = Open())
        {
            before = await RuntimeRowsAsync(inspect);
            Assert.Null(await ((IActivityDefinitionVersionPublicationStore)inspect.DesignStores).FindAsync("version-1"));
            Assert.Empty(await inspect.Design.ActivityDefinitionVersions.AsNoTracking().ToListAsync());
            Assert.Empty(await inspect.Design.ActivityDependencyProjections.AsNoTracking().ToListAsync());
            Assert.Equal(ActivityDefinitionDraftStatus.Active, (await ((IActivityDefinitionDraftStore)inspect.DesignStores).FindAsync("draft-1"))!.Status);
            Assert.Null(await inspect.Receipts.FindAsync(null, "publish-operation-1"));
        }

        await using (var retry = Open())
            await retry.Command.ExecuteAsync(commit);

        await using var verify = Open();
        // Phase one is recognised as done, not rewritten: both rows keep their original incarnation and revision.
        Assert.Equal(before, await RuntimeRowsAsync(verify));
        Assert.NotNull(await ((IActivityDefinitionVersionPublicationStore)verify.DesignStores).FindAsync("version-1"));
        Assert.Equal(1, (await verify.Design.ActivityDependencyProjections.AsNoTracking().SingleAsync()).Sequence);
        ReceiptAssert.Equivalent(commit.Receipt, await verify.Receipts.FindAsync(null, "publish-operation-1"));
    }

    [Fact]
    public async Task A_crash_between_the_design_commit_and_the_receipt_resumes_at_the_receipt_and_a_completed_publication_is_not_replayed()
    {
        var commit = Commit();
        var crash = new RefuseSaveInterceptor(context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added));
        await using (var crashed = Open(publishingInterceptors: [crash]))
        {
            var failure = await Assert.ThrowsAsync<ActivityPublicationReceiptPendingException>(() => crashed.Command.ExecuteAsync(commit));
            Assert.IsType<InjectedCrashException>(failure.InnerException);
            Assert.Empty(crashed.Publishing.ChangeTracker.Entries());
        }

        RuntimeRows runtimeBefore;
        await using (var inspect = Open())
        {
            // The publication is done and observable; only its idempotency artifact is missing.
            Assert.Equal(ActivityDefinitionDraftStatus.Published, (await ((IActivityDefinitionDraftStore)inspect.DesignStores).FindAsync("draft-1"))!.Status);
            Assert.Null(await inspect.Receipts.FindAsync(null, "publish-operation-1"));
            runtimeBefore = await RuntimeRowsAsync(inspect);
        }

        await using (var retry = Open())
            Assert.Equal("version-1", (await retry.Command.ExecuteAsync(commit)).DefinitionVersionId);

        await using (var verify = Open())
        {
            ReceiptAssert.Equivalent(commit.Receipt, await verify.Receipts.FindAsync(null, "publish-operation-1"));
            Assert.Equal(runtimeBefore, await RuntimeRowsAsync(verify));
            Assert.Single(await verify.Design.ActivityDefinitionVersionPublications.AsNoTracking().ToListAsync());
            Assert.Equal(1, (await verify.Design.ActivityDependencyProjections.AsNoTracking().SingleAsync()).Sequence);
        }

        await using var replay = Open();
        await Assert.ThrowsAsync<InvalidOperationException>(() => replay.Command.ExecuteAsync(commit));
        Assert.Single(await replay.Publishing.ActivityPublicationReceipts.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_different_publication_reusing_an_interrupted_publications_version_id_is_a_conflict_not_a_resume()
    {
        var original = Commit();
        var crash = new RefuseSaveInterceptor(context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added));
        await using (var crashed = Open(publishingInterceptors: [crash]))
            await Assert.ThrowsAsync<ActivityPublicationReceiptPendingException>(() => crashed.Command.ExecuteAsync(original));

        var impostor = Commit(hashCharacter: 'b', idempotencyKey: "impostor-operation", sourceReferenceId: "source-ref-impostor");
        await using (var scope = Open())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Command.ExecuteAsync(impostor));

        await using var verify = Open();
        Assert.Null(await verify.Receipts.FindAsync(null, "impostor-operation"));
        Assert.Null(await verify.Templates.FindAsync(impostor.ExecutableTemplate.TemplateId));
        Assert.Equal(original.ExecutableTemplate.TemplateId, (await ((IActivityDefinitionVersionPublicationStore)verify.DesignStores).FindAsync("version-1"))!.TemplateId);
        // The genuine interrupted publication can still finish.
        await verify.Command.ExecuteAsync(original);
        Assert.NotNull(await verify.Receipts.FindAsync(null, "publish-operation-1"));
    }

    [Fact]
    public async Task A_source_reference_bound_to_another_artifact_is_a_conflict_and_nothing_is_designed()
    {
        await using (var seed = Open())
            await seed.SourceReferences.SaveAsync(Commit().SourceReference with { ArtifactId = "someone-elses-template" });

        await using (var scope = Open())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Command.ExecuteAsync(Commit()));

        await using var verify = Open();
        Assert.Null(await ((IActivityDefinitionVersionPublicationStore)verify.DesignStores).FindAsync("version-1"));
        Assert.Equal(ActivityDefinitionDraftStatus.Active, (await ((IActivityDefinitionDraftStore)verify.DesignStores).FindAsync("draft-1"))!.Status);
        Assert.Null(await verify.Receipts.FindAsync(null, "publish-operation-1"));
    }

    [Fact]
    public async Task A_stale_draft_is_refused_before_any_runtime_material_is_written()
    {
        var stale = Commit(draftRevision: 3);
        await using (var scope = Open())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Command.ExecuteAsync(stale));

        await using var verify = Open();
        Assert.Null(await verify.Templates.FindAsync(stale.ExecutableTemplate.TemplateId));
        Assert.Null(await verify.SourceReferences.FindAsync("source-ref-1"));
    }

    [Fact]
    public async Task A_racing_identical_receipt_is_success_and_a_different_one_is_a_conflict()
    {
        var commit = Commit();
        await using (var scope = Open(publishingInterceptors: [new InterleaveBeforeSaveInterceptor(async () =>
                     {
                         await using var other = Open();
                         Assert.True(await other.Receipts.TryCreateAsync(commit.Receipt));
                     })]))
            Assert.Equal("version-1", (await scope.Command.ExecuteAsync(commit)).DefinitionVersionId);

        await SeedAsync("definition-2", "draft-2");
        var second = Commit("definition-2", "draft-2", "version-2", hashCharacter: 'c', idempotencyKey: "second-operation", sourceReferenceId: "source-ref-2");
        await using (var scope = Open(publishingInterceptors: [new InterleaveBeforeSaveInterceptor(async () =>
                     {
                         await using var other = Open();
                         Assert.True(await other.Receipts.TryCreateAsync(second.Receipt with { Status = ActivityPublicationReceiptStatus.Rejected, Outcome = null }));
                     })]))
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Command.ExecuteAsync(second));
    }

    [Fact]
    public async Task Concurrent_attempts_of_one_publication_converge_on_one_committed_publication()
    {
        var commit = Commit();
        var rendezvous = new RendezvousAfterFirstQueryInterceptor.Rendezvous(4);
        var scopes = Enumerable.Range(0, 4).Select(_ => Open(publishingInterceptors: [new RendezvousAfterFirstQueryInterceptor(rendezvous)])).ToArray();
        try
        {
            var results = await Task.WhenAll(scopes.Select(scope => Task.Run(() => scope.Command.ExecuteAsync(commit))));
            Assert.All(results, result => Assert.Equal("version-1", result.DefinitionVersionId));
        }
        finally
        {
            foreach (var scope in scopes)
                await scope.DisposeAsync();
        }

        await using var verify = Open();
        Assert.Single(await verify.Design.ActivityDefinitionVersionPublications.AsNoTracking().ToListAsync());
        Assert.Single(await verify.Publishing.ActivityPublicationReceipts.AsNoTracking().ToListAsync());
        Assert.Equal(1, (await verify.Design.ActivityDependencyProjections.AsNoTracking().SingleAsync()).Sequence);
    }

    [Fact]
    public async Task An_attempt_overtaken_before_its_preflight_recognises_the_committed_publication_as_its_own()
    {
        var commit = Commit();
        // The racing attempt finishes the whole publication after this attempt found nothing committed, but
        // before this attempt's preflight reads the draft, which it then finds already published.
        var overtake = new BeforeFirstReadInterceptor("elsa_activity_definition_drafts", async () =>
        {
            await using var racing = Open();
            await racing.Command.ExecuteAsync(commit);
        });
        await using (var scope = Open(designInterceptors: [overtake]))
            Assert.Equal("version-1", (await scope.Command.ExecuteAsync(commit)).DefinitionVersionId);
        Assert.True(overtake.Fired);

        await using var verify = Open();
        Assert.Single(await verify.Design.ActivityDefinitionVersionPublications.AsNoTracking().ToListAsync());
        ReceiptAssert.Equivalent(commit.Receipt, await verify.Receipts.FindAsync(null, "publish-operation-1"));
    }

    [Fact]
    public async Task Replacing_a_drafts_projected_facts_leaves_another_tenants_draft_with_the_same_id_alone()
    {
        await SeedAsync("definition-tenant-a", "draft-shared", "tenant-a");
        var foreignOwner = new ActivityDefinitionReference("ActivityDraft", "definition-tenant-b", DraftId: "draft-shared", Revision: 1, TenantId: "tenant-b");
        var foreignDependency = new ActivityDefinitionReference("ActivityVersion", "definition-elsewhere", "version-elsewhere", "1.0.0", TenantId: "tenant-b");
        await using (var seed = design.Open<ActivitiesDesignSqliteDbContext>(options => new(options)))
        {
            var privileged = new TestAccess(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("projection-rebuild")));
            await ((IActivityDependencyProjectionRebuilder)new EfActivityDesignStores(seed, privileged)).RebuildAsync(new ActivityDependencyProjectionRebuild(
                "seed",
                1,
                Seeded,
                [new ActivityDependencyItem("foreign", foreignOwner, foreignDependency, new ActivityDependencyOccurrence("occurrence-foreign", []), true, 1, [foreignOwner, foreignDependency])]));
        }

        var commit = Commit("definition-tenant-a", "draft-shared", "version-tenant-a", hashCharacter: 'f', idempotencyKey: "tenant-operation",
            sourceReferenceId: "source-ref-tenant-a", operationTenant: "tenant-a", resourceTenant: "tenant-a");
        await using (var scope = Open("tenant-a"))
            await scope.Command.ExecuteAsync(commit);

        await using var verify = Open("tenant-a");
        var projection = await verify.Design.ActivityDependencyProjections.AsNoTracking().SingleAsync();
        Assert.Equal(2, projection.Sequence);
        var survivor = Assert.Single(projection.Items);
        Assert.Equal(("tenant-b", "draft-shared"), (survivor.Owner.TenantId, survivor.Owner.DraftId));
        Assert.Equal("tenant-a", (await ((IActivityDefinitionVersionPublicationStore)verify.DesignStores).FindAsync("version-tenant-a"))!.TenantId);
    }

    [Fact]
    public async Task A_dependency_edge_is_committed_and_replaces_the_drafts_projected_facts()
    {
        await SeedAsync("definition-dependency", "draft-dependency");
        var dependency = Commit("definition-dependency", "draft-dependency", "version-dependency", hashCharacter: 'd', idempotencyKey: "dependency-operation", sourceReferenceId: "source-ref-dependency");
        await using (var scope = Open())
            await scope.Command.ExecuteAsync(dependency);

        var dependent = Commit(dependency: new(
            "definition-dependency",
            "version-dependency",
            "1.0.0",
            dependency.ExecutableTemplate.TemplateId,
            dependency.ExecutableTemplate.TemplateHash,
            "occurrence-1"));
        await using (var scope = Open())
            await scope.Command.ExecuteAsync(dependent);

        await using var verify = Open();
        var edge = Assert.Single(await verify.DesignStores.ListOutboundAsync("version-1"));
        Assert.Equal("version-dependency", edge.DependencyVersionId);
        var projection = await verify.Design.ActivityDependencyProjections.AsNoTracking().SingleAsync();
        Assert.Equal(2, projection.Sequence);
        var item = Assert.Single(projection.Items);
        Assert.Equal(("ActivityVersion", "version-1"), (item.Owner.Kind, item.Owner.VersionId));
        Assert.Equal("version-dependency", item.Dependency.VersionId);
        Assert.DoesNotContain(projection.Items, projected => projected.Owner.Kind == "ActivityDraft");
    }

    [Fact]
    public async Task The_command_refuses_a_composition_whose_artifact_or_design_backend_is_not_ef()
    {
        await using var scope = Open();
        Assert.Throws<InvalidOperationException>(() => new EfActivityPublicationCommand(
            scope.Receipts, new ForeignTemplateStore(), scope.SourceReferences, scope.DesignStores, TestAccess.Scoped("default")));
        Assert.Throws<InvalidOperationException>(() => new EfActivityPublicationCommand(
            scope.Receipts, scope.Templates, scope.SourceReferences, new ForeignPublicationStore(), TestAccess.Scoped("default")));
    }

    [Fact]
    public async Task A_source_owned_publication_commits_runtime_first_and_a_retry_after_that_phase_converges()
    {
        var first = SourceCommit(Published);
        var crash = new RefuseSaveInterceptor(context => context.ChangeTracker.Entries<ActivityDefinitionVersionPublication>().Any(entry => entry.State == EntityState.Added));
        await using (var crashed = Open(designInterceptors: [crash]))
            await Assert.ThrowsAsync<InjectedCrashException>(() => crashed.SourceCommand.ExecuteAsync(first));

        await using (var inspect = Open())
        {
            Assert.NotNull(await inspect.SourceReferences.FindAsync(first.SourceReference.SourceReferenceId));
            Assert.Null(await ((IActivityDefinitionVersionPublicationStore)inspect.DesignStores).FindAsync("source-version-1"));
        }

        // The reconciler re-mints its timestamps on retry; the reference is still this publication's own.
        var retry = SourceCommit(Published.AddMinutes(1));
        await using (var scope = Open())
            await scope.SourceCommand.ExecuteAsync(retry);

        await using var verify = Open();
        Assert.NotNull(await ((IActivityDefinitionVersionPublicationStore)verify.DesignStores).FindAsync("source-version-1"));
        var authoring = await ((IActivityDefinitionAuthoringStore)verify.DesignStores).FindAsync("source-definition-1");
        Assert.Equal(ActivityContentAuthorityKind.ProviderSource, authoring!.ContentAuthority.Kind);
        Assert.Equal("source-version-1", authoring.HeadVersionId);
        Assert.Equal(Published, (await verify.SourceReferences.FindAsync(first.SourceReference.SourceReferenceId))!.CreatedAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => verify.SourceCommand.ExecuteAsync(retry));
    }

    private async Task SeedAsync(string definitionId, string draftId, string? tenantId = null)
    {
        await using var context = design.Open<ActivitiesDesignSqliteDbContext>(options => new(options));
        await ActivityPublicationTestMaterial.SeedDraftAsync(context, definitionId, draftId, tenantId);
    }

    private ActivityPublicationScope Open(
        string scope = "default",
        IInterceptor[]? designInterceptors = null,
        IInterceptor[]? publishingInterceptors = null) =>
        new(
            publishing.Open<PublishingSnapshotReviewSqliteDbContext>(options => new(options), publishingInterceptors ?? []),
            design.Open<ActivitiesDesignSqliteDbContext>(options => new(options), designInterceptors ?? []),
            runtime.Open<BookmarkStateSqliteDbContext>(options => new(options)),
            TestAccess.Scoped(scope));

    private static async Task<RuntimeRows> RuntimeRowsAsync(ActivityPublicationScope scope)
    {
        var template = await scope.Runtime.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        var reference = await scope.Runtime.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        return new(template.IncarnationId, template.Revision, reference.IncarnationId, reference.Revision);
    }

    private sealed record RuntimeRows(string TemplateIncarnation, long TemplateRevision, string ReferenceIncarnation, long ReferenceRevision);

    private sealed class ForeignTemplateStore :Elsa.Workflows.Runtime.Core.Contracts.IExecutableActivityTemplateStore
    {
        public ValueTask SaveAsync(ExecutableActivityTemplate template, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string templateId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ForeignPublicationStore : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
