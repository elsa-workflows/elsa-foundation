using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeArtifactScopeTests
{
    private static EfWorkflowExecutableSourceReferenceStore RootSources(Fixture owner, TimeProvider clock) =>
        new(owner.Context, new Accessor(PersistenceAccessContext.Scoped(new("tenant-a"))), FenceCodec(), clock);
    private static WorkflowExecutableSourceReference RootReference(WorkflowExecutable root, FakeTimeProvider clock) =>
        Reference("source", root.Identity.ArtifactId) with { DefinitionId = root.Identity.DefinitionId, DefinitionVersionId = root.Identity.DefinitionVersionId,
            ArtifactVersion = root.Identity.ArtifactVersion, CreatedAt = clock.GetUtcNow(), PublishedAt = clock.GetUtcNow(), Scope = WorkflowExecutableReferenceScope.TestRun,
            ExpiresAt = clock.GetUtcNow().AddHours(1) };
    private static WorkflowExecutable RootParent(WorkflowExecutable child)
    {
        var basis = Executable("root");
        return new(basis.Identity, basis.RootActivity, basis.ResumeTargets, basis.CreatedAt, basis.CompatibilityMetadata, null,
            [new(child.Identity.ArtifactId, child.Identity.ArtifactHash, [basis.RootActivity.ExecutableNodeId])], [], [], basis.IncidentStrategy);
    }
    private static async Task<IReadOnlyList<WorkflowExecutableRootWriteLease>> RootGrants(Fixture owner, FakeTimeProvider clock, params WorkflowExecutable[] material)
    {
        var grants = new List<WorkflowExecutableRootWriteLease>();
        foreach (var item in material) grants.Add((await owner.Executable.TryAcquireRootWriteLeaseAsync(item.Identity.ArtifactId, "invocation",
            clock.GetUtcNow().AddMinutes(1), clock.GetUtcNow()))!);
        return grants;
    }

    [Fact]
    public async Task EF_source_root_fence_creation_and_adoption_preserve_the_exact_persisted_winner()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a"); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sources = RootSources(owner, clock); var child = Executable("child"); var root = RootParent(child); await owner.Executable.SaveBatchAsync([root, child]);
        var grants = await RootGrants(owner, clock, root, child); var candidate = RootReference(root, clock);
        Assert.True(sources.CanWriteRootReferences(owner.Executable));
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(candidate, await sources.SaveWithRootWriteLeasesAsync(candidate, root.Identity, grants)));
        var before = await owner.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        var adopted = await sources.SaveWithRootWriteLeasesAsync(candidate with { CreatedAt = candidate.CreatedAt.AddSeconds(1), PublishedAt = candidate.PublishedAt!.Value.AddSeconds(1) }, root.Identity, grants);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(candidate, adopted));
        var after = await owner.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(before.ContentJson, after.ContentJson); Assert.Equal(before.IncarnationId, after.IncarnationId); Assert.Equal(before.Revision + 1, after.Revision);
        await using var fresh = database.Open("tenant-a"); Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(candidate, (await fresh.Store.FindAsync("source"))!));
    }

    [Fact]
    public async Task EF_source_root_fence_preserves_selected_publication_attribution_independent_of_the_content_producer()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a"); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var root = Executable("producer-a"); await owner.Executable.SaveAsync(root); var grants = await RootGrants(owner, clock, root);
        var publication = RootReference(root, clock) with { SourceId = "publication-b", SourceVersion = "2", DefinitionId = "definition-b", DefinitionVersionId = "version-b", ArtifactVersion = "published-2" };
        var sources = RootSources(owner, clock);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(publication, await sources.SaveWithRootWriteLeasesAsync(publication, root.Identity, grants)));
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(publication, await sources.SaveWithRootWriteLeasesAsync(publication with { CreatedAt = publication.CreatedAt.AddSeconds(1) }, root.Identity, grants)));
        await using var fresh = database.Open("tenant-a"); Assert.Equal(root.Identity, (await fresh.Executable.FindAsync(root.Identity.ArtifactId))!.Identity);
        Assert.NotEqual(root.Identity.DefinitionVersionId, (await fresh.Store.FindAsync("source"))!.DefinitionVersionId);
    }

    [Fact]
    public async Task EF_source_root_fence_cannot_publish_a_reference_to_unleased_material()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a"); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var root = Executable("root"); await owner.Executable.SaveAsync(root); var grants = await RootGrants(owner, clock, root);
        await Assert.ThrowsAsync<ArgumentException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(RootReference(root, clock) with { ArtifactId = "unleased" }, root.Identity, grants).AsTask());
        Assert.Null(await owner.Store.FindAsync("source"));
    }

    [Fact]
    public async Task EF_source_root_fence_capability_requires_the_same_context_and_current_scope_and_keeps_original_constructor()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a"); await using var other = database.Open("tenant-a");
        var sources = RootSources(owner, TimeProvider.System);
        Assert.True(sources.CanWriteRootReferences(owner.Executable)); Assert.False(sources.CanWriteRootReferences(other.Executable));
        Assert.False(sources.CanWriteRootReferences(new EfWorkflowExecutableStore(owner.Context, new Accessor(PersistenceAccessContext.Scoped(new("tenant-b"))))));
        Assert.NotNull(typeof(EfWorkflowExecutableSourceReferenceStore).GetConstructor([typeof(RuntimeDbContext), typeof(IPersistenceAccessContextAccessor), typeof(IRuntimeRecoveryContinuationCodec)]));
    }

    [Theory]
    [InlineData("missing")] [InlineData("extra")] [InlineData("duplicate")] [InlineData("owner")]
    [InlineData("token")] [InlineData("expired")] [InlineData("released")] [InlineData("recreated")]
    public async Task EF_source_root_fence_refuses_incomplete_or_noncurrent_native_grants(string defect)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a"); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sources = RootSources(owner, clock); var child = Executable("child"); var root = RootParent(child); var other = Executable("other");
        await owner.Executable.SaveBatchAsync([root, child, other]); var grants = await RootGrants(owner, clock, root, child);
        var candidate = RootReference(root, clock);
        switch (defect)
        {
            case "missing": grants = [grants[0]]; break;
            case "extra": grants = [.. grants, .. await RootGrants(owner, clock, other)]; break;
            case "duplicate": grants = [grants[0], grants[0]]; break;
            case "owner": grants = [grants[0] with { LeaseId = "other-owner" }, grants[1]]; break;
            case "token": grants = [grants[0] with { ConcurrencyToken = "wrong" }, grants[1]]; break;
            case "expired": clock.Advance(TimeSpan.FromMinutes(2)); break;
            case "released": await owner.Executable.ReleaseRootWriteLeaseAsync(grants[0]); break;
            case "recreated":
                foreach (var grant in grants) await owner.Executable.ReleaseRootWriteLeaseAsync(grant);
                Assert.True(await owner.Executable.DeleteAsync(child.Identity.ArtifactId)); await owner.Executable.SaveAsync(child); break;
        }
        var failure = await Record.ExceptionAsync(() => sources.SaveWithRootWriteLeasesAsync(candidate, root.Identity, grants).AsTask());
        if (defect is "missing" or "extra" or "duplicate") Assert.IsAssignableFrom<ArgumentException>(failure);
        else Assert.IsType<WorkflowExecutableRootWriteLeaseLostException>(failure);
        await using var fresh = database.Open("tenant-a"); Assert.Null(await fresh.Store.FindAsync("source"));
    }

    [Theory]
    [InlineData("artifact")] [InlineData("definition")] [InlineData("version")] [InlineData("artifact-version")] [InlineData("hash")]
    public async Task EF_source_root_fence_requires_the_full_physical_root_pin(string defect)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a"); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var root = Executable("root"); await owner.Executable.SaveAsync(root); var grants = await RootGrants(owner, clock, root);
        var pin = defect switch
        {
            "artifact" => root.Identity with { ArtifactId = "other" }, "definition" => root.Identity with { DefinitionId = "other" },
            "version" => root.Identity with { DefinitionVersionId = "other" }, "artifact-version" => root.Identity with { ArtifactVersion = "other" },
            _ => root.Identity with { ArtifactHash = "other" }
        };
        var candidate = RootReference(root, clock) with { ArtifactId = pin.ArtifactId, DefinitionId = pin.DefinitionId, DefinitionVersionId = pin.DefinitionVersionId, ArtifactVersion = pin.ArtifactVersion };
        await Assert.ThrowsAsync<InvalidOperationException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(candidate, pin, grants).AsTask());
        Assert.Null(await owner.Store.FindAsync("source"));
    }

    [Theory]
    [InlineData("retired")] [InlineData("expired")] [InlineData("source")] [InlineData("expiry")] [InlineData("layout")] [InlineData("input")]
    public async Task EF_source_root_fence_refuses_changed_or_nonlive_winning_source_payload(string defect)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a"); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var root = Executable("root"); await owner.Executable.SaveAsync(root); var grants = await RootGrants(owner, clock, root); var candidate = RootReference(root, clock);
        var existing = defect switch
        {
            "retired" => candidate.Retire(clock.GetUtcNow(), "retired"), "expired" => candidate with { ExpiresAt = clock.GetUtcNow() },
            "source" => candidate with { SourceId = "other" }, "expiry" => candidate with { ExpiresAt = clock.GetUtcNow().AddHours(2) },
            "layout" => candidate with { Layout = [new("other", 1, 2)] },
            _ => candidate with { AuthoredInputs = [new("node", "input", "expression", System.Text.Json.JsonSerializer.SerializeToElement("opaque"))] }
        };
        await owner.Store.SaveAsync(existing);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(candidate, root.Identity, grants).AsTask());
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(existing, (await owner.Store.FindAsync("source"))!));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_source_root_fence_refuses_nonlive_proposals(bool retired)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a"); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var root = Executable("root"); await owner.Executable.SaveAsync(root); var grants = await RootGrants(owner, clock, root);
        var candidate = retired ? RootReference(root, clock).Retire(clock.GetUtcNow(), "retired") : RootReference(root, clock) with { ExpiresAt = clock.GetUtcNow() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(candidate, root.Identity, grants).AsTask());
        Assert.Null(await owner.Store.FindAsync("source"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_source_root_fence_loses_to_a_real_peer_collector_after_discovery_before_its_transaction(bool delete)
    {
        await using var database = await Database.CreateFileAsync(); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow); var root = Executable("root");
        IReadOnlyList<WorkflowExecutableRootWriteLease> grants;
        await using (var seed = database.Open("tenant-a")) { await seed.Executable.SaveAsync(root); grants = await RootGrants(seed, clock, root); }
        WorkflowExecutableDeletionGuard? ownedGuard = null;
        var peer = new BeforeTemplateMaintenanceTransaction(async () =>
        {
            clock.Advance(TimeSpan.FromMinutes(2)); await using var collector = database.Open("tenant-a");
            ownedGuard = await collector.Executable.TryBeginDeletionAsync(root.Identity.ArtifactId, "actual-peer", clock.GetUtcNow().AddMinutes(1), clock.GetUtcNow());
            Assert.NotNull(ownedGuard);
            if (delete) Assert.True(await collector.Executable.DeleteAsync(ownedGuard!, clock.GetUtcNow()));
        });
        var candidate = RootReference(root, clock);
        await using (var owner = database.Open("tenant-a", peer))
            await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseLostException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(candidate, root.Identity, grants).AsTask());
        Assert.True(peer.Ran); await using var fresh = database.Open("tenant-a"); Assert.Null(await fresh.Store.FindAsync("source"));
        if (delete) Assert.Null(await fresh.Executable.FindAsync(root.Identity.ArtifactId));
        else
        {
            Assert.Null(await fresh.Executable.TryAcquireRootWriteLeaseAsync(root.Identity.ArtifactId, "intruder", clock.GetUtcNow().AddMinutes(1), clock.GetUtcNow()));
            Assert.True(await fresh.Executable.CancelDeletionAsync(ownedGuard!));
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_source_root_fence_does_not_adopt_a_source_changed_by_a_real_peer_before_its_transaction(bool create)
    {
        await using var database = await Database.CreateFileAsync(); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow); var root = Executable("root"); var candidate = RootReference(root, clock);
        IReadOnlyList<WorkflowExecutableRootWriteLease> grants;
        await using (var seed = database.Open("tenant-a"))
        { await seed.Executable.SaveAsync(root); grants = await RootGrants(seed, clock, root); if (!create) await seed.Store.SaveAsync(candidate); }
        WorkflowExecutableSourceReference? winner = null;
        var peer = new BeforeTemplateMaintenanceTransaction(async () =>
        {
            await using var writer = database.Open("tenant-a");
            winner = create ? candidate with { SourceId = "other" } : candidate.Retire(clock.GetUtcNow(), "retired");
            if (create) await writer.Store.SaveAsync(winner); else Assert.True(await writer.Store.TryRetireAsync(candidate, winner));
        });
        await using (var owner = database.Open("tenant-a", peer))
        {
            var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(candidate, root.Identity, grants).AsTask());
            Assert.NotNull(EfRelationalExceptionClassifier.FindSaveFailure(failure));
        }
        Assert.True(peer.Ran); await using var fresh = database.Open("tenant-a");
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(winner!, (await fresh.Store.FindAsync("source"))!));
        Assert.True(await fresh.Executable.RenewRootWriteLeaseAsync(grants[0], clock.GetUtcNow().AddMinutes(1), clock.GetUtcNow()));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_source_root_fence_preserves_caller_writes_or_local_transaction(bool dirty)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a"); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var root = Executable("root"); await owner.Executable.SaveAsync(root); var grants = await RootGrants(owner, clock, root);
        var tracked = await owner.Context.WorkflowExecutables.SingleAsync();
        if (dirty)
        {
            tracked.ContentJson += " ";
            await Assert.ThrowsAsync<InvalidOperationException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(RootReference(root, clock), root.Identity, grants).AsTask());
            Assert.True(owner.Context.ChangeTracker.HasChanges());
        }
        else
        {
            await using var transaction = await owner.Context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(RootReference(root, clock), root.Identity, grants).AsTask());
            Assert.Same(transaction, owner.Context.Database.CurrentTransaction); Assert.Equal(EntityState.Unchanged, owner.Context.Entry(tracked).State);
        }
        await using var fresh = database.Open("tenant-a"); Assert.Null(await fresh.Store.FindAsync("source"));
    }

    [Fact]
    public async Task EF_source_root_fence_refuses_ambient_transaction_before_discarding_tracked_reads()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a"); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var root = Executable("root"); await owner.Executable.SaveAsync(root); var grants = await RootGrants(owner, clock, root); var tracked = await owner.Context.WorkflowExecutables.SingleAsync();
        using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeOption.RequiresNew, System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            var transaction = System.Transactions.Transaction.Current;
            await Assert.ThrowsAsync<InvalidOperationException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(RootReference(root, clock), root.Identity, grants).AsTask());
            Assert.Same(transaction, System.Transactions.Transaction.Current); Assert.Equal(EntityState.Unchanged, owner.Context.Entry(tracked).State);
        }
        Assert.Null(await owner.Store.FindAsync("source"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_source_root_fence_contextualizes_IO_or_unrequested_cancellation(bool cancellation)
    {
        await using var database = await Database.CreateAsync(); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow); var root = Executable("root"); IReadOnlyList<WorkflowExecutableRootWriteLease> grants;
        await using (var seed = database.Open("tenant-a")) { await seed.Executable.SaveAsync(root); grants = await RootGrants(seed, clock, root); }
        Exception cause = cancellation ? new OperationCanceledException("Unrequested provider cancellation") : new IOException("Provider read failure");
        await using (var owner = database.Open("tenant-a", new MaintenanceReadFailure(cause)))
        {
            var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(RootReference(root, clock), root.Identity, grants).AsTask());
            Assert.Same(cause, failure.InnerException); Assert.Equal("publishing source", failure.Operation); Assert.Equal("source", failure.Identity);
        }
        await using var fresh = database.Open("tenant-a"); Assert.Null(await fresh.Store.FindAsync("source"));
    }

    [Fact]
    public async Task EF_source_root_fence_propagates_requested_storage_cancellation()
    {
        await using var database = await Database.CreateAsync(); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow); var root = Executable("root"); IReadOnlyList<WorkflowExecutableRootWriteLease> grants;
        await using (var seed = database.Open("tenant-a")) { await seed.Executable.SaveAsync(root); grants = await RootGrants(seed, clock, root); }
        using var stop = new CancellationTokenSource();
        await using (var owner = database.Open("tenant-a", new AtomicReadCancellation(stop)))
        {
            var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(RootReference(root, clock), root.Identity, grants, stop.Token).AsTask());
            Assert.Equal(stop.Token, failure.CancellationToken);
        }
        await using var fresh = database.Open("tenant-a"); Assert.Null(await fresh.Store.FindAsync("source"));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_source_root_fence_rolls_back_actual_SQL_and_coordination_on_precommit_provider_failure(bool adopt)
    {
        await using var database = await Database.CreateAsync(); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow); var root = Executable("root"); var candidate = RootReference(root, clock);
        IReadOnlyList<WorkflowExecutableRootWriteLease> grants; long revision; string? payload = null;
        await using (var seed = database.Open("tenant-a"))
        {
            await seed.Executable.SaveAsync(root); grants = await RootGrants(seed, clock, root);
            if (adopt) { await seed.Store.SaveAsync(candidate); payload = (await seed.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync()).ContentJson; }
            revision = (await seed.Context.WorkflowExecutableCoordinations.AsNoTracking().SingleAsync()).Revision;
        }
        var probe = new SourceRootSqlFailure();
        await using (var owner = database.Open("tenant-a", probe))
        {
            var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(candidate, root.Identity, grants).AsTask());
            Assert.Same(probe.Cause, failure.InnerException); Assert.Equal("publishing source", failure.Operation); Assert.Equal("source", failure.Identity);
        }
        Assert.True(probe.SawDurableRowsInsideTransaction);
        await using var fresh = database.Open("tenant-a"); Assert.Equal(revision, (await fresh.Context.WorkflowExecutableCoordinations.AsNoTracking().SingleAsync()).Revision);
        if (adopt) Assert.Equal(payload, (await fresh.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync()).ContentJson);
        else Assert.Null(await fresh.Store.FindAsync("source"));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task EF_source_root_fence_refuses_source_or_grant_expiry_during_actual_SaveChanges(bool leaseExpires, bool afterSql)
    {
        await using var database = await Database.CreateAsync(); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow); var root = Executable("root");
        WorkflowExecutableRootWriteLease grant;
        await using (var seed = database.Open("tenant-a"))
        {
            await seed.Executable.SaveAsync(root);
            grant = (await seed.Executable.TryAcquireRootWriteLeaseAsync(root.Identity.ArtifactId, "invocation", clock.GetUtcNow().AddMinutes(leaseExpires ? 1 : 10), clock.GetUtcNow()))!;
        }
        var candidate = RootReference(root, clock) with { ExpiresAt = clock.GetUtcNow().AddMinutes(leaseExpires ? 10 : 1) };
        IInterceptor expiry = afterSql ? new AtomicClockAfterSql(clock) : new AtomicClockBeforeSql(clock);
        await using (var owner = database.Open("tenant-a", expiry))
        {
            if (leaseExpires) await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseLostException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(candidate, root.Identity, [grant]).AsTask());
            else await Assert.ThrowsAsync<InvalidOperationException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(candidate, root.Identity, [grant]).AsTask());
        }
        await using var fresh = database.Open("tenant-a"); Assert.Null(await fresh.Store.FindAsync("source"));
    }

    [Fact]
    public async Task EF_source_root_fence_actual_postcommit_ACK_loss_is_retried_from_fresh_context_without_changing_source_payload()
    {
        await using var database = await Database.CreateAsync(); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow); var root = Executable("root"); var candidate = RootReference(root, clock);
        IReadOnlyList<WorkflowExecutableRootWriteLease> grants;
        await using (var seed = database.Open("tenant-a")) { await seed.Executable.SaveAsync(root); grants = await RootGrants(seed, clock, root); }
        var loss = new AtomicCommitAckLoss();
        await using (var owner = database.Open("tenant-a", loss))
        {
            var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(candidate, root.Identity, grants).AsTask());
            Assert.Same(loss.Cause, failure.InnerException); Assert.Equal("publishing source", failure.Operation);
        }
        Assert.True(loss.Committed); clock.Advance(TimeSpan.FromSeconds(10));
        await using var fresh = database.Open("tenant-a"); var before = await fresh.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        var winner = await RootSources(fresh, clock).SaveWithRootWriteLeasesAsync(candidate with { CreatedAt = clock.GetUtcNow(), PublishedAt = clock.GetUtcNow() }, root.Identity, grants);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(candidate, winner));
        var after = await fresh.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(before.ContentJson, after.ContentJson); Assert.Equal(before.IncarnationId, after.IncarnationId); Assert.Equal(before.Revision + 1, after.Revision);
    }

    [Fact]
    public async Task EF_source_root_fence_captures_scope_before_awaits_and_refreshes_it_for_the_next_operation()
    {
        await using var database = await Database.CreateAsync(); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow); var root = Executable("root");
        IReadOnlyList<WorkflowExecutableRootWriteLease> firstGrants, secondGrants;
        await using (var first = database.Open("tenant-a")) { await first.Executable.SaveAsync(root); firstGrants = await RootGrants(first, clock, root); }
        await using (var second = database.Open("tenant-b")) { await second.Executable.SaveAsync(root); secondGrants = await RootGrants(second, clock, root); }
        var access = new MovableFenceAccess(); var pivot = new SourceRootReadScopePivot(() => access.Current = PersistenceAccessContext.Scoped(new("tenant-b")));
        await using (var owner = database.Open("tenant-a", pivot))
        {
            var sources = new EfWorkflowExecutableSourceReferenceStore(owner.Context, access, FenceCodec(), clock);
            await sources.SaveWithRootWriteLeasesAsync(RootReference(root, clock) with { TenantId = "tenant-a" }, root.Identity, firstGrants);
            Assert.True(pivot.Ran);
            await sources.SaveWithRootWriteLeasesAsync(RootReference(root, clock) with { SourceReferenceId = "next", TenantId = "tenant-b" }, root.Identity, secondGrants);
        }
        await using var firstVerify = database.Open("tenant-a"); await using var secondVerify = database.Open("tenant-b");
        Assert.NotNull(await firstVerify.Store.FindAsync("source")); Assert.Null(await secondVerify.Store.FindAsync("source"));
        Assert.Null(await firstVerify.Store.FindAsync("next")); Assert.NotNull(await secondVerify.Store.FindAsync("next"));
    }

    [Fact]
    public async Task EF_source_root_fence_cannot_use_another_scopes_valid_material_grant()
    {
        await using var database = await Database.CreateAsync(); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow); var root = Executable("root");
        await using var first = database.Open("tenant-a"); await using var second = database.Open("tenant-b");
        await first.Executable.SaveAsync(root); await second.Executable.SaveAsync(root); var wrong = await RootGrants(second, clock, root);
        await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseLostException>(() => RootSources(first, clock).SaveWithRootWriteLeasesAsync(RootReference(root, clock), root.Identity, wrong).AsTask());
        Assert.Null(await first.Store.FindAsync("source")); Assert.Null(await second.Store.FindAsync("source"));
    }

    [Theory]
    [InlineData("workflow-scope")] [InlineData("workflow-scope-hash")]
    [InlineData("source-scope")] [InlineData("source-scope-hash")] [InlineData("source-id-hash")] [InlineData("source-artifact-hash")]
    public async Task EF_source_root_fence_cannot_turn_corrupt_physical_projections_into_absence(string defect)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a"); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var root = Executable("root"); await owner.Executable.SaveAsync(root); var grants = await RootGrants(owner, clock, root); var candidate = RootReference(root, clock);
        if (defect.StartsWith("workflow", StringComparison.Ordinal))
        {
            var row = await owner.Context.WorkflowExecutables.SingleAsync();
            if (defect == "workflow-scope") row.ScopeKey = EfRelationalIdentity.Encode("foreign"); else row.ScopeKeyHash = "foreign";
        }
        else
        {
            await owner.Store.SaveAsync(candidate); var row = await owner.Context.WorkflowExecutableSourceReferences.SingleAsync();
            switch (defect)
            {
                case "source-scope": row.ScopeKey = EfRelationalIdentity.Encode("foreign"); break;
                case "source-scope-hash": row.ScopeKeyHash = "foreign"; break;
                case "source-id-hash": row.SourceReferenceIdHash = "foreign"; break;
                default: row.ArtifactIdHash = "foreign"; break;
            }
        }
        await owner.Context.SaveChangesAsync(); owner.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(candidate, root.Identity, grants).AsTask());
        if (defect.StartsWith("workflow", StringComparison.Ordinal)) Assert.Empty(await owner.Context.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync());
        else Assert.Single(await owner.Context.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task EF_source_root_fence_refuses_before_SaveChanges_when_native_fence_reads_exhaust_source_lifetime()
    {
        await using var database = await Database.CreateAsync(); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow); var root = Executable("root");
        WorkflowExecutableRootWriteLease grant;
        await using (var seed = database.Open("tenant-a"))
        {
            await seed.Executable.SaveAsync(root);
            grant = (await seed.Executable.TryAcquireRootWriteLeaseAsync(root.Identity.ArtifactId, "invocation", clock.GetUtcNow().AddMinutes(10), clock.GetUtcNow()))!;
        }
        var candidate = RootReference(root, clock) with { ExpiresAt = clock.GetUtcNow().AddMinutes(1) };
        var delay = new SourceRootClockOnLeaseRead(clock); var saves = new AtomicSaveCounter();
        await using (var owner = database.Open("tenant-a", delay, saves))
            await Assert.ThrowsAsync<InvalidOperationException>(() => RootSources(owner, clock).SaveWithRootWriteLeasesAsync(candidate, root.Identity, [grant]).AsTask());
        Assert.True(delay.Advanced); Assert.Equal(0, saves.Count);
        await using var fresh = database.Open("tenant-a"); Assert.Null(await fresh.Store.FindAsync("source"));
    }

    private sealed class SourceRootClockOnLeaseRead(FakeTimeProvider clock) : DbCommandInterceptor
    {
        public bool Advanced { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Advanced && command.CommandText.Contains(RuntimeArtifactEfModule.WorkflowExecutableRootWriteLeaseTableName, StringComparison.Ordinal))
            { Advanced = true; clock.Advance(TimeSpan.FromMinutes(2)); }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SourceRootSqlFailure : SaveChangesInterceptor
    {
        public bool SawDurableRowsInsideTransaction { get; private set; }
        public Microsoft.Data.Sqlite.SqliteException? Cause { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var owner = (RuntimeDbContext)eventData.Context!;
            SawDurableRowsInsideTransaction = await owner.WorkflowExecutables.AnyAsync(cancellationToken) && await owner.WorkflowExecutableSourceReferences.AnyAsync(cancellationToken);
            try { await owner.Database.ExecuteSqlRawAsync("SELECT * FROM elsa_test_missing_source_root_failure", cancellationToken); }
            catch (Microsoft.Data.Sqlite.SqliteException failure) { Cause = failure; throw; }
            throw new InvalidOperationException("The provider failure control did not fail.");
        }
    }
    private sealed class SourceRootReadScopePivot(Action pivot) : DbCommandInterceptor
    {
        public bool Ran { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        { if (!Ran) { Ran = true; pivot(); } return ValueTask.FromResult(result); }
    }

}
