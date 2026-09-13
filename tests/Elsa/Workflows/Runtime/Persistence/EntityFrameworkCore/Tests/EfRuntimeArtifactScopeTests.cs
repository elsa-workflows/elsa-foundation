using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeArtifactScopeTests
{
    [Fact]
    public async Task Source_reference_ids_are_isolated_when_tenants_reuse_the_same_id()
    {
        await using var database = await Database.CreateAsync();
        await using var tenantA = database.Open("tenant-a");
        await using var tenantB = database.Open("tenant-b");

        await tenantA.Store.SaveAsync(Reference("same-ref", "artifact-a"));
        await tenantB.Store.SaveAsync(Reference("same-ref", "artifact-b"));

        Assert.Equal("artifact-a", (await tenantA.Store.FindAsync("same-ref"))!.ArtifactId);
        Assert.Equal("artifact-b", (await tenantB.Store.FindAsync("same-ref"))!.ArtifactId);
    }

    [Fact]
    public async Task Executables_and_templates_isolate_same_logical_ids_and_allow_privileged_scoped_access()
    {
        await using var database = await Database.CreateAsync();
        await using var tenantA = database.Open("tenant-a");
        await using var tenantB = database.Open("tenant-b");
        await tenantA.Executable.SaveAsync(Executable("same-artifact"));
        await tenantB.Executable.SaveAsync(Executable("same-artifact"));
        await tenantA.Template.SaveAsync(Template("same-template", "hash-a"));
        await tenantB.Template.SaveAsync(Template("same-template", "hash-b"));
        Assert.Equal("same-artifact", (await tenantA.Executable.FindAsync("same-artifact"))!.Identity.ArtifactId);
        Assert.Null(await tenantA.Template.FindByHashAsync("hash-b"));
        Assert.Equal("hash-b", (await tenantB.Template.FindByHashAsync("hash-b"))!.TemplateHash);

        await using var privileged = database.Open(PersistenceAccessContext.PrivilegedScoped(new PersistenceScope("tenant-a"), new PersistenceAccessPurpose("maintenance")));
        Assert.NotNull(await privileged.Executable.FindAsync("same-artifact"));
        Assert.NotNull(await privileged.Template.FindAsync("same-template"));
    }

    [Fact]
    public async Task Global_and_across_scope_access_are_rejected_before_querying()
    {
        await using var database = await Database.CreateAsync();
        await using var global = database.Open(PersistenceAccessContext.Global);
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.Store.FindAsync("ref").AsTask());

        await using var across = database.Open(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("gc")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => across.Store.ListUnreferencedArtifactIdsAsync(new(["artifact"]), DateTimeOffset.UtcNow).AsTask());
    }

    [Fact]
    public async Task Unreferenced_lookup_requires_exact_artifact_value_after_hash_projection_collision()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.SaveAsync(Reference("ref-b", "artifact-b"));

        var row = await fixture.Context.WorkflowExecutableSourceReferences.SingleAsync(x => x.ArtifactId == "artifact-b");
        row.ArtifactIdHash = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Hash("artifact-a");
        await fixture.Context.SaveChangesAsync();

        var unreferenced = await fixture.Store.ListUnreferencedArtifactIdsAsync(new(["artifact-a", "artifact-b"]), DateTimeOffset.UtcNow);
        Assert.Equal(["artifact-a", "artifact-b"], unreferenced);
    }

    [Fact]
    public async Task Unreferenced_lookup_is_bounded_to_the_finite_candidate_set()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.SaveAsync(Reference("ref-live", "artifact-live"));
        await fixture.Store.SaveAsync(Reference("ref-other", "artifact-other"));

        var unreferenced = await fixture.Store.ListUnreferencedArtifactIdsAsync(new(["artifact-live", "artifact-missing"]), DateTimeOffset.UtcNow);

        Assert.Equal(["artifact-missing"], unreferenced);
    }

    [Fact]
    public async Task Executable_save_is_idempotent_and_batch_failure_rolls_back_new_rows()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");

        await fixture.Executable.SaveAsync(Executable("same-artifact"));
        await fixture.Executable.SaveAsync(Executable("same-artifact"));
        Assert.NotNull(await fixture.Executable.FindAsync("same-artifact"));

        await fixture.Executable.SaveAsync(Executable("incomplete"));
        var coordination = await fixture.Context.WorkflowExecutableCoordinations
            .SingleAsync(x => x.ArtifactId == "incomplete");
        fixture.Context.WorkflowExecutableCoordinations.Remove(coordination);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable.SaveBatchAsync(
            [Executable("new-artifact"), Executable("incomplete")]).AsTask());
        Assert.Null(await fixture.Executable.FindAsync("new-artifact"));
    }

    [Fact]
    public async Task Ordinary_and_guarded_deletes_remove_the_pair_and_reject_stale_guards()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await using var other = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow;

        await fixture.Executable.SaveAsync(Executable("ordinary"));
        Assert.True(await fixture.Executable.DeleteAsync("ordinary"));
        Assert.Null(await fixture.Executable.FindAsync("ordinary"));

        await fixture.Executable.SaveAsync(Executable("guarded"));
        var guard = await fixture.Executable.TryBeginDeletionAsync("guarded", "operation", now.AddMinutes(5), now);
        Assert.NotNull(guard);

        var row = await other.Context.WorkflowExecutableCoordinations.SingleAsync(x => x.ArtifactId == "guarded");
        row.ContentJson = "{\"Leases\":{},\"Guard\":null}";
        row.Revision++;
        await other.Context.SaveChangesAsync();
        Assert.False(await fixture.Executable.DeleteAsync(guard!, now));
        Assert.NotNull(await fixture.Executable.FindAsync("guarded"));
    }

    [Fact]
    public async Task Leases_and_guards_are_mutually_exclusive_and_expired_state_recovers()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Executable.SaveAsync(Executable("coordination"));
        var now = DateTimeOffset.UtcNow;

        var lease = await fixture.Executable.TryAcquireRootWriteLeaseAsync("coordination", "lease", now.AddMinutes(1), now);
        Assert.NotNull(lease);
        Assert.Null(await fixture.Executable.TryBeginDeletionAsync("coordination", "operation", now.AddMinutes(5), now));
        await fixture.Executable.ReleaseRootWriteLeaseAsync(lease!);

        var guard = await fixture.Executable.TryBeginDeletionAsync("coordination", "operation", now.AddMinutes(1), now);
        Assert.NotNull(guard);
        Assert.Null(await fixture.Executable.TryAcquireRootWriteLeaseAsync("coordination", "other", now.AddMinutes(5), now));

        var recovered = await fixture.Executable.TryAcquireRootWriteLeaseAsync("coordination", "other", now.AddMinutes(5), now.AddMinutes(2));
        Assert.NotNull(recovered);
        Assert.False(await fixture.Executable.RenewRootWriteLeaseAsync(lease!, now.AddMinutes(6), now.AddMinutes(2)));
    }

    [Fact]
    public async Task Optimistic_coordination_conflict_clears_tracker_and_reloads_before_retry()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await using var other = database.Open("tenant-a");
        await fixture.Executable.SaveAsync(Executable("conflict"));
        var now = DateTimeOffset.UtcNow;
        var first = await fixture.Executable.TryAcquireRootWriteLeaseAsync("conflict", "first", now.AddMinutes(5), now);
        Assert.NotNull(first);

        var row = await other.Context.WorkflowExecutableCoordinations.SingleAsync(x => x.ArtifactId == "conflict");
        row.ContentJson = "{\"Leases\":{},\"Guard\":null}";
        row.Revision++;
        await other.Context.SaveChangesAsync();

        var second = await fixture.Executable.TryAcquireRootWriteLeaseAsync("conflict", "second", now.AddMinutes(5), now);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task Corrupt_coordination_payloads_fail_closed_as_invalid_data()
    {
        var payloads = new[]
        {
            "null",
            "{",
            "{\"Leases\":null,\"Guard\":null}",
            "{\"Leases\":{\"key\":{\"Id\":\"other\",\"Token\":\"token\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"}},\"Guard\":null}",
            "{\"Leases\":{\"one\":{\"Id\":\"one\",\"Token\":\"\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"},\"two\":{\"Id\":\"two\",\"Token\":\"\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"}},\"Guard\":null}",
            "{\"Leases\":{\"one\":{\"Id\":\"one\",\"Token\":\"token\",\"ExpiresAt\":\"0001-01-01T00:00:00+00:00\"}},\"Guard\":null}",
            "{\"Leases\":{},\"Guard\":{\"OperationId\":\"\",\"Token\":\"token\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"}}"
        };

        foreach (var payload in payloads)
        {
            await using var database = await Database.CreateAsync();
            await using var fixture = database.Open("tenant-a");
            await fixture.Executable.SaveAsync(Executable("corrupt"));
            var row = await fixture.Context.WorkflowExecutableCoordinations.SingleAsync(x => x.ArtifactId == "corrupt");
            row.ContentJson = payload;
            await fixture.Context.SaveChangesAsync();
            fixture.Context.ChangeTracker.Clear();

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable
                .TryBeginDeletionAsync("corrupt", "operation", DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow)
                .AsTask());
            Assert.IsNotType<JsonException>(exception);
            Assert.IsNotType<NullReferenceException>(exception);
        }
    }

    private static WorkflowExecutableSourceReference Reference(string id, string artifact) => new(
        id, artifact, "WorkflowDefinition", "definition", "1", "definition", "definition-version", "1",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, WorkflowExecutableReferenceScope.Published);

    private static WorkflowExecutable Executable(string artifactId)
    {
        var node = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), outputCaptures: new Dictionary<string, RuntimeOutputCapture>());
        return new WorkflowExecutable(new WorkflowExecutableIdentity(artifactId, "definition", "version", "1", $"hash-{artifactId}"), node, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static ExecutableActivityTemplate Template(string id, string hash)
    {
        var node = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), outputCaptures: new Dictionary<string, RuntimeOutputCapture>());
        return new ExecutableActivityTemplate(id, hash, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "fingerprint", new Dictionary<string, string>(), DateTimeOffset.UtcNow);
    }

    private sealed class Database : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private Database(SqliteConnection connection) => this.connection = connection;

        public static async Task<Database> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new Database(connection);
        }

        public Fixture Open(string scope) => Open(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

        public Fixture Open(PersistenceAccessContext access)
        {
            var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            return new Fixture(context, new EfWorkflowExecutableSourceReferenceStore(context, new Accessor(access)), new EfWorkflowExecutableStore(context, new Accessor(access)), new EfExecutableActivityTemplateStore(context, new Accessor(access)));
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class Fixture(BookmarkStateSqliteDbContext context, EfWorkflowExecutableSourceReferenceStore store, EfWorkflowExecutableStore executable, EfExecutableActivityTemplateStore template) : IAsyncDisposable
    {
        public BookmarkStateSqliteDbContext Context { get; } = context;
        public EfWorkflowExecutableSourceReferenceStore Store { get; } = store;
        public EfWorkflowExecutableStore Executable { get; } = executable;
        public EfExecutableActivityTemplateStore Template { get; } = template;
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class Accessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
