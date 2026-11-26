using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeArtifactScopeTests
{
    private static EfExecutableActivityTemplateStore AtomicTemplates(Fixture owner, TimeProvider? clock = null) =>
        new(owner.Context, new Accessor(PersistenceAccessContext.Scoped(new("tenant-a"))), FenceCodec(), clock);

    [Fact]
    public void EF_atomic_template_publication_preserves_the_original_binary_constructor()
    {
        Assert.NotNull(typeof(EfExecutableActivityTemplateStore).GetConstructor([
            typeof(RuntimeDbContext), typeof(IPersistenceAccessContextAccessor), typeof(IRuntimeRecoveryContinuationCodec)]));
    }

    [Fact]
    public async Task EF_atomic_template_publication_returns_the_persisted_winner_without_rewriting_its_payload()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var templates = AtomicTemplates(owner); var template = Template("template", "template-hash"); var source = Reference("source", template.TemplateId);
        var winner = await templates.SaveWithReferenceAsync(template, source);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(source, winner));
        var before = await owner.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        var retry = source with { CreatedAt = source.CreatedAt.AddMinutes(1), PublishedAt = source.PublishedAt!.Value.AddMinutes(1) };
        var adopted = await templates.SaveWithReferenceAsync(template, retry);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(source, adopted));
        var after = await owner.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(before.ContentJson, after.ContentJson); Assert.Equal(before.IncarnationId, after.IncarnationId); Assert.Equal(before.Revision + 1, after.Revision);
        Assert.False(await templates.TryDeleteUnreferencedAsync((await templates.FindAsync(template.TemplateId))!));
        await using var fresh = database.Open("tenant-a");
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(adopted, (await fresh.Store.FindAsync(source.SourceReferenceId))!));
    }

    [Theory]
    [InlineData("retired")] [InlineData("expired")] [InlineData("source")] [InlineData("expiry")]
    [InlineData("activation")] [InlineData("slot")] [InlineData("tenant")] [InlineData("scope")]
    [InlineData("layout")] [InlineData("input")] [InlineData("presentation")]
    public async Task EF_atomic_template_publication_refuses_a_conflicting_winner_before_material_is_committed(string defect)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash"); var proposed = Reference("source", template.TemplateId);
        var existing = defect switch
        {
            "retired" => proposed.Retire(DateTimeOffset.UtcNow, "retired"),
            "expired" => proposed with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) },
            "source" => proposed with { SourceId = "other" },
            "expiry" => proposed with { ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) },
            "activation" => proposed with { ActivationId = "other" },
            "slot" => proposed with { SlotId = "other" },
            "tenant" => proposed with { TenantId = "tenant-a" },
            "layout" => proposed with { Layout = [new("node", 1, 2)] },
            "input" => proposed with { AuthoredInputs = [new("node", "input", "expression", JsonSerializer.SerializeToElement("value"))] },
            "presentation" => proposed with { ActivityPresentation = [new("node", "display")] },
            _ => proposed with { Scope = WorkflowExecutableReferenceScope.TestRun }
        };
        await owner.Store.SaveAsync(existing);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(template, proposed).AsTask());
        Assert.Null(await owner.Template.FindAsync(template.TemplateId)); Assert.Null(await owner.Template.FindByHashAsync(template.TemplateHash));
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(existing, (await owner.Store.FindAsync("source"))!));
    }

    [Theory]
    [InlineData("")] [InlineData(" ")] [InlineData("\t")]
    public async Task EF_atomic_template_publication_refuses_invalid_source_identity_without_partial_material(string id)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash");
        await Assert.ThrowsAnyAsync<ArgumentException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(template, Reference(id, template.TemplateId)).AsTask());
        Assert.Null(await owner.Template.FindAsync(template.TemplateId)); Assert.Empty(await owner.Context.ExecutableActivityTemplateHashClaims.ToArrayAsync());
        Assert.Empty(await owner.Store.ListAllAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_atomic_template_publication_refuses_nonlive_proposals_without_any_partial_write(bool retired)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash"); var source = Reference("source", template.TemplateId);
        source = retired ? source.Retire(DateTimeOffset.UtcNow, "retired") : source with { ExpiresAt = DateTimeOffset.UtcNow };
        await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(template, source).AsTask());
        Assert.Null(await owner.Template.FindAsync(template.TemplateId)); Assert.Null(await owner.Store.FindAsync("source"));
    }

    [Theory]
    [InlineData("missing")] [InlineData("different-hash")]
    public async Task EF_atomic_template_publication_requires_exact_dependencies_before_committing_a_root(string defect)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var leaf = Template("leaf", "leaf-hash"); var parent = TemplateParent(leaf, "closed");
        if (defect == "different-hash") await owner.Template.SaveAsync(Template("leaf", "other-hash"));
        await Assert.ThrowsAsync<InvalidDataException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(parent, Reference("source", parent.TemplateId)).AsTask());
        Assert.Null(await owner.Template.FindAsync(parent.TemplateId)); Assert.Null(await owner.Store.FindAsync("source"));
    }

    [Fact]
    public async Task EF_atomic_template_publication_can_recreate_collected_material_with_its_live_root()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a"); var templates = AtomicTemplates(owner);
        var template = Template("template", "template-hash"); await templates.SaveAsync(template);
        var stale = (await templates.FindAsync(template.TemplateId))!;
        Assert.True(await templates.TryDeleteUnreferencedAsync(stale));
        await templates.SaveWithReferenceAsync(template, Reference("source", template.TemplateId));
        Assert.False(await templates.TryDeleteUnreferencedAsync(stale));
        Assert.False(await templates.TryDeleteUnreferencedAsync((await templates.FindAsync(template.TemplateId))!));
        await using var fresh = database.Open("tenant-a"); Assert.NotNull(await fresh.Store.FindAsync("source")); Assert.NotNull(await fresh.Template.FindAsync(template.TemplateId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_atomic_template_publication_after_collector_discovery_defeats_actual_conditional_deletion(bool adopt)
    {
        await using var database = await Database.CreateFileAsync(); var template = Template("template", "template-hash");
        var source = Reference("source", template.TemplateId); ExecutableActivityTemplate observed;
        var doomed = Reference("old-source", template.TemplateId).Retire(DateTimeOffset.UtcNow, "retired");
        await using (var seed = database.Open("tenant-a"))
        {
            await seed.Template.SaveAsync(template); observed = (await seed.Template.FindAsync(template.TemplateId))!;
            if (adopt) { await seed.Store.SaveAsync(source); await seed.Store.SaveAsync(doomed); }
        }
        var peer = new BeforeTemplateMaintenanceTransaction(async () =>
        {
            await using var writer = database.Open("tenant-a");
            var winner = await AtomicTemplates(writer).SaveWithReferenceAsync(template, source);
            Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(source, winner));
        });
        await using (var collector = database.Open("tenant-a", peer))
            Assert.False(adopt
                ? await collector.Template.TryDeleteDoomedReferenceAsync(doomed, DateTimeOffset.UtcNow)
                : await collector.Template.TryDeleteUnreferencedAsync(observed));
        Assert.True(peer.Ran);
        await using var fresh = database.Open("tenant-a"); Assert.NotNull(await fresh.Template.FindAsync(template.TemplateId)); Assert.NotNull(await fresh.Store.FindAsync("source"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_atomic_template_publication_rolls_back_if_a_reference_expires_during_awaited_storage(bool afterSql)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow); var source = Reference("source", "template") with { ExpiresAt = clock.GetUtcNow().AddMinutes(1) };
        await using var database = await Database.CreateAsync();
        IInterceptor delay = afterSql ? new AtomicClockAfterSql(clock) : new AtomicClockBeforeSql(clock);
        await using (var owner = database.Open("tenant-a", delay))
            await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicTemplates(owner, clock).SaveWithReferenceAsync(Template("template", "template-hash"), source).AsTask());
        await using var fresh = database.Open("tenant-a"); Assert.Null(await fresh.Template.FindAsync("template")); Assert.Null(await fresh.Store.FindAsync("source"));
    }

    [Fact]
    public async Task EF_atomic_template_publication_postcommit_ACK_loss_is_recoverable_from_a_fresh_context()
    {
        await using var database = await Database.CreateAsync(); var template = Template("template", "template-hash"); var source = Reference("source", template.TemplateId);
        var loss = new AtomicCommitAckLoss();
        await using (var owner = database.Open("tenant-a", loss))
        {
            var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(template, source).AsTask());
            Assert.Same(loss.Cause, failure.InnerException); Assert.Equal("publishing", failure.Operation);
        }
        Assert.True(loss.Committed);
        await using var fresh = database.Open("tenant-a");
        var winner = await AtomicTemplates(fresh).SaveWithReferenceAsync(template, source with { CreatedAt = source.CreatedAt.AddMinutes(1) });
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(source, winner));
        Assert.Single(await fresh.Context.ExecutableActivityTemplates.ToArrayAsync()); Assert.Single(await fresh.Context.WorkflowExecutableSourceReferences.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_atomic_template_publication_preserves_caller_pending_writes_or_local_transaction(bool dirty)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var seed = Template("seed", "seed-hash"); await owner.Template.SaveAsync(seed);
        var template = Template("template", "template-hash");
        if (dirty)
        {
            (await owner.Context.ExecutableActivityTemplates.SingleAsync()).Revision++;
            await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(template, Reference("source", template.TemplateId)).AsTask());
            Assert.True(owner.Context.ChangeTracker.HasChanges());
        }
        else
        {
            await using var transaction = await owner.Context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(template, Reference("source", template.TemplateId)).AsTask());
            Assert.Same(transaction, owner.Context.Database.CurrentTransaction);
        }
        await using var fresh = database.Open("tenant-a"); Assert.Null(await fresh.Template.FindAsync(template.TemplateId)); Assert.Null(await fresh.Store.FindAsync("source"));
    }

    [Theory]
    [InlineData("scope")] [InlineData("scope-hash")] [InlineData("source-hash")] [InlineData("artifact-hash")]
    public async Task EF_atomic_template_publication_does_not_treat_a_corrupt_physical_source_as_absent(string corruption)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash"); var source = Reference("source", template.TemplateId); await owner.Store.SaveAsync(source);
        var row = await owner.Context.WorkflowExecutableSourceReferences.SingleAsync();
        switch (corruption)
        {
            case "scope": row.ScopeKey = EfRelationalIdentity.Encode("foreign"); break;
            case "scope-hash": row.ScopeKeyHash = "foreign"; break;
            case "source-hash": row.SourceReferenceIdHash = "foreign"; break;
            default: row.ArtifactIdHash = "foreign"; break;
        }
        await owner.Context.SaveChangesAsync(); owner.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(template, source).AsTask());
        Assert.Null(await owner.Template.FindAsync(template.TemplateId)); Assert.Single(await owner.Context.WorkflowExecutableSourceReferences.ToArrayAsync());
    }

    [Fact]
    public async Task EF_atomic_template_publication_material_conflict_cannot_install_a_source()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        await owner.Template.SaveAsync(Template("template", "old-hash"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(
            Template("template", "new-hash"), Reference("source", "template")).AsTask());
        Assert.Null(await owner.Store.FindAsync("source")); Assert.Equal("old-hash", (await owner.Template.FindAsync("template"))!.TemplateHash);
    }

    [Fact]
    public async Task EF_atomic_template_publication_refuses_mismatched_reference_material_before_effects()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        await Assert.ThrowsAsync<ArgumentException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(
            Template("template", "template-hash"), Reference("source", "other")).AsTask());
        Assert.Null(await owner.Template.FindAsync("template")); Assert.Null(await owner.Store.FindAsync("source"));
    }

    [Fact]
    public async Task EF_atomic_template_publication_refuses_ambient_transaction_before_discarding_tracked_reads()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        await owner.Template.SaveAsync(Template("seed", "seed-hash")); var tracked = await owner.Context.ExecutableActivityTemplates.SingleAsync();
        using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeOption.RequiresNew,
                   System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            var transaction = System.Transactions.Transaction.Current;
            await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(
                Template("template", "template-hash"), Reference("source", "template")).AsTask());
            Assert.Same(transaction, System.Transactions.Transaction.Current); Assert.Equal(EntityState.Unchanged, owner.Context.Entry(tracked).State);
        }
        await using var fresh = database.Open("tenant-a"); Assert.Null(await fresh.Template.FindAsync("template")); Assert.Null(await fresh.Store.FindAsync("source"));
    }

    [Fact]
    public async Task EF_atomic_template_publication_captures_scope_once_and_refreshes_it_for_a_later_unit()
    {
        await using var database = await Database.CreateAsync(); var access = new MovableFenceAccess();
        var pivot = new FenceScopePivot(() => access.Current = PersistenceAccessContext.Scoped(new("tenant-b")));
        await using var owner = database.Open("tenant-a", pivot);
        var store = new EfExecutableActivityTemplateStore(owner.Context, access, FenceCodec()); var template = Template("template", "template-hash");
        await store.SaveWithReferenceAsync(template, Reference("source", template.TemplateId)); Assert.True(pivot.Ran);
        await using var first = database.Open("tenant-a"); await using var other = database.Open("tenant-b");
        Assert.NotNull(await first.Template.FindAsync(template.TemplateId)); Assert.NotNull(await first.Store.FindAsync("source"));
        Assert.Null(await other.Template.FindAsync(template.TemplateId)); Assert.Null(await other.Store.FindAsync("source"));
        await store.SaveWithReferenceAsync(template, Reference("next", template.TemplateId));
        Assert.NotNull(await other.Template.FindAsync(template.TemplateId)); Assert.NotNull(await other.Store.FindAsync("next")); Assert.Null(await first.Store.FindAsync("next"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_atomic_template_publication_contextualizes_infrastructure_IO_or_unrequested_cancellation(bool cancellation)
    {
        await using var database = await Database.CreateAsync(); Exception cause = cancellation ? new OperationCanceledException("Unrequested cancellation") : new IOException("Provider IO failure");
        await using (var owner = database.Open("tenant-a", new MaintenanceReadFailure(cause)))
        {
            var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(
                Template("template", "template-hash"), Reference("source", "template")).AsTask());
            Assert.Same(cause, failure.InnerException); Assert.Equal("publishing", failure.Operation); Assert.Equal("template", failure.Identity);
        }
        await using var fresh = database.Open("tenant-a"); Assert.Null(await fresh.Template.FindAsync("template")); Assert.Null(await fresh.Store.FindAsync("source"));
    }

    [Fact]
    public async Task EF_atomic_template_publication_requested_cancellation_has_no_partial_effect()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a"); using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(
            Template("template", "template-hash"), Reference("source", "template"), stop.Token).AsTask());
        Assert.Null(await owner.Template.FindAsync("template")); Assert.Null(await owner.Store.FindAsync("source"));
    }

    [Fact]
    public async Task EF_atomic_template_publication_refuses_before_SaveChanges_if_an_existing_material_read_exhausts_reference_lifetime()
    {
        await using var database = await Database.CreateAsync(); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var template = Template("template", "template-hash");
        await using (var seed = database.Open("tenant-a")) await seed.Template.SaveAsync(template);
        var delay = new AtomicClockDuringMaterialRead(clock); var saves = new AtomicSaveCounter();
        await using (var owner = database.Open("tenant-a", delay, saves))
            await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicTemplates(owner, clock).SaveWithReferenceAsync(template,
                Reference("source", template.TemplateId) with { ExpiresAt = clock.GetUtcNow().AddMinutes(1) }).AsTask());
        Assert.True(delay.Advanced); Assert.Equal(0, saves.Count);
        await using var fresh = database.Open("tenant-a"); Assert.Null(await fresh.Store.FindAsync("source")); Assert.Equal(1, (await fresh.Context.ExecutableActivityTemplates.SingleAsync()).Revision);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_atomic_template_publication_rolls_back_real_SQL_on_provider_failure_before_commit(bool adoptMaterial)
    {
        await using var database = await Database.CreateAsync(); var template = Template("template", "template-hash");
        if (adoptMaterial) { await using var seed = database.Open("tenant-a"); await seed.Template.SaveAsync(template); }
        var probe = new AtomicSqlFailure();
        await using (var owner = database.Open("tenant-a", probe))
        {
            var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(
                template, Reference("source", template.TemplateId)).AsTask());
            Assert.Same(probe.Cause, failure.InnerException); Assert.Equal("publishing", failure.Operation);
        }
        Assert.True(probe.SawBothRows);
        await using var fresh = database.Open("tenant-a"); Assert.Null(await fresh.Store.FindAsync("source"));
        if (adoptMaterial) Assert.Equal(1, (await fresh.Context.ExecutableActivityTemplates.SingleAsync()).Revision);
        else { Assert.Null(await fresh.Template.FindAsync(template.TemplateId)); Assert.Empty(await fresh.Context.ExecutableActivityTemplateHashClaims.ToArrayAsync()); }
    }

    private sealed class AtomicSqlFailure : SaveChangesInterceptor
    {
        public bool SawBothRows { get; private set; }
        public Microsoft.Data.Sqlite.SqliteException? Cause { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var owner = (RuntimeDbContext)eventData.Context!;
            SawBothRows = await owner.ExecutableActivityTemplates.AnyAsync(cancellationToken) && await owner.WorkflowExecutableSourceReferences.AnyAsync(cancellationToken);
            try { await owner.Database.ExecuteSqlRawAsync("SELECT * FROM elsa_test_missing_atomic_template_failure", cancellationToken); }
            catch (Microsoft.Data.Sqlite.SqliteException cause) { Cause = cause; throw; }
            throw new InvalidOperationException("The provider failure control did not fail.");
        }
    }

    [Fact]
    public async Task EF_atomic_template_publication_adoption_fences_the_actual_source_row_in_its_own_transaction()
    {
        await using var database = await Database.CreateAsync(); var template = Template("template", "template-hash"); var reference = Reference("source", template.TemplateId);
        WorkflowExecutableSourceReferenceEntity original; long materialRevision;
        await using (var seed = database.Open("tenant-a"))
        {
            await AtomicTemplates(seed).SaveWithReferenceAsync(template, reference);
            original = await seed.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
            materialRevision = (await seed.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync()).Revision;
        }
        var advance = new AdvanceAdoptedSourceRevision(original.Id);
        await using (var owner = database.Open("tenant-a", advance))
        {
            var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(template, reference).AsTask());
            Assert.IsType<DbUpdateConcurrencyException>(EfRelationalExceptionClassifier.FindSaveFailure(failure));
        }
        Assert.Equal(1, advance.Changed); // Actual SQL in this same transaction, not a concurrent peer.
        await using var fresh = database.Open("tenant-a"); var source = await fresh.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(original.Revision, source.Revision); Assert.Equal(original.ContentJson, source.ContentJson);
        Assert.Equal(materialRevision, (await fresh.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync()).Revision);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(reference, await AtomicTemplates(fresh).SaveWithReferenceAsync(template, reference)));
    }

    [Fact]
    public async Task EF_atomic_template_publication_requested_cancellation_during_storage_propagates_without_domain_wrapping()
    {
        await using var database = await Database.CreateAsync(); using var stop = new CancellationTokenSource();
        await using (var owner = database.Open("tenant-a", new AtomicReadCancellation(stop)))
        {
            var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicTemplates(owner).SaveWithReferenceAsync(
                Template("template", "template-hash"), Reference("source", "template"), stop.Token).AsTask());
            Assert.Equal(stop.Token, failure.CancellationToken);
        }
        await using var fresh = database.Open("tenant-a"); Assert.Null(await fresh.Template.FindAsync("template")); Assert.Null(await fresh.Store.FindAsync("source"));
    }

    private sealed class AtomicReadCancellation(CancellationTokenSource stop) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { stop.Cancel(); throw new OperationCanceledException(stop.Token); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_atomic_template_publication_phase_one_uses_the_selected_provider_clock_for_awaited_liveness(bool expired)
    {
        await using var database = await Database.CreateAsync(); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var template = Template("template", "template-hash");
        await using (var seed = database.Open("tenant-a")) await seed.Template.SaveAsync(template);
        var delay = new AtomicClockDuringMaterialRead(clock); var saves = new AtomicSaveCounter();
        await using (var owner = database.Open("tenant-a", delay, saves))
        {
            var commit = new EfActivityPublicationRuntimeCommit(AtomicTemplates(owner, clock), owner.Store);
            var source = Reference("source", template.TemplateId) with { ExpiresAt = clock.GetUtcNow().AddMinutes(expired ? 1 : 3) };
            if (expired) await Assert.ThrowsAsync<InvalidOperationException>(() => commit.CommitAsync(template, source).AsTask());
            else Assert.True(await commit.CommitAsync(template, source));
        }
        Assert.True(delay.Advanced); Assert.Equal(expired ? 0 : 1, saves.Count);
        await using var fresh = database.Open("tenant-a");
        Assert.Equal(expired, await fresh.Store.FindAsync("source") is null);
        Assert.Equal(expired ? 1 : 2, (await fresh.Context.ExecutableActivityTemplates.SingleAsync()).Revision);
    }

    private sealed class AtomicSaveCounter : SaveChangesInterceptor
    {
        public int Count { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Count++; return ValueTask.FromResult(result); }
    }
    private sealed class AtomicClockDuringMaterialRead(FakeTimeProvider clock) : DbCommandInterceptor
    {
        private bool sawSource;
        public bool Advanced { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains($"FROM \"{RuntimeArtifactEfModule.SourceReferenceTableName}\"", StringComparison.Ordinal)) sawSource = true;
            else if (sawSource && !Advanced && command.CommandText.Contains($"FROM \"{RuntimeArtifactEfModule.ExecutableActivityTemplateTableName}\"", StringComparison.Ordinal))
            { Advanced = true; clock.Advance(TimeSpan.FromMinutes(2)); }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class AtomicClockBeforeSql(FakeTimeProvider clock) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { clock.Advance(TimeSpan.FromMinutes(2)); return ValueTask.FromResult(result); }
    }
    private sealed class AtomicClockAfterSql(FakeTimeProvider clock) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { clock.Advance(TimeSpan.FromMinutes(2)); return ValueTask.FromResult(result); }
    }
    private sealed class AtomicCommitAckLoss : DbTransactionInterceptor
    {
        public bool Committed { get; private set; }
        public IOException Cause { get; } = new("Actual template/root commit acknowledgement was lost.");
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { Committed = true; throw Cause; }
    }
}
