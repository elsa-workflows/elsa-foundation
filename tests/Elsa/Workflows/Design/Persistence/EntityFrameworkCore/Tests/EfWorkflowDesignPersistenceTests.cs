using System.Text.Json;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Serialization.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowDesignPersistenceTests
{
    [Fact]
    public async Task Definitions_drafts_layouts_and_versions_survive_reopen_and_preserve_scope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var serializer = new TestSerializer(); var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        await using (var db = Create(connection))
        {
            await db.Database.EnsureCreatedAsync(); var atomic = new EfDesignAtomicWriter(db); var identities = new TestIdentity();
            var definition = new WorkflowDefinition { Id = "definition-1", TenantId = "tenant-a", Name = "Order" }; var draft = new WorkflowDefinitionDraft { Id = "draft-1", TenantId = "tenant-a", WorkflowDefinitionId = definition.Id, State = State() };
            var add = new EfAddWorkflowDefinitionCommand(db, accessor, atomic, serializer, identities); await add.Execute(new DesignOperationKey("create-1"), definition, draft, [new DesignMetadataRecord("root", 1, 2)]);
            var definitions = new EfWorkflowDefinitionStore(db, accessor); Assert.Single(await definitions.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "ord" }));
            var drafts = new EfWorkflowDefinitionDraftStore(db, serializer, accessor); var loadedLayout = await drafts.FindWithLayoutByIdAsync(draft.Id); Assert.NotNull(loadedLayout); Assert.Single(loadedLayout!.Layout);
            var version = new EfAddWorkflowDefinitionVersionCommand(db, accessor, atomic, serializer, identities); var added = await version.Execute(new DesignOperationKey("version-1"), definition.Id, State()); Assert.Equal("1.0.0", added.Version);
        }
        await using (var reopened = Create(connection))
        {
            var store = new EfWorkflowDefinitionStore(reopened, accessor); Assert.NotNull(await store.FindByIdAsync("definition-1"));
            var versions = new EfWorkflowDefinitionVersionStore(reopened, serializer, store, accessor); Assert.Equal("1.0.0", (await versions.FindLatestVersionAsync("definition-1"))!.Version);
        }
        var other = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"))); await using var scopedDb = Create(connection); Assert.Null(await new EfWorkflowDefinitionStore(scopedDb, other).FindByIdAsync("definition-1"));
    }

    [Fact]
    public async Task Operation_key_replays_and_conflicting_request_is_rejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var writer = new EfDesignAtomicWriter(db); var key = new DesignOperationKey("same"); var first = await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, _ => Task.FromResult(new { Id = "winner" })); var replay = await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, _ => Task.FromResult(new { Id = "loser" })); Assert.Equal(first.Id, replay.Id); await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ExecuteAsync(key, "test.op", new { Value = 2 }, _ => Task.FromResult(new { Id = "conflict" })));
    }

    private static WorkflowsDesignSqliteDbContext Create(SqliteConnection connection) => new(new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>().UseSqlite(connection).Options);
    private static WorkflowDefinitionState State() => new([], null, [], [], null);
    private sealed class TestIdentity : IIdentityGenerator { private int n; public string Generate() => $"generated-{Interlocked.Increment(ref n)}"; }
    private sealed class TestAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => current; }
    private sealed class TestSerializer : IPayloadSerializer
    {
        public string Serialize(object payload) => JsonSerializer.Serialize(payload);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload);
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<JsonElement>(serializedData);
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type)!;
        public object Deserialize(JsonElement serializedData) => serializedData;
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData)!;
        public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>()!;
        public JsonSerializerOptions GetOptions() => new();
    }
}
