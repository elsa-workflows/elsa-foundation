using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class RuntimeRecoveryBoundedRouteTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Providers => new()
    {
        "sqlite",
        "sqlserver",
        "postgresql",
        "mongodb"
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Recovery_routes_filter_order_and_limit_on_every_persistent_provider(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([new RecoveryManifestSource()]);

        await using var client = await driver.OpenPhysicalClientAsync();
        var boundedStore = client.BoundedDocumentStore
                           ?? throw new InvalidOperationException(
                               "The physical provider did not expose its admitted bounded-query runtime.");
        var store = new GroundworkExecutionLivenessStateStore(
            client.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            boundedStore);
        await store.SaveAsync(ExpiredLease("wf-1", "owner-a", Now.AddMinutes(-3)));
        await store.SaveAsync(StaleHeartbeat("wf-2", "owner-a", Now.AddMinutes(-2)));
        await store.SaveAsync(DetectedOwnerless("wf-3", Now.AddMinutes(-4)));
        await store.SaveAsync(ExpiredLease("wf-4", "owner-b", Now.AddMinutes(-5)));
        await store.SaveAsync(Live("wf-5", "owner-a"));
        var scanner = new GroundworkRuntimeRecoveryScanner(
            client.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            boundedStore);

        var unfiltered = await scanner.ScanAsync(Request(limit: 2));
        var ownerA = await scanner.ScanAsync(Request(ownerId: "owner-a", limit: 2));
        var ownerB = await scanner.ScanAsync(Request(ownerId: "owner-b", limit: 2));

        Assert.Equal(["wf-4", "wf-3"], unfiltered.Select(candidate => candidate.WorkflowExecutionId));
        Assert.Equal(["wf-3", "wf-1"], ownerA.Select(candidate => candidate.WorkflowExecutionId));
        Assert.Equal(["wf-4", "wf-3"], ownerB.Select(candidate => candidate.WorkflowExecutionId));
        Assert.DoesNotContain(
            unfiltered.Concat(ownerA).Concat(ownerB),
            candidate => candidate.WorkflowExecutionId == "wf-5");
    }

    private static RuntimeRecoveryScanRequest Request(string? ownerId = null, int limit = 10) =>
        new(
            Now,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            limit,
            ownerId);

    private static ExecutionLivenessState ExpiredLease(
        string workflowExecutionId,
        string ownerId,
        DateTimeOffset expiresAt) =>
        State(
            workflowExecutionId,
            new RuntimeExecutionLease(
                $"lease-{workflowExecutionId}",
                workflowExecutionId,
                ownerId,
                expiresAt.AddMinutes(-1),
                expiresAt,
                1),
            heartbeat: null,
            interruptedExecution: null);

    private static ExecutionLivenessState StaleHeartbeat(
        string workflowExecutionId,
        string ownerId,
        DateTimeOffset recordedAt) =>
        State(
            workflowExecutionId,
            new RuntimeExecutionLease(
                $"lease-{workflowExecutionId}",
                workflowExecutionId,
                ownerId,
                Now.AddMinutes(-1),
                Now.AddMinutes(5),
                1),
            new RuntimeHeartbeat(
                $"heartbeat-{workflowExecutionId}",
                workflowExecutionId,
                ownerId,
                $"lease-{workflowExecutionId}",
                recordedAt),
            interruptedExecution: null);

    private static ExecutionLivenessState DetectedOwnerless(
        string workflowExecutionId,
        DateTimeOffset interruptedAt) =>
        State(
            workflowExecutionId,
            lease: null,
            heartbeat: null,
            interruptedExecution: new InterruptedExecutionState(
                $"interruption-{workflowExecutionId}",
                workflowExecutionId,
                leaseId: null,
                lastCheckpointId: $"checkpoint-{workflowExecutionId}",
                RuntimeInterruptionReason.HostStopped,
                RuntimeInterruptionStatus.Detected,
                interruptedAt));

    private static ExecutionLivenessState Live(string workflowExecutionId, string ownerId) =>
        State(
            workflowExecutionId,
            new RuntimeExecutionLease(
                $"lease-{workflowExecutionId}",
                workflowExecutionId,
                ownerId,
                Now.AddMinutes(-1),
                Now.AddMinutes(5),
                1),
            new RuntimeHeartbeat(
                $"heartbeat-{workflowExecutionId}",
                workflowExecutionId,
                ownerId,
                $"lease-{workflowExecutionId}",
                Now),
            interruptedExecution: null);

    private static ExecutionLivenessState State(
        string workflowExecutionId,
        RuntimeExecutionLease? lease,
        RuntimeHeartbeat? heartbeat,
        InterruptedExecutionState? interruptedExecution) =>
        new(
            $"op-{workflowExecutionId}",
            workflowExecutionId,
            lease,
            heartbeat,
            drain: null,
            interruptedExecution);

    private sealed class RecoveryManifestSource : IGroundworkStorageManifestSource
    {
        public string FeatureIdentity => "runtime-recovery-conformance";

        public async ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
            CancellationToken cancellationToken = default)
        {
            var runtime = await new RuntimeGroundworkStorageManifestSource()
                .CreateDeclarationAsync(cancellationToken);
            var recoveryManifest = runtime.Manifest with
            {
                StorageUnits = runtime.Manifest.StorageUnits
                    .Where(unit => StringComparer.Ordinal.Equals(
                        unit.Identity.Value,
                        ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind))
                    .ToArray()
            };

            return new GroundworkStorageManifestDeclaration(
                FeatureIdentity,
                recoveryManifest,
                [],
                [],
                [],
                []);
        }
    }
}
