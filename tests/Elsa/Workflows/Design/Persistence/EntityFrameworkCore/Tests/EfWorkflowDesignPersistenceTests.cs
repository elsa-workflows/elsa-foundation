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
            await db.Database.EnsureCreatedAsync(); var atomic = new EfDesignAtomicWriter(db, accessor); var identities = new TestIdentity();
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
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))); var writer = new EfDesignAtomicWriter(db, access); var key = new DesignOperationKey("same"); var first = await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, _ => Task.FromResult(new { Id = "winner" })); var replay = await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, _ => Task.FromResult(new { Id = "loser" })); Assert.Equal(first.Id, replay.Id); await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ExecuteAsync(key, "test.op", new { Value = 2 }, _ => Task.FromResult(new { Id = "conflict" })));
    }

    [Fact]
    public async Task Same_ids_and_operation_keys_are_isolated_by_tenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var a = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var b = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b")));
        var serializer = new TestSerializer(); var identities = new TestIdentity();
        var first = new EfDesignAtomicWriter(db, access: a);
        var second = new EfDesignAtomicWriter(db, access: b);
        await first.ExecuteAsync(new DesignOperationKey("same"), "test.op", new { Value = 1 }, _ => Task.FromResult(new { Id = "a" }));
        var replay = await second.ExecuteAsync(new DesignOperationKey("same"), "test.op", new { Value = 1 }, _ => Task.FromResult(new { Id = "b" }));
        Assert.Equal("b", replay.Id);
        var addA = new EfAddWorkflowDefinitionCommand(db, a, first, serializer, identities);
        var addB = new EfAddWorkflowDefinitionCommand(db, b, second, serializer, identities);
        await addA.Execute(new DesignOperationKey("add-a"), new WorkflowDefinition { Id = "shared", TenantId = "tenant-a", Name = "A" }, new WorkflowDefinitionDraft { Id = "draft-a", TenantId = "tenant-a", WorkflowDefinitionId = "shared", State = State() });
        await addB.Execute(new DesignOperationKey("add-b"), new WorkflowDefinition { Id = "shared", TenantId = "tenant-b", Name = "B" }, new WorkflowDefinitionDraft { Id = "draft-b", TenantId = "tenant-b", WorkflowDefinitionId = "shared", State = State() });
        Assert.Equal("A", (await new EfWorkflowDefinitionStore(db, a).GetAsync("shared")).Name);
        Assert.Equal("B", (await new EfWorkflowDefinitionStore(db, b).GetAsync("shared")).Name);
    }

    [Fact]
    public async Task Scope_less_mutations_are_rejected_and_corrupt_replays_fail_closed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var global = new TestAccessor(PersistenceAccessContext.Global);
        var writer = new EfDesignAtomicWriter(db, access: global);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ExecuteAsync(new DesignOperationKey("global"), "test.op", new { Value = 1 }, _ => Task.FromResult(new { Id = "x" })));

        var scoped = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var scopedWriter = new EfDesignAtomicWriter(db, access: scoped);
        await scopedWriter.ExecuteAsync(new DesignOperationKey("corrupt"), "test.op", new { Value = 1 }, _ => Task.FromResult(new { Id = "x" }));
        var marker = await db.Operations.SingleAsync(x => x.OperationKey == "corrupt"); marker.ResultJson = "{\"Id\":\"tampered\"}"; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => scopedWriter.ExecuteAsync(new DesignOperationKey("corrupt"), "test.op", new { Value = 1 }, _ => Task.FromResult(new { Id = "unused" })));
    }

    [Fact]
    public async Task Promotion_copies_layout_and_presentation_into_write_once_version_sibling()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(); var identities = new TestIdentity(); var writer = new EfDesignAtomicWriter(db, accessor);
        var definition = new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" };
        var draft = new WorkflowDefinitionDraft { Id = "draft", TenantId = "tenant-a", WorkflowDefinitionId = definition.Id, State = State() };
        await new EfAddWorkflowDefinitionCommand(db, accessor, writer, serializer, identities).Execute(new DesignOperationKey("add"), definition, draft, [new DesignMetadataRecord("root", 3, 4)], [new ActivityPresentationRecord("root", "Root", "Description")]);
        var versionId = await new EfPromoteDraftToVersionCommand(db, accessor, writer, serializer, identities).Execute(new DesignOperationKey("promote"), draft.Id, "1.0.0");
        var layout = await new EfWorkflowDefinitionVersionLayoutStore(db, accessor).FindByVersionIdAsync(versionId);
        Assert.NotNull(layout); Assert.Single(layout!.Records);
        Assert.Single(layout.ActivityPresentation);
    }

    [Fact]
    public async Task Failed_stage_does_not_leak_tracked_rows_into_the_next_operation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, accessor);
        await Assert.ThrowsAsync<InvalidDataException>(() => writer.ExecuteAsync<object>(new DesignOperationKey("failed"), "test.op", new { Value = 1 }, _ =>
        {
            db.Definitions.Add(new WorkflowDefinition { Id = "leaked", TenantId = "tenant-a", Name = "Should not persist" });
            throw new InvalidDataException("stage failed");
        }));
        Assert.Empty(db.ChangeTracker.Entries());
        await writer.ExecuteAsync(new DesignOperationKey("next"), "test.op", new { Value = 2 }, _ => Task.FromResult(new { Id = "next" }));
        Assert.Null(await db.Definitions.SingleOrDefaultAsync(x => x.Id == "leaked"));
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
