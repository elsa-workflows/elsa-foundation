using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// T089's public-contract matrix. Failure-window, cancellation, disposal, and restart scenarios remain in T090;
/// this suite exercises the ordinary distributed placement and command-transport contracts on every provider.
/// </summary>
public sealed class DistributedProviderContractTests
{
    private const string ProviderMatrixOptIn = "ELSA_RUN_GROUNDWORK_DISTRIBUTED_PROVIDER_MATRIX";
    private const string Scope = "distributed-provider-contract";
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    private static readonly GroundworkStoreScenarioEvidence RecoveryEvidence =
        GroundworkStoreScenarioEvidence.Cancellation |
        GroundworkStoreScenarioEvidence.FailureInjection |
        GroundworkStoreScenarioEvidence.ProcessRestart;

    [Fact]
    public void Distributed_catalog_partition_is_closed_between_t089_and_t090()
    {
        var distributed = DistributedDefinitions();
        var t089 = T089Definitions();
        var t090 = T090Definitions();
        var probes = T089Probes();

        Assert.Empty(
            t089.Select(definition => definition.ScenarioId)
                .Intersect(t090.Select(definition => definition.ScenarioId), StringComparer.Ordinal));
        Assert.Equal(
            distributed.Select(definition => definition.ScenarioId).Order(StringComparer.Ordinal),
            t089.Concat(t090).Select(definition => definition.ScenarioId).Order(StringComparer.Ordinal));
        Assert.Equal(
            t089.Select(definition => definition.ScenarioId).Order(StringComparer.Ordinal),
            probes.SelectMany(probe => probe.ScenarioIds).Order(StringComparer.Ordinal));
        Assert.All(
            t090,
            definition => Assert.NotEqual(
                GroundworkStoreScenarioEvidence.None,
                definition.RequiredEvidence & RecoveryEvidence));
    }

    [SkippableFact]
    [Trait("Category", "GroundworkProviderMatrix")]
    public async Task Distributed_t089_public_contracts_pass_on_every_provider()
    {
        RequireProviderMatrixOptIn();

        foreach (var providerKey in GroundworkStoreScenarioCatalog.MandatoryProviderKeys)
        {
            foreach (var probe in T089Probes())
                await probe.ExecuteAsync(providerKey);
        }
    }

    private static async Task AssertPlacementClaimRenewAndTakeoverAsync(string providerKey) =>
        _ = await DistributedProviderEvidenceScenarios.RunPlacementClaimRenewAndTakeoverAsync(providerKey);

    private static async Task AssertPlacementStaleReleaseAndExecutionFenceAsync(string providerKey)
    {
        await DistributedProviderEvidenceScenarios.RunPlacementStaleReleaseAsync(providerKey);

        await new RuntimeFenceContractTests()
            .Runtime_fence_contract_is_enforced_by_every_persistent_provider(providerKey);
    }

    private static async Task AssertCommandSendSequenceAsync(string providerKey) =>
        _ = await DistributedProviderEvidenceScenarios.RunCommandSendSequenceAsync(providerKey);

    private static async Task AssertCommandLeaseRetryAndStaleAckAsync(string providerKey)
    {
        await using var driver = await CreateDistributedDriverAsync(providerKey);
        await using var firstClient = await driver.OpenPhysicalClientAsync(Access(Scope));
        await using var secondClient = await driver.OpenPhysicalClientAsync(Access(Scope));
        var first = Transport(firstClient);
        var second = Transport(secondClient);

        await first.SendAsync("wf-lease", Envelope("wf-lease", "env-lease"), Now);
        var leasedByA = await first.LeaseAsync("wf-lease", NodeA, Now, LeaseDuration, maxItems: 10);
        Assert.Single(leasedByA);
        Assert.Empty(await second.LeaseAsync("wf-lease", NodeB, Now.AddSeconds(1), LeaseDuration, maxItems: 10));

        var afterExpiry = Now + LeaseDuration + TimeSpan.FromSeconds(1);
        var leasedByB = await second.LeaseAsync("wf-lease", NodeB, afterExpiry, LeaseDuration, maxItems: 10);

        Assert.Single(leasedByB);
        Assert.Equal(2, leasedByB[0].DeliveryAttemptCount);
        Assert.False(await first.AckAsync("wf-lease", leasedByA[0].TransportItemId, NodeA, leasedByA[0].LeaseToken!.Value, afterExpiry));
        Assert.True(await second.AckAsync("wf-lease", leasedByB[0].TransportItemId, NodeB, leasedByB[0].LeaseToken!.Value, afterExpiry));
        Assert.Equal(0, await second.CountPendingAsync("wf-lease"));
    }

