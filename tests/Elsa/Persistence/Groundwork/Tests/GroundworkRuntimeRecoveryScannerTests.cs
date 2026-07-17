using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkRuntimeRecoveryScannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Unfiltered_scan_uses_only_four_finite_signal_routes_and_returns_the_oldest_candidate()
    {
        var newest = State("wf-2", "op-2", "worker-1", leaseExpiresAt: Now.AddMinutes(-1));
        var oldest = State("wf-1", "op-1", "worker-1", leaseExpiresAt: Now.AddMinutes(-3));
        var bounded = new RecordingBoundedStore(new Dictionary<string, IReadOnlyList<DocumentEnvelope>>
        {
            [ElsaRuntimeStorageManifest.ListRecoveryByLeaseExpiryQuery] =
            [
                Envelope(oldest),
                Envelope(newest)
            ]
        });
        var scanner = new GroundworkRuntimeRecoveryScanner(
            new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create()),
            GroundworkTestSerialization.Serializer,
            bounded);

        var candidate = Assert.Single(await scanner.ScanAsync(Request(limit: 1)));

        Assert.Equal("wf-1", candidate.WorkflowExecutionId);
        Assert.Equal(
            [
                ElsaRuntimeStorageManifest.ListRecoveryDetectedQuery,
                ElsaRuntimeStorageManifest.ListRecoveryByLeaseExpiryQuery,
                ElsaRuntimeStorageManifest.ListRecoveryByLeaseAcquisitionQuery,
                ElsaRuntimeStorageManifest.ListRecoveryByHeartbeatQuery
            ],
            bounded.Queries.Select(query => query.QueryIdentity));
        Assert.All(bounded.Queries, query => Assert.Equal(1, query.Take));
        Assert.DoesNotContain(
            bounded.Queries,
            query => query.QueryIdentity == ElsaRuntimeStorageManifest.ListAllQuery);
    }

    [Fact]
    public async Task Owner_filtered_scan_uses_dedicated_owner_and_ownerless_routes()
    {
        var ownerless = new ExecutionLivenessState(
            "op-ownerless",
            "wf-ownerless",
            executionLease: null,
            heartbeat: null,
            drain: null,
            interruptedExecution: new InterruptedExecutionState(
                "interruption-1",
                "wf-ownerless",
                leaseId: null,
                lastCheckpointId: "checkpoint-1",
                RuntimeInterruptionReason.HostStopped,
                RuntimeInterruptionStatus.Detected,
                Now.AddMinutes(-2)));
        var envelope = Envelope(ownerless);
        var bounded = new RecordingBoundedStore(new Dictionary<string, IReadOnlyList<DocumentEnvelope>>
        {
            [ElsaRuntimeStorageManifest.ListRecoveryDetectedOwnerlessQuery] = [envelope],
            [ElsaRuntimeStorageManifest.ListRecoveryByLeaseExpiryAndOwnerQuery] = [envelope]
        });
        var scanner = new GroundworkRuntimeRecoveryScanner(
            new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create()),
            GroundworkTestSerialization.Serializer,
            bounded);

        var candidate = Assert.Single(await scanner.ScanAsync(Request(ownerId: "worker-1")));

        Assert.Equal("wf-ownerless", candidate.WorkflowExecutionId);
        Assert.Equal("InterruptedExecution", candidate.Metadata["runtime.recovery.source"]);
        Assert.Equal(6, bounded.Queries.Count);
        Assert.Contains(
            bounded.Queries,
            query => query.QueryIdentity == ElsaRuntimeStorageManifest.ListRecoveryDetectedOwnerlessQuery &&
                     query.Clauses.Any(clause => clause.Comparisons.Any(comparison =>
                         comparison.Path == ElsaRuntimeStorageManifest.RecoveryHasOperationalOwnerField &&
                         comparison.Values.SequenceEqual([bool.FalseString]))));
        Assert.Contains(
            bounded.Queries,
            query => query.QueryIdentity == ElsaRuntimeStorageManifest.ListRecoveryByHeartbeatAndOwnerQuery &&
                     query.Clauses.Any(clause => clause.Comparisons.Any(comparison =>
                         comparison.Path == ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField &&
                         comparison.Values.SequenceEqual(["worker-1"]))));
    }

    [Fact]
    public async Task Runtime_test_bounded_store_admits_unfiltered_and_owner_aware_recovery_routes()
    {
        var documents = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var bounded = new Testing.RuntimeTestBoundedDocumentStore(documents);
        var states = new GroundworkExecutionLivenessStateStore(
            documents,
            GroundworkTestSerialization.Serializer,
            bounded);
        var scanner = new GroundworkRuntimeRecoveryScanner(
            documents,
            GroundworkTestSerialization.Serializer,
            bounded);
        var ownerless = new ExecutionLivenessState(
            "op-ownerless",
            "wf-ownerless",
            executionLease: null,
            heartbeat: null,
            drain: null,
            interruptedExecution: new InterruptedExecutionState(
                "interruption-1",
                "wf-ownerless",
                leaseId: null,
                lastCheckpointId: "checkpoint-1",
                RuntimeInterruptionReason.HostStopped,
                RuntimeInterruptionStatus.Detected,
                Now.AddMinutes(-2)));
        await states.SaveAsync(ownerless);

        var unfiltered = Assert.Single(await scanner.ScanAsync(Request()));
        var ownerAware = Assert.Single(await scanner.ScanAsync(Request(ownerId: "worker-1")));

        Assert.Equal("wf-ownerless", unfiltered.WorkflowExecutionId);
        Assert.Equal("wf-ownerless", ownerAware.WorkflowExecutionId);
    }

    private static RuntimeRecoveryScanRequest Request(
        string? ownerId = null,
        int limit = 10) =>
        new(
            Now,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            limit,
            ownerId);

    private static ExecutionLivenessState State(
        string workflowExecutionId,
        string operationalStateId,
        string ownerId,
        DateTimeOffset leaseExpiresAt) =>
        new(
            operationalStateId,
            workflowExecutionId,
            new RuntimeExecutionLease(
                $"lease-{operationalStateId}",
                workflowExecutionId,
                ownerId,
                leaseExpiresAt.AddMinutes(-1),
                leaseExpiresAt,
                1),
            heartbeat: null,
            drain: null,
            interruptedExecution: null);

    private static DocumentEnvelope Envelope(ExecutionLivenessState state)
    {
        var document = new
        {
            Collection = ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
            state.WorkflowExecutionId,
            HasOperationalOwner = state.ExecutionLease is not null || state.Heartbeat is not null,
            State = state
        };
        var (schemaVersion, content) = GroundworkTestSerialization.Serializer.Serialize(
            ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
            document);
        return new DocumentEnvelope(
            ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
            DocumentId.Compose(state.WorkflowExecutionId, state.OperationalStateId),
            schemaVersion,
            1,
            content,
            Now,
            Now);
    }

    private sealed class RecordingBoundedStore(
        IReadOnlyDictionary<string, IReadOnlyList<DocumentEnvelope>> results)
        : IBoundedDocumentStore
    {
        public List<DocumentQuery> Queries { get; } = [];

        public Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Queries.Add(query);
            var documents = results.TryGetValue(query.QueryIdentity, out var configured)
                ? configured.Take(query.Take ?? int.MaxValue).ToArray()
                : [];
            return Task.FromResult(new DocumentQueryResult(documents, documents.Length));
        }

        public Task<long> CountAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AnyAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
