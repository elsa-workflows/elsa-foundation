using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        await fixture.Store.SaveAsync(Reference("ref-a", "artifact-a"));
        await fixture.Store.SaveAsync(Reference("ref-b", "artifact-b"));

        var row = await fixture.Context.WorkflowExecutableSourceReferences.SingleAsync(x => x.ArtifactId == "artifact-b");
        row.ArtifactIdHash = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Hash("artifact-a");
        await fixture.Context.SaveChangesAsync();

        var unreferenced = await fixture.Store.ListUnreferencedArtifactIdsAsync(new(["artifact-a", "artifact-b"]), DateTimeOffset.UtcNow);
        Assert.Equal(["artifact-b"], unreferenced);
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

    private static WorkflowExecutableSourceReference Reference(string id, string artifact) => new(
        id, artifact, "WorkflowDefinition", "definition", "1", "definition", "definition-version", "1",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, WorkflowExecutableReferenceScope.Published);

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
            return new Fixture(context, new EfWorkflowExecutableSourceReferenceStore(context, new Accessor(access)));
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class Fixture(BookmarkStateSqliteDbContext context, EfWorkflowExecutableSourceReferenceStore store) : IAsyncDisposable
    {
        public BookmarkStateSqliteDbContext Context { get; } = context;
        public EfWorkflowExecutableSourceReferenceStore Store { get; } = store;
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class Accessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