    private static async ValueTask<GroundworkProviderDriver> CreateDistributedDriverAsync(string providerKey)
    {
        var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions);
        await driver.ResetPhysicalAsync([new DistributedGroundworkStorageManifestSource()]);
        return driver;
    }

    private static GroundworkExecutionPlacementStore Placement(GroundworkProviderClient client) =>
        new(client.DocumentStore, client.BoundedDocumentStore);

    private static GroundworkExecutionCommandTransport Transport(GroundworkProviderClient client) =>
        new(client.DocumentStore, GroundworkTestAccess.AccessContext(Scope), client.BoundedDocumentStore);

    private static DocumentStoreAccess Access(string scope) =>
        DocumentStoreAccess.Scoped(new StorageScope(scope));

    private static ExecutionPlacementClaim Claim(string ownerId, DateTimeOffset requestedAt) =>
        new("wf-placement", ownerId, requestedAt, requestedAt + LeaseDuration);

    private static WorkflowExecutionCommandEnvelope Envelope(string workflowExecutionId, string envelopeId) =>
        new(
            envelopeId,
            workflowExecutionId,
            new WorkflowExecutionCommand(
                $"command-{envelopeId}",
                workflowExecutionId,
                WorkflowExecutionCommandKind.RunSchedulerWork,
                Now,
                null,
                new Dictionary<string, string>()),
            $"idempotency-{envelopeId}",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            Now,
            partition: new WorkflowExecutionPartition(Scope));

    private static IReadOnlyList<GroundworkStoreScenarioDefinition> DistributedDefinitions() =>
        GroundworkStoreScenarioCatalog.ForFamily(GroundworkStoreScenarioFamily.DistributedRuntime);

    private static IReadOnlyList<GroundworkStoreScenarioDefinition> T089Definitions() =>
        DistributedDefinitions()
            .Where(definition => (definition.RequiredEvidence & RecoveryEvidence) == GroundworkStoreScenarioEvidence.None)
            .ToArray();

    private static IReadOnlyList<GroundworkStoreScenarioDefinition> T090Definitions() =>
        DistributedDefinitions()
            .Where(definition => (definition.RequiredEvidence & RecoveryEvidence) != GroundworkStoreScenarioEvidence.None)
            .ToArray();

    private static IReadOnlyList<DistributedScenarioProbe> T089Probes() =>
    [
        Probe(
            ["distributed-placement-claim-renew-and-takeover"],
            AssertPlacementClaimRenewAndTakeoverAsync),
        Probe(
            ["distributed-placement-stale-release-and-execution-fence"],
            AssertPlacementStaleReleaseAndExecutionFenceAsync),
        Probe(
            ["distributed-command-send-sequence"],
            AssertCommandSendSequenceAsync),
        Probe(
            ["distributed-command-lease-retry-and-stale-acknowledgement"],
            AssertCommandLeaseRetryAndStaleAckAsync),
        Probe(
            ["distributed-bounded-route-equivalence", "distributed-cross-partition-isolation"],
            providerKey => new FoundationBoundedQueryContractTests()
                .Distributed_placement_and_command_routes_filter_order_distinct_count_and_limit_before_materialization(providerKey))
    ];

    private static DistributedScenarioProbe Probe(
        IReadOnlyList<string> scenarioIds,
        Func<string, Task> executeAsync) =>
        new(scenarioIds, executeAsync);

    private static void RequireProviderMatrixOptIn() =>
        Skip.If(
            !string.Equals(Environment.GetEnvironmentVariable(ProviderMatrixOptIn), "1", StringComparison.Ordinal),
            $"Set {ProviderMatrixOptIn}=1 to run the SQLite, SQL Server, PostgreSQL, and MongoDB Distributed contract matrix.");

    private sealed record DistributedScenarioProbe(
        IReadOnlyList<string> ScenarioIds,
        Func<string, Task> ExecuteAsync);
}
