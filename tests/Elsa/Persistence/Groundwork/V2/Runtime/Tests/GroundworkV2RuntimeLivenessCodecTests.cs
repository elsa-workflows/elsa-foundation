using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2RuntimeLivenessCodecTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField)]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField)]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField)]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField)]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField)]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField)]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField)]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryHasOperationalOwnerField)]
    public async Task Codec_rejects_every_drifted_recovery_projection(string field)
    {
        var state = State();
        var (store, session) = CreateStore();
        await store.SaveAsync(state);

        session.Replace(field, Drift(field, state));

        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.FindAsync(state.WorkflowExecutionId, state.OperationalStateId));
    }

    [Theory]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField)]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField)]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField)]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField)]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField)]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField)]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField)]
    [InlineData(ElsaRuntimeV2StorageManifest.RecoveryHasOperationalOwnerField)]
    public async Task Codec_accepts_equivalent_provider_projection_shapes(string field)
    {
        var state = State();
        var (store, session) = CreateStore();
        await store.SaveAsync(state);

        session.Replace(field, ProviderShape(field, state));

        var found = await store.FindAsync(state.WorkflowExecutionId, state.OperationalStateId);
        Assert.NotNull(found);
        Assert.Equal(state.OperationalStateId, found!.OperationalStateId);
    }

    private static (IExecutionLivenessStateStore Store, MemorySession Session) CreateStore()
    {
        var declared = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind);
        var suffix = Guid.NewGuid().ToString("N");
        var unit = declared with
        {
            Id = new StorageUnitId($"{declared.Id.Value}-{suffix}"),
            Name = $"{declared.Name}_{suffix}"
        };
        var session = new MemorySession(unit);
        var source = new MemorySessionSource(session, unit);
        var accessor = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        return (new GroundworkV2ExecutionLivenessStateStore(source, accessor), session);
    }

    private static ExecutionLivenessState State() =>
        new(
            "op-1",
            "wf-1",
            new RuntimeExecutionLease("lease-1", "wf-1", "worker-a", Now.AddMinutes(-1), Now.AddMinutes(5), 1),
            new RuntimeHeartbeat("heartbeat-1", "wf-1", "worker-a", "lease-1", Now.AddMinutes(-1)),
            drain: null,
            new InterruptedExecutionState(
                "interrupt-1",
                "wf-1",
                "lease-1",
                "checkpoint-1",
                RuntimeInterruptionReason.HostStopped,
                RuntimeInterruptionStatus.Detected,
                Now.AddMinutes(-3)));

    private static object Drift(string field, ExecutionLivenessState state) => field switch
    {
        ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField => (int)state.InterruptedExecution!.Status + 1,
        ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField => state.InterruptedExecution!.InterruptedAt.AddTicks(1),
        ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField => "worker-b",
        ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField => state.ExecutionLease!.AcquiredAt.AddTicks(1),
        ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField => state.ExecutionLease!.ExpiresAt.AddTicks(1),
        ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField => "worker-b",
        ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField => state.Heartbeat!.RecordedAt.AddTicks(1),
        ElsaRuntimeV2StorageManifest.RecoveryHasOperationalOwnerField => false,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };

    private static object ProviderShape(string field, ExecutionLivenessState state) => field switch
    {
        ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField => (long)state.InterruptedExecution!.Status,
        ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField => state.InterruptedExecution!.InterruptedAt.UtcDateTime,
        ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField => JsonString(state.ExecutionLease!.OwnerId),
        ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField => state.ExecutionLease!.AcquiredAt.UtcDateTime,
        ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField => state.ExecutionLease!.ExpiresAt.UtcDateTime,
        ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField => JsonString(state.Heartbeat!.OwnerId),
        ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField => state.Heartbeat!.RecordedAt.UtcDateTime,
        ElsaRuntimeV2StorageManifest.RecoveryHasOperationalOwnerField => JsonBoolean(true),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };

    private static JsonElement JsonString(string value) => JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement.Clone();

    private static JsonElement JsonBoolean(bool value) => JsonDocument.Parse(value ? "true" : "false").RootElement.Clone();

    private sealed class TestAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class MemorySessionSource(MemorySession session, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return session;
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) =>
            throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, unitId);
            return unit;
        }
    }

    private sealed class MemorySession(StorageUnit unit) : SynchronousStorageSessionTestDouble, IStorageSession
    {
        private StoredEntry? entry;

        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = StorageAccess.Scoped(new StorageScope("tenant-a"));

        public StoredEntry? Read(StorageKey key) => entry;

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) => throw new NotSupportedException();
        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();

        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => Write(values, WriteOutcomeStatus.Inserted);
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => Write(values, WriteOutcomeStatus.Updated);
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => Write(values, WriteOutcomeStatus.Upserted);
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();

        public void Replace(string field, object? value)
        {
            Assert.NotNull(entry);
            var values = new Dictionary<string, object?>(entry!.Values.Values, StringComparer.Ordinal)
            {
                [field] = value
            };
            entry = new StoredEntry(new StorageValues(values), entry.Version);
        }

        private WriteOutcome Write(StorageValues values, WriteOutcomeStatus status)
        {
            entry = new StoredEntry(values, 1);
            return new WriteOutcome(status, 1);
        }
    }
}
