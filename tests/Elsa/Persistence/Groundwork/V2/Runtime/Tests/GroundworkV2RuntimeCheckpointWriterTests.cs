using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2RuntimeCheckpointWriterTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };

    static GroundworkV2RuntimeCheckpointWriterTests()
    {
        Json.Converters.Add(new JsonStringEnumConverter());
    }

    [Fact]
    public async Task Failed_batch_rolls_back_and_marker_remains_reusable()
    {
        var source = new MemorySource { FailCommitBeforeApply = true };
        var writer = NewWriter(source);
        var commit = NewCommit("rollback");

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, Immediate()).AsTask());
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "rollback", "tenant-a"));

        source.FailCommitBeforeApply = false;
        var result = await writer.CommitAsync(commit, Immediate());
        Assert.Empty(result.PendingPostCommitWorkIds);
        Assert.Equal(2, source.UnitOfWorkCount);
    }

    [Fact]
    public async Task Replay_is_idempotent_and_conflicting_payload_is_rejected()
    {
        var source = new MemorySource();
        var writer = NewWriter(source);
        var commit = NewCommit("replay");

        await writer.CommitAsync(commit, Immediate());
        await writer.CommitAsync(commit, Immediate());
        Assert.Equal(1, source.UnitOfWorkCount);

        var conflicting = commit with
        {
            Checkpoint = commit.Checkpoint with { Name = "different" }
        };
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(
            () => writer.CommitAsync(conflicting, Immediate()).AsTask());
        Assert.Equal(1, source.UnitOfWorkCount);
    }

    [Fact]
    public async Task Ambiguous_acknowledgement_reconciles_through_the_marker()
    {
        var source = new MemorySource { ThrowAfterApply = true };
        var writer = NewWriter(source);

        var result = await writer.CommitAsync(NewCommit("ambiguous"), Immediate());

        Assert.Empty(result.PendingPostCommitWorkIds);
        Assert.Equal(1, source.UnitOfWorkCount);
        Assert.NotNull(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "ambiguous", "tenant-a"));
    }

    [Fact]
    public async Task Marker_and_rows_are_isolated_by_the_explicit_scope()
    {
        var source = new MemorySource();
        var tenantA = NewWriter(source, "tenant-a");
        var tenantB = NewWriter(source, "tenant-b");
        var commit = NewCommit("scoped");

        await tenantA.CommitAsync(commit, Immediate());
        await tenantB.CommitAsync(commit, Immediate());

        Assert.NotNull(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "scoped", "tenant-a"));
        Assert.NotNull(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "scoped", "tenant-b"));
        Assert.Equal(2, source.UnitOfWorkCount);
    }

    [Fact]
    public async Task Unsupported_provider_is_refused_before_opening_a_unit_of_work()
    {
        var source = new MemorySource { AdvertiseAtomicCommit = false };
        var writer = NewWriter(source);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => writer.CommitAsync(NewCommit("unsupported"), Immediate()).AsTask());
        Assert.Equal(0, source.UnitOfWorkCount);
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "unsupported", "tenant-a"));
    }

    [Fact]
    public async Task A_stale_execution_fence_is_rejected_before_any_checkpoint_row_is_staged()
    {
        var source = new MemorySource();
        source.SeedLiveness(new ExecutionLivenessState(
            "ownership:workflow-1",
            "workflow-1",
            new RuntimeExecutionLease(
                "lease-current",
                "workflow-1",
                "owner-current",
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5),
                2),
            null,
            null,
            null));
        var writer = NewWriter(source);
        var commit = NewCommit("stale") with
        {
            ExpectedFence = new RuntimeExecutionFence("lease-old", "owner-old", 1)
        };

        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => writer.CommitAsync(commit, Immediate()).AsTask());
        Assert.Empty(source.LastUnitOfWork!.Staged);
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "stale", "tenant-a"));
    }

    [Fact]
    public async Task The_workflow_row_precedes_the_create_only_marker()
    {
        var source = new MemorySource();
        var writer = NewWriter(source);
        var execution = new WorkflowExecutionState(
            "workflow-1",
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Running,
            null,
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            "tenant-a",
            new Dictionary<string, string>());
        var stateChanges = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>("workflow-1", RuntimeStateChangeOperation.Upsert, execution, new Dictionary<string, string>()),
            null,
            [],
            [],
            [],
            [],
            []);

        await writer.CommitAsync(NewCommit("ordered") with { StateChanges = stateChanges }, Immediate());

        var staged = source.LastUnitOfWork!.Staged;
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind, staged[0].Unit.Id.Value);
        Assert.Equal(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, staged[^1].Unit.Id.Value);
        Assert.Equal(RowWriteMode.Insert, staged[^1].Mode);
        Assert.Equal(WritePreconditionKind.CreateOnly, staged[^1].Options.Precondition.Kind);
    }

    private static GroundworkV2RuntimeCheckpointWriter NewWriter(MemorySource source, string scope = "tenant-a") =>
        new(source, new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope(scope))));

    private static RuntimeCheckpointPersistenceDecision Immediate() =>
        new(RuntimeCheckpointPersistenceMode.Immediate);

    private static RuntimeCheckpointCommit NewCommit(string commitId) =>
        new(
            commitId,
            new RuntimeCheckpoint(
                $"checkpoint-{commitId}",
                "runtime",
                "workflow-1",
                DateTimeOffset.UtcNow,
                [],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            [],
            new Dictionary<string, string>());

    private sealed class Accessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class MemorySource : IGroundworkStorageSessionSource, IGroundworkStorageCapabilitySource
    {
        private readonly MemoryBacking backing = new();
        public bool AdvertiseAtomicCommit { get; init; } = true;
        public bool FailCommitBeforeApply { get; set; }
        public bool ThrowAfterApply { get; set; }
        public int UnitOfWorkCount { get; private set; }
        public MemoryUnitOfWork? LastUnitOfWork { get; private set; }

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) =>
            AdvertiseAtomicCommit ? WellKnownCapabilities.All : [];

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            new MemorySession(ElsaRuntimeV2StorageManifest.Require(unitId), access, backing);

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null)
        {
            UnitOfWorkCount++;
            LastUnitOfWork = new MemoryUnitOfWork(access, backing, this);
            return LastUnitOfWork;
        }

        public StorageUnit Unit(string unitId, string? targetName = null) => ElsaRuntimeV2StorageManifest.Require(unitId);

        public StoredEntry? Find(string unitId, string id, string scope) =>
            backing.Read(scope, unitId, id);

        public void SeedLiveness(ExecutionLivenessState state)
        {
            var operationalStateId = state.OperationalStateId;
            var identity = $"{state.WorkflowExecutionId.Length}:{state.WorkflowExecutionId}{operationalStateId}";
            var content = JsonSerializer.Serialize(new
            {
                collection = ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind,
                workflowExecutionId = state.WorkflowExecutionId,
                hasOperationalOwner = true,
                state
            }, Json);
            var values = GroundworkRuntimeRowStore.Values(
                identity,
                ElsaRuntimeV2StorageManifest.SchemaVersion,
                content,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.WorkflowExecutionId,
                    [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind,
                    [ElsaRuntimeV2StorageManifest.ExecutionLivenessOperationalStateIdField] = operationalStateId,
                    [ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField] = null,
                    [ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField] = null,
                    [ElsaRuntimeV2StorageManifest.RecoveryHasOperationalOwnerField] = true,
                    [ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField] = state.ExecutionLease!.OwnerId,
                    [ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField] = state.ExecutionLease.AcquiredAt,
                    [ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField] = state.ExecutionLease.ExpiresAt,
                    [ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField] = null,
                    [ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField] = null
                });
            backing.Write("tenant-a", ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, identity, values, 1);
        }

        public sealed class MemoryBacking
        {
            private readonly Dictionary<(string Scope, string Unit, string Id), StoredEntry> rows = [];

            public StoredEntry? Read(string scope, string unit, string id) =>
                rows.GetValueOrDefault((scope, unit, id));

            public void Write(string scope, string unit, string id, StorageValues values, long version) =>
                rows[(scope, unit, id)] = new StoredEntry(values, version);

            public void Delete(string scope, string unit, string id) => rows.Remove((scope, unit, id));
        }

        private sealed class MemorySession(StorageUnit unit, StorageAccess access, MemoryBacking backing) : IStorageSession
        {
            public StorageUnit Unit { get; } = unit;
            public StorageAccess Access { get; } = access;

            public StoredEntry? Read(StorageKey key) =>
                backing.Read(Access.Scope!.Value, Unit.Id.Value, (string)key.Values[ElsaRuntimeV2StorageManifest.IdField]!);

            public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) => throw new NotSupportedException();
            public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
            public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
            public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
            public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
            public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
            public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
        }

        public sealed class MemoryUnitOfWork(
            StorageAccess access,
            MemoryBacking backing,
            MemorySource owner) : IUnitOfWork
        {
            public List<RowWrite> Staged { get; } = [];
            private bool rolledBack;

            public IStorageSession OpenSession(StorageUnit unit) => new MemorySession(unit, access, backing);

            public void Stage(RowWrite write)
            {
                if (rolledBack)
                    throw new InvalidOperationException("The unit of work has rolled back.");
                Staged.Add(write);
            }

            public BatchWriteSummary Commit() => CommitWithOutcomes().Summary;

            public BatchWriteReport CommitWithOutcomes()
            {
                if (owner.FailCommitBeforeApply)
                    throw new InvalidOperationException("simulated atomic failure");
                foreach (var write in Staged)
                    Apply(write);
                if (owner.ThrowAfterApply)
                {
                    owner.ThrowAfterApply = false;
                    throw new InvalidOperationException("simulated ambiguous acknowledgement");
                }
                return new BatchWriteReport(Staged.Select(write => new RowWriteOutcome(write, new WriteOutcome(
                    write.Mode == RowWriteMode.Delete ? WriteOutcomeStatus.Deleted : WriteOutcomeStatus.Upserted, 1))).ToArray());
            }

            public ValueTask<BatchWriteReport> CommitWithOutcomesAsync(CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(CommitWithOutcomes());

            public ValueTask<BatchWriteSummary> CommitAsync(CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(CommitWithOutcomes().Summary);

            public void Rollback()
            {
                rolledBack = true;
                Staged.Clear();
            }

            public void Dispose() { }

            private void Apply(RowWrite write)
            {
                var id = write.Mode == RowWriteMode.Delete
                    ? (string)write.Key!.Values[ElsaRuntimeV2StorageManifest.IdField]!
                    : (string)write.Values!.Values[ElsaRuntimeV2StorageManifest.IdField]!;
                var existing = backing.Read(access.Scope!.Value, write.Unit.Id.Value, id);
                if (write.Options.Precondition.Kind == WritePreconditionKind.CreateOnly && existing is not null)
                    throw new InvalidOperationException("create-only conflict");
                if (write.Mode == RowWriteMode.Delete)
                {
                    backing.Delete(access.Scope.Value, write.Unit.Id.Value, id);
                    return;
                }

                var version = (existing?.Version ?? 0) + 1;
                backing.Write(access.Scope.Value, write.Unit.Id.Value, id, write.Values!, version);
            }
        }
    }
}
