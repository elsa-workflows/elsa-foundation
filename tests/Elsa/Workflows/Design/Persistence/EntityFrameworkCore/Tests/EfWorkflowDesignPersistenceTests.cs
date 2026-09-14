using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Atomic;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Locking.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
            var version = new EfAddWorkflowDefinitionVersionCommand(db, accessor, atomic, serializer, identities, new TestLockProvider()); var added = await version.Execute(new DesignOperationKey("version-1"), definition.Id, State()); Assert.Equal("1.0.0", added.Version);
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
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))); var writer = new EfDesignAtomicWriter(db, access); var key = new DesignOperationKey("same"); var first = await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "winner" })); var replay = await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "loser" })); Assert.Equal(first.Id, replay.Id); await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ExecuteAsync(key, "test.op", new { Value = 2 }, ["test"], _ => Task.FromResult(new { Id = "conflict" })));
    }

    [Fact]
    public async Task Operation_fingerprint_is_canonical_across_request_property_order()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))); IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var key = new DesignOperationKey("canonical-request");
        await writer.ExecuteAsync(key, "test.op", new { A = "é", B = "東京" }, ["test"], (_, _) => Task.FromResult(DesignAtomicWriteStage<string>.Accepted("winner")));
        var replay = await writer.ExecuteAsync<string>(key, "test.op", new { B = "東京", A = "é" }, ["test"], (_, _) => throw new InvalidOperationException("canonical replay must not restage"));
        Assert.Equal(DesignAtomicWriteStatus.Replayed, replay.Status);
        Assert.Equal("winner", replay.Value);
    }

    [Fact]
    public async Task State_request_material_uses_the_configured_payload_serializer_and_groundwork_framing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))); var state = State();
        var serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = null }; var serializer = new TestSerializer(serializerOptions); var identity = new TestIdentity(); var writer = new EfDesignAtomicWriter(db, access);
        db.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" }); await db.SaveChangesAsync();
        await new EfAddWorkflowDefinitionVersionCommand(db, access, writer, serializer, identity, new TestLockProvider()).Execute(new DesignOperationKey("serializer-request"), "definition", state);
        var marker = await db.Operations.SingleAsync(x => x.OperationKey == "serializer-request");
        var stateJson = serializer.Serialize(state);
        var materialJson = JsonSerializer.Serialize(new { definitionId = "definition", stateJson });
        Assert.Equal(GroundworkFingerprint("workflow.version.add.v1", materialJson), marker.RequestFingerprint);
    }

    [Fact]
    public async Task Projection_batches_definition_ids_without_truncating_rows_and_preserves_scope_and_order()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var tenant = "tenant-a"; var definitions = Enumerable.Range(0, 205).Select(index => new WorkflowDefinition { Id = $"definition-{index:D3}", TenantId = tenant, Name = $"Definition {index:D3}" }).ToArray();
        db.Definitions.AddRange(definitions);
        var oldDraft = new WorkflowDefinitionDraft { Id = "draft-old", TenantId = tenant, WorkflowDefinitionId = definitions[0].Id, CreatedAt = DateTimeOffset.UnixEpoch, LastModifiedAt = DateTimeOffset.UnixEpoch.AddDays(1), StateSource = "{}" };
        var currentDraft = new WorkflowDefinitionDraft { Id = "draft-current", TenantId = tenant, WorkflowDefinitionId = definitions[0].Id, CreatedAt = DateTimeOffset.UnixEpoch.AddDays(2), LastModifiedAt = DateTimeOffset.UnixEpoch.AddDays(1), StateSource = "{}" };
        db.Drafts.AddRange([oldDraft, currentDraft]);
        foreach (var definition in definitions.Skip(1))
            db.Drafts.Add(new WorkflowDefinitionDraft { Id = $"draft-{definition.Id}", TenantId = tenant, WorkflowDefinitionId = definition.Id, StateSource = "{}" });
        db.Versions.AddRange(Enumerable.Range(1, 201).Select(number => new WorkflowDefinitionVersion(definitions[0].Id, $"{number}.0.0") { Id = $"version-{number:D3}", TenantId = tenant }));
        foreach (var definition in definitions.Skip(1))
            db.Versions.Add(new WorkflowDefinitionVersion(definition.Id, "1.0.0") { Id = $"version-{definition.Id}", TenantId = tenant });
        db.Definitions.Add(new WorkflowDefinition { Id = "tenant-b-only", TenantId = "tenant-b", Name = "Foreign" });
        db.Versions.Add(new WorkflowDefinitionVersion("tenant-b-only", "9.0.0") { Id = "foreign-version", TenantId = "tenant-b" });
        await db.SaveChangesAsync();

        var requested = definitions.Select(x => x.Id).Reverse().Append("tenant-b-only").Append(definitions[0].Id).ToArray();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        var projections = await new EfWorkflowDefinitionListProjectionStore(db, access).ListByDefinitionIdsAsync(requested);
        Assert.Equal(206, projections.Count);
        Assert.Equal(requested.Distinct(StringComparer.Ordinal), projections.Select(x => x.WorkflowDefinitionId));
        var first = Assert.Single(projections, x => x.WorkflowDefinitionId == definitions[0].Id);
        Assert.Equal("draft-current", first.DraftId);
        Assert.Equal("version-201", first.LatestVersionId);
        Assert.Equal("201.0.0", first.LatestVersion);
        Assert.Equal(201, first.VersionCount);
        var foreign = Assert.Single(projections, x => x.WorkflowDefinitionId == "tenant-b-only");
        Assert.Null(foreign.DraftId); Assert.Null(foreign.LatestVersionId); Assert.Null(foreign.LatestVersion); Assert.Equal(0, foreign.VersionCount);
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
        await first.ExecuteAsync(new DesignOperationKey("same"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "a" }));
        var replay = await second.ExecuteAsync(new DesignOperationKey("same"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "b" }));
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ExecuteAsync(new DesignOperationKey("global"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "x" })));

        var scoped = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var scopedWriter = new EfDesignAtomicWriter(db, access: scoped);
        await scopedWriter.ExecuteAsync(new DesignOperationKey("corrupt"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "x" }));
        var marker = await db.Operations.SingleAsync(x => x.OperationKey == "corrupt"); marker.ResultJson = "{\"Id\":\"tampered\"}"; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => scopedWriter.ExecuteAsync(new DesignOperationKey("corrupt"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "unused" })));
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
        var versionId = await new EfPromoteDraftToVersionCommand(db, accessor, writer, serializer, identities, new TestLockProvider()).Execute(new DesignOperationKey("promote"), draft.Id, "1.0.0");
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
        await Assert.ThrowsAsync<InvalidDataException>(() => writer.ExecuteAsync<object>(new DesignOperationKey("failed"), "test.op", new { Value = 1 }, ["test"], _ =>
        {
            db.Definitions.Add(new WorkflowDefinition { Id = "leaked", TenantId = "tenant-a", Name = "Should not persist" });
            throw new InvalidDataException("stage failed");
        }));
        Assert.Empty(db.ChangeTracker.Entries());
        await writer.ExecuteAsync(new DesignOperationKey("next"), "test.op", new { Value = 2 }, ["test"], _ => Task.FromResult(new { Id = "next" }));
        Assert.Null(await db.Definitions.SingleOrDefaultAsync(x => x.Id == "leaked"));
    }

    [Fact]
    public async Task Direct_atomic_interface_returns_conflict_and_rejects_empty_mutation_units()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var key = new DesignOperationKey("direct");
        await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1)));
        var conflict = await writer.ExecuteAsync(key, "test.op", new { Value = 2 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(2)));
        Assert.Equal(DesignAtomicWriteStatus.Conflict, conflict.Status);
        await Assert.ThrowsAsync<ArgumentException>(() => writer.ExecuteAsync(
            new DesignOperationKey("empty"), "test.op", new { Value = 1 }, [],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1))));
    }

    [Fact]
    public async Task Replay_and_conflict_are_resolved_before_before_attempt_work()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var key = new DesignOperationKey("preflight-order");
        await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1)));
        var beforeAttemptCalls = 0;

        var replay = await writer.ExecuteAsync<int>(key, "test.op", new { Value = 1 }, ["test"],
            (_, _) => throw new InvalidOperationException("stage must not run"),
            _ =>
            {
                beforeAttemptCalls++;
                return Task.CompletedTask;
            });
        var conflict = await writer.ExecuteAsync<int>(key, "test.op", new { Value = 2 }, ["test"],
            (_, _) => throw new InvalidOperationException("stage must not run"),
            _ =>
            {
                beforeAttemptCalls++;
                return Task.CompletedTask;
            });

        Assert.Equal(DesignAtomicWriteStatus.Replayed, replay.Status);
        Assert.Equal(DesignAtomicWriteStatus.Conflict, conflict.Status);
        Assert.Equal(0, beforeAttemptCalls);
    }

    [Fact]
    public async Task Ef_rejects_authoritative_result_that_differs_from_staged_value()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access);
        var suppliedJson = JsonSerializer.Serialize(new ResultValue("supplied"));
        await Assert.ThrowsAsync<InvalidDataException>(() => writer.ExecuteAsync(
            new DesignOperationKey("mismatch"), "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<ResultValue>.Accepted(
                new ResultValue("staged"), "sha256:invalid", suppliedJson))));
        Assert.Empty(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Ef_honors_custom_result_codec_for_authoritative_validation_and_replay()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        var value = new CustomResult(CustomResultStatus.Ready);
        var json = JsonSerializer.Serialize(value, options);
        var fingerprint = GroundworkFingerprint("test.op.result", json);
        var codec = new CustomResultCodec(options);

        var committed = await writer.ExecuteAsync(
            new DesignOperationKey("custom-codec"), "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<CustomResult>.Accepted(value, fingerprint, json)),
            resultCodec: codec);
        var replayed = await writer.ExecuteAsync<CustomResult>(
            new DesignOperationKey("custom-codec"), "test.op", new { Value = 1 }, ["test"],
            (_, _) => throw new InvalidOperationException("replay must not restage"),
            resultCodec: codec);

        Assert.Equal(DesignAtomicWriteStatus.Committed, committed.Status);
        Assert.Equal(DesignAtomicWriteStatus.Replayed, replayed.Status);
        Assert.Equal(value, committed.Value);
        Assert.Equal(value, replayed.Value);
    }

    [Fact]
    public async Task Ef_reconciles_a_commit_acknowledgement_failure_without_rerunning_stage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var interceptor = new AcknowledgementLostInterceptor();
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access, reconciliationTimeout: TimeSpan.FromMilliseconds(250));
        var stageCalls = 0;
        interceptor.FailNextCommit = true;

        var result = await writer.ExecuteAsync(
            new DesignOperationKey("ack-lost"), "test.op", new { Value = 1 }, ["test"],
            (_, _) =>
            {
                stageCalls++;
                return Task.FromResult(DesignAtomicWriteStage<ResultValue>.Accepted(new ResultValue("durable")));
            });

        Assert.Equal(DesignAtomicWriteStatus.Reconciled, result.Status);
        Assert.Equal(1, stageCalls);
        Assert.Single(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Shared_protocol_rolls_back_when_commit_is_rejected()
    {
        var scope = new ProtocolScope();
        var rollbackCount = 0;
        var lane = new DesignAtomicWriteLane<ProtocolScope, ProtocolMarker, ProtocolStage, ProtocolResult>
        {
            MarkerId = "marker",
            LoadMarker = _ => Task.FromResult<ProtocolMarker?>(null),
            BeginScope = () => scope,
            SaveMarker = (_, _, _) => Task.CompletedTask,
            Commit = (_, _) => Task.FromResult(DesignAtomicCommitDisposition.Rejected),
            Rollback = _ => rollbackCount++,
            ClassifyMarkerRace = _ => false,
            ClassifyUncertainCommit = _ => false,
            OnUncertainCommit = (_, _) => throw new InvalidOperationException(),
            TryReconcileAfterCommit = (_, _) => Task.FromResult<ProtocolResult?>(null),
            Delay = (_, _) => Task.CompletedTask,
            IsAccepted = stage => stage.Accepted,
            OnCommitted = _ => new ProtocolResult("committed"),
            OnReplay = _ => new ProtocolResult("replayed"),
            OnRejected = () => new ProtocolResult("rejected")
        };

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            lane,
            (_, _) => Task.FromResult(new ProtocolStage(true)),
            null,
            CancellationToken.None);

        Assert.Equal("rejected", result.Status);
        Assert.Equal(1, rollbackCount);
        Assert.True(scope.Disposed);
    }


    private static WorkflowsDesignSqliteDbContext Create(SqliteConnection connection) => new(new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>().UseSqlite(connection).Options);
    private static WorkflowDefinitionState State() => new([], null, [], [], null);
    private sealed class TestIdentity : IIdentityGenerator { private int n; public string Generate() => $"generated-{Interlocked.Increment(ref n)}"; }
    private sealed class TestLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle AcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
        private sealed class Handle : IDistributedSynchronizationHandle { public CancellationToken HandleLostToken => CancellationToken.None; public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
    private sealed class TestAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => current; }
    private sealed record ResultValue(string Value);
    private enum CustomResultStatus { Ready }
    private sealed record CustomResult(CustomResultStatus Status);
    private sealed class CustomResultCodec(JsonSerializerOptions options) : IDesignAtomicWriteResultCodec<CustomResult>
    {
        public CustomResult Deserialize(string json) => JsonSerializer.Deserialize<CustomResult>(json, options)!;
        public bool Equivalent(CustomResult left, CustomResult right) => left == right;
    }

    private static string GroundworkFingerprint(string operationKind, string json)
    {
        var identity = "elsa-design-material:v1";
        var material = $"{Encoding.UTF8.GetByteCount(identity)}:{identity}{Encoding.UTF8.GetByteCount(operationKind)}:{operationKind}1:1{Encoding.UTF8.GetByteCount(json)}:{json}";
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))}";
    }
    private sealed class ProtocolScope : IDisposable { public bool Disposed { get; private set; } public void Dispose() => Disposed = true; }
    private sealed class ProtocolMarker { }
    private sealed record ProtocolStage(bool Accepted);
    private sealed record ProtocolResult(string Status);
    private sealed class AcknowledgementLostInterceptor : DbTransactionInterceptor
    {
        public bool FailNextCommit { get; set; }

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        {
            if (!FailNextCommit)
                return;
            FailNextCommit = false;
            throw new InvalidOperationException("commit acknowledgement lost");
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!FailNextCommit)
                return Task.CompletedTask;
            FailNextCommit = false;
            return Task.FromException(new InvalidOperationException("commit acknowledgement lost"));
        }
    }
    private sealed class TestSerializer(JsonSerializerOptions? serializerOptions = null) : IPayloadSerializer
    {
        private readonly JsonSerializerOptions options = serializerOptions ?? new();
        public string Serialize(object payload) => JsonSerializer.Serialize(payload, options);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, options);
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<JsonElement>(serializedData);
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type)!;
        public object Deserialize(JsonElement serializedData) => serializedData;
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData)!;
        public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(options)!;
        public JsonSerializerOptions GetOptions() => options;
    }
}
