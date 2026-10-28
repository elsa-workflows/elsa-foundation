using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeArtifactScopeTests
{
    [Fact]
    public async Task EF_template_maintenance_uses_observed_material_and_removes_references_before_material()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash"); await owner.Template.SaveAsync(template);
        var retired = Reference("source", template.TemplateId).Retire(DateTimeOffset.UtcNow, "retired"); await owner.Store.SaveAsync(retired);
        Assert.True(owner.Template.CanMaintainReferences(owner.Executable, owner.Store));
        Assert.False(await owner.Template.TryDeleteUnreferencedAsync(template)); // A caller-created DTO is not a native read observation.
        var observed = (await owner.Template.FindAsync(template.TemplateId))!;
        Assert.False(await owner.Template.TryDeleteUnreferencedAsync(observed));
        Assert.True(await owner.Template.TryDeleteDoomedReferenceAsync(retired, DateTimeOffset.UtcNow));
        Assert.True(await owner.Template.TryDeleteUnreferencedAsync(observed)); // A coordination revision advance did not change incarnation/content.
        await using var fresh = database.Open("tenant-a");
        Assert.Null(await fresh.Template.FindByHashAsync(template.TemplateHash)); Assert.Null(await fresh.Store.FindAsync("source"));
        Assert.Empty(await fresh.Context.ExecutableActivityTemplateHashClaims.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData("wrapper")] [InlineData("node")] [InlineData("nested")]
    public async Task EF_template_maintenance_keeps_physical_workflow_dependencies_before_any_execution_root(string route)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash"); await owner.Template.SaveAsync(template);
        var observed = (await owner.Template.FindAsync(template.TemplateId))!;
        var retired = Reference("source", template.TemplateId).Retire(DateTimeOffset.UtcNow, "retired"); await owner.Store.SaveAsync(retired);
        await owner.Executable.SaveAsync(TemplateWorkflow(template, route));
        Assert.False(await owner.Template.TryDeleteDoomedReferenceAsync(retired, DateTimeOffset.UtcNow));
        Assert.False(await owner.Template.TryDeleteUnreferencedAsync(observed));
        Assert.True(await owner.Executable.DeleteAsync("workflow"));
        Assert.True(await owner.Template.TryDeleteDoomedReferenceAsync(retired, DateTimeOffset.UtcNow));
        Assert.True(await owner.Template.TryDeleteUnreferencedAsync(observed));
    }

    [Theory]
    [InlineData("closed")] [InlineData("direct")] [InlineData("metadata")] [InlineData("nested")] [InlineData("shared")]
    public async Task EF_template_maintenance_keeps_every_physical_parent_route_until_parent_material_is_collected(string route)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var leaf = Template("leaf", "leaf-hash"); await owner.Template.SaveAsync(leaf); await owner.Template.SaveAsync(TemplateParent(leaf, route));
        var observed = (await owner.Template.FindAsync(leaf.TemplateId))!;
        Assert.False(await owner.Template.TryDeleteUnreferencedAsync(observed));
        Assert.True(await owner.Template.TryDeleteUnreferencedAsync((await owner.Template.FindAsync("parent"))!));
        Assert.True(await owner.Template.TryDeleteUnreferencedAsync(observed));
    }

    [Theory]
    [InlineData("workflow", false)] [InlineData("parent", false)] [InlineData("source", false)]
    [InlineData("workflow", true)] [InlineData("parent", true)] [InlineData("source", true)]
    public async Task A_real_peer_writer_after_EF_maintenance_discovery_wins_against_the_actual_conditional_operation(string kind, bool deleteReference)
    {
        await using var database = await Database.CreateFileAsync();
        var template = Template("leaf", "leaf-hash"); ExecutableActivityTemplate observed;
        var retired = Reference("old-source", template.TemplateId).Retire(DateTimeOffset.UtcNow, "retired");
        await using (var seed = database.Open("tenant-a"))
        {
            await seed.Template.SaveAsync(template); observed = (await seed.Template.FindAsync(template.TemplateId))!;
            if (deleteReference) await seed.Store.SaveAsync(retired);
        }
        var peer = new BeforeTemplateMaintenanceTransaction(async () =>
        {
            await using var writer = database.Open("tenant-a");
            await WriteTemplateReferencer(writer, template, kind);
        });
        await using (var collector = database.Open("tenant-a", peer))
        {
            var deleted = deleteReference
                ? await collector.Template.TryDeleteDoomedReferenceAsync(retired, DateTimeOffset.UtcNow)
                : await collector.Template.TryDeleteUnreferencedAsync(observed);
            Assert.False(deleted);
        }
        Assert.True(peer.Ran);
        await using var fresh = database.Open("tenant-a");
        Assert.NotNull(await fresh.Template.FindAsync(template.TemplateId));
        if (deleteReference) Assert.NotNull(await fresh.Store.FindAsync(retired.SourceReferenceId));
        if (kind == "workflow") Assert.NotNull(await fresh.Executable.FindAsync("workflow"));
        else if (kind == "parent") Assert.NotNull(await fresh.Template.FindAsync("parent"));
        else Assert.NotNull(await fresh.Store.FindAsync("source"));
    }

    [Fact]
    public async Task EF_collection_wins_before_exact_parent_publication_and_admission_refuses_without_partial_root()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var leaf = Template("leaf", "leaf-hash"); await owner.Template.SaveAsync(leaf);
        Assert.True(await owner.Template.TryDeleteUnreferencedAsync((await owner.Template.FindAsync(leaf.TemplateId))!));
        var parent = TemplateParent(leaf, "closed");
        await Assert.ThrowsAsync<InvalidDataException>(() => new EfActivityPublicationRuntimeCommit(owner.Template, owner.Store)
            .CommitAsync(parent, Reference("source", parent.TemplateId)).AsTask());
        Assert.Null(await owner.Template.FindAsync(parent.TemplateId)); Assert.Null(await owner.Store.FindAsync("source"));
        await owner.Template.SaveAsync(leaf);
        Assert.True(await new EfActivityPublicationRuntimeCommit(owner.Template, owner.Store).CommitAsync(parent, Reference("source", parent.TemplateId)));
        Assert.False(await owner.Template.TryDeleteUnreferencedAsync((await owner.Template.FindAsync(leaf.TemplateId))!));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_doomed_reference_maintenance_refuses_restored_or_replaced_current_rows(bool replace)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash"); await owner.Template.SaveAsync(template);
        var retired = Reference("source", template.TemplateId).Retire(DateTimeOffset.UtcNow, "retired"); await owner.Store.SaveAsync(retired);
        if (replace)
        {
            Assert.True(await owner.Store.TryDeleteDoomedAsync(retired, DateTimeOffset.UtcNow));
            await owner.Store.SaveAsync(retired with { SourceId = "replacement" });
        }
        else Assert.True(await owner.Store.TryRestoreAsync(retired, retired with { DeletedAt = null, DeletedReason = null }));
        Assert.False(await owner.Template.TryDeleteDoomedReferenceAsync(retired, DateTimeOffset.UtcNow));
        Assert.NotNull(await owner.Store.FindAsync("source"));
    }

    [Fact]
    public async Task EF_read_observation_refuses_a_different_scope_and_identical_recreated_incarnation()
    {
        await using var database = await Database.CreateAsync(); await using var first = database.Open("tenant-a"); await using var other = database.Open("tenant-b");
        var template = Template("template", "template-hash"); await first.Template.SaveAsync(template); await other.Template.SaveAsync(template);
        var observed = (await first.Template.FindAsync(template.TemplateId))!;
        Assert.False(await other.Template.TryDeleteUnreferencedAsync(observed));
        Assert.True(await first.Template.DeleteAsync(template.TemplateId)); await first.Template.SaveAsync(template);
        Assert.False(await first.Template.TryDeleteUnreferencedAsync(observed));
        Assert.True(await first.Template.TryDeleteUnreferencedAsync((await first.Template.FindAsync(template.TemplateId))!));
        Assert.NotNull(await other.Template.FindAsync(template.TemplateId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_maintenance_refuses_caller_transaction_or_pending_writes_without_discarding_them(bool pending)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash"); await owner.Template.SaveAsync(template);
        var observed = (await owner.Template.FindAsync(template.TemplateId))!;
        if (pending)
        {
            (await owner.Context.ExecutableActivityTemplates.SingleAsync()).Revision++;
            await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Template.TryDeleteUnreferencedAsync(observed).AsTask());
            Assert.True(owner.Context.ChangeTracker.HasChanges());
        }
        else
        {
            await using var transaction = await owner.Context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Template.TryDeleteUnreferencedAsync(observed).AsTask());
            Assert.Same(transaction, owner.Context.Database.CurrentTransaction);
        }
        await using var fresh = database.Open("tenant-a");
        Assert.NotNull(await fresh.Template.FindAsync(template.TemplateId));
    }

    [Fact]
    public async Task EF_maintenance_refuses_ambient_transaction_without_enlisting_or_discarding_caller_state()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash"); await owner.Template.SaveAsync(template);
        var observed = (await owner.Template.FindAsync(template.TemplateId))!;
        var retired = Reference("source", template.TemplateId).Retire(DateTimeOffset.UtcNow, "retired"); await owner.Store.SaveAsync(retired);
        var tracked = await owner.Context.ExecutableActivityTemplates.SingleAsync();
        using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeOption.RequiresNew,
                   System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            var transaction = System.Transactions.Transaction.Current;
            Assert.NotNull(transaction);
            await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Template.TryDeleteUnreferencedAsync(observed).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Template.TryDeleteDoomedReferenceAsync(retired, DateTimeOffset.UtcNow).AsTask());
            Assert.Same(transaction, System.Transactions.Transaction.Current);
            Assert.Equal(EntityState.Unchanged, owner.Context.Entry(tracked).State);
        }
        await using var fresh = database.Open("tenant-a");
        Assert.NotNull(await fresh.Template.FindAsync(template.TemplateId)); Assert.NotNull(await fresh.Store.FindAsync("source"));
        Assert.True(await fresh.Template.TryDeleteDoomedReferenceAsync(retired, DateTimeOffset.UtcNow));
        Assert.True(await fresh.Template.TryDeleteUnreferencedAsync(observed));
    }

    [Fact]
    public async Task EF_maintenance_capability_requires_the_exact_context_and_current_scope()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a"); await using var foreign = database.Open("tenant-a");
        Assert.True(owner.Template.CanMaintainReferences(owner.Executable, owner.Store));
        Assert.False(owner.Template.CanMaintainReferences(foreign.Executable, owner.Store));
        Assert.False(owner.Template.CanMaintainReferences(owner.Executable, foreign.Store));
        var scope = new Accessor(PersistenceAccessContext.Scoped(new("tenant-b")));
        Assert.False(owner.Template.CanMaintainReferences(new EfWorkflowExecutableStore(owner.Context, scope), owner.Store));
        Assert.False(owner.Template.CanMaintainReferences(owner.Executable, new EfWorkflowExecutableSourceReferenceStore(owner.Context, scope, FenceCodec())));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_maintenance_rolls_back_real_SQL_deletion_on_provider_failure_before_commit(bool deleteReference)
    {
        await using var database = await Database.CreateAsync(); var template = Template("template", "template-hash"); ExecutableActivityTemplate observed;
        var retired = Reference("source", template.TemplateId).Retire(DateTimeOffset.UtcNow, "retired");
        await using (var seed = database.Open("tenant-a"))
        {
            await seed.Template.SaveAsync(template); observed = (await seed.Template.FindAsync(template.TemplateId))!;
            if (deleteReference) await seed.Store.SaveAsync(retired);
        }
        var failureProbe = new AfterTemplateMaintenanceSql(deleteReference);
        await using (var failing = database.Open("tenant-a", failureProbe))
        {
            var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => deleteReference
                ? failing.Template.TryDeleteDoomedReferenceAsync(retired, DateTimeOffset.UtcNow).AsTask()
                : failing.Template.TryDeleteUnreferencedAsync(observed).AsTask());
            Assert.Equal("maintaining template", failure.Operation); Assert.NotNull(failureProbe.Cause); Assert.Same(failureProbe.Cause, failure.InnerException);
        }
        Assert.True(failureProbe.SawDeletion);
        await using var fresh = database.Open("tenant-a"); Assert.NotNull(await fresh.Template.FindAsync(template.TemplateId));
        if (deleteReference) Assert.NotNull(await fresh.Store.FindAsync("source"));
        else Assert.True(await fresh.Template.TryDeleteUnreferencedAsync(observed));
    }

    [Fact]
    public async Task EF_material_maintenance_does_not_let_a_corrupt_source_projection_hide_a_reference()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash"); await owner.Template.SaveAsync(template); await owner.Store.SaveAsync(Reference("source", template.TemplateId));
        var observed = (await owner.Template.FindAsync(template.TemplateId))!;
        (await owner.Context.WorkflowExecutableSourceReferences.SingleAsync()).ArtifactIdHash = "corrupt";
        await owner.Context.SaveChangesAsync(); owner.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => owner.Template.TryDeleteUnreferencedAsync(observed).AsTask());
        Assert.NotNull(await owner.Template.FindAsync(template.TemplateId));
    }

    [Theory]
    [InlineData("workflow", false)] [InlineData("workflow", true)]
    [InlineData("parent", false)] [InlineData("parent", true)]
    [InlineData("source", false)] [InlineData("source", true)]
    public async Task EF_maintenance_retains_material_when_a_physical_scan_row_has_contradictory_scope_projections(string family, bool hash)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash"); await owner.Template.SaveAsync(template);
        var observed = (await owner.Template.FindAsync(template.TemplateId))!;
        if (family == "workflow")
        {
            await owner.Executable.SaveAsync(TemplateWorkflow(template, "nested"));
            var row = await owner.Context.WorkflowExecutables.SingleAsync();
            if (hash) row.ScopeKeyHash = "foreign"; else row.ScopeKey = EfRelationalIdentity.Encode("foreign");
        }
        else if (family == "parent")
        {
            await owner.Template.SaveAsync(TemplateParent(template, "closed"));
            var row = await owner.Context.ExecutableActivityTemplates.SingleAsync(candidate => candidate.TemplateHash == "parent-hash");
            if (hash) row.ScopeKeyHash = "foreign"; else row.ScopeKey = EfRelationalIdentity.Encode("foreign");
        }
        else
        {
            await owner.Store.SaveAsync(Reference("source", template.TemplateId));
            var row = await owner.Context.WorkflowExecutableSourceReferences.SingleAsync();
            if (hash) row.ScopeKeyHash = "foreign"; else row.ScopeKey = EfRelationalIdentity.Encode("foreign");
        }
        await owner.Context.SaveChangesAsync(); owner.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => owner.Template.TryDeleteUnreferencedAsync(observed).AsTask());
        Assert.NotNull(await owner.Template.FindAsync(template.TemplateId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_maintenance_contextualizes_infrastructure_IO_and_unrequested_cancellation(bool cancellation)
    {
        await using var database = await Database.CreateAsync(); var template = Template("template", "template-hash"); ExecutableActivityTemplate observed;
        await using (var seed = database.Open("tenant-a")) { await seed.Template.SaveAsync(template); observed = (await seed.Template.FindAsync(template.TemplateId))!; }
        Exception cause = cancellation ? new OperationCanceledException("Unrequested provider cancellation") : new IOException("Provider IO failure");
        await using (var failing = database.Open("tenant-a", new MaintenanceReadFailure(cause)))
        {
            var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => failing.Template.TryDeleteUnreferencedAsync(observed).AsTask());
            Assert.Equal("maintaining template", failure.Operation); Assert.Equal(template.TemplateId, failure.Identity); Assert.Same(cause, failure.InnerException);
        }
        await using var fresh = database.Open("tenant-a"); Assert.NotNull(await fresh.Template.FindAsync(template.TemplateId));
    }

    private sealed class MaintenanceReadFailure(Exception cause) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) => throw cause;
    }

    [Fact]
    public async Task EF_maintenance_traverses_later_physical_reference_pages_before_material_deletion()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash"); await owner.Template.SaveAsync(template);
        var ids = Enumerable.Range(0, RuntimeStorePageRequest.MaximumLimit + 1).Select(index => "page-source-" + index).ToArray();
        string Key(string id) => EfRelationalIdentity.Hash($"{8}:tenant-a{id.Length}:{id}");
        var last = ids.MaxBy(Key, StringComparer.Ordinal)!;
        foreach (var id in ids) await owner.Store.SaveAsync(Reference(id, id == last ? template.TemplateId : "unrelated"));
        var observed = (await owner.Template.FindAsync(template.TemplateId))!;
        Assert.False(await owner.Template.TryDeleteUnreferencedAsync(observed));
        Assert.NotNull(await owner.Template.FindAsync(template.TemplateId)); Assert.NotNull(await owner.Store.FindAsync(last));
    }

    [Fact]
    public async Task EF_maintenance_retains_an_ambiguous_template_and_workflow_identity()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("shared-id", "template-hash"); await owner.Template.SaveAsync(template);
        await owner.Executable.SaveAsync(Executable(template.TemplateId));
        var observed = (await owner.Template.FindAsync(template.TemplateId))!;
        var retired = Reference("source", template.TemplateId).Retire(DateTimeOffset.UtcNow, "retired"); await owner.Store.SaveAsync(retired);
        Assert.False(await owner.Template.TryDeleteDoomedReferenceAsync(retired, DateTimeOffset.UtcNow));
        Assert.False(await owner.Template.TryDeleteUnreferencedAsync(observed));
        Assert.NotNull(await owner.Executable.FindAsync(template.TemplateId)); Assert.NotNull(await owner.Store.FindAsync("source"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EF_doomed_reference_maintenance_deletes_only_currently_expired_or_retired_rows(bool expired)
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash"); await owner.Template.SaveAsync(template);
        var now = DateTimeOffset.UtcNow;
        var reference = Reference("source", template.TemplateId) with { ExpiresAt = expired ? now : now.AddHours(1) };
        await owner.Store.SaveAsync(reference);
        Assert.Equal(expired, await owner.Template.TryDeleteDoomedReferenceAsync(reference, now));
        if (expired) Assert.Null(await owner.Store.FindAsync("source"));
        else Assert.NotNull(await owner.Store.FindAsync("source"));
        var observed = (await owner.Template.FindAsync(template.TemplateId))!;
        Assert.Equal(expired, await owner.Template.TryDeleteUnreferencedAsync(observed));
    }

    [Fact]
    public async Task EF_material_deletion_requires_the_actual_reader_object_not_constructed_or_serialized_copies()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash"); await owner.Template.SaveAsync(template);
        var observed = (await owner.Template.FindAsync(template.TemplateId))!;
        var copy = JsonSerializer.Deserialize<ExecutableActivityTemplate>(JsonSerializer.Serialize(observed), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.False(await owner.Template.TryDeleteUnreferencedAsync(template));
        Assert.False(await owner.Template.TryDeleteUnreferencedAsync(copy));
        Assert.True(await owner.Template.TryDeleteUnreferencedAsync(observed));
    }

    [Fact]
    public async Task EF_maintenance_propagates_requested_cancellation_before_effects()
    {
        await using var database = await Database.CreateAsync(); await using var owner = database.Open("tenant-a");
        var template = Template("template", "template-hash"); await owner.Template.SaveAsync(template); var observed = (await owner.Template.FindAsync(template.TemplateId))!;
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.Template.TryDeleteUnreferencedAsync(observed, stop.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.Template.TryDeleteDoomedReferenceAsync(Reference("source", template.TemplateId), DateTimeOffset.UtcNow, stop.Token).AsTask());
        Assert.NotNull(await owner.Template.FindAsync(template.TemplateId));
    }

    private sealed class BeforeTemplateMaintenanceTransaction(Func<Task> peer) : DbTransactionInterceptor
    {
        public bool Ran { get; private set; }
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (!Ran) { Ran = true; await peer(); }
            return result;
        }
    }

    private sealed class AfterTemplateMaintenanceSql(bool reference) : SaveChangesInterceptor
    {
        public bool SawDeletion { get; private set; }
        public Microsoft.Data.Sqlite.SqliteException? Cause { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var owner = (RuntimeDbContext)eventData.Context!;
            SawDeletion = reference ? !await owner.WorkflowExecutableSourceReferences.AnyAsync(cancellationToken) : !await owner.ExecutableActivityTemplates.AnyAsync(cancellationToken);
            try { await owner.Database.ExecuteSqlRawAsync("SELECT * FROM elsa_test_missing_template_maintenance_failure", cancellationToken); }
            catch (Microsoft.Data.Sqlite.SqliteException failure) { Cause = failure; throw; }
            throw new InvalidOperationException("The provider failure control did not fail.");
        }
    }
}
