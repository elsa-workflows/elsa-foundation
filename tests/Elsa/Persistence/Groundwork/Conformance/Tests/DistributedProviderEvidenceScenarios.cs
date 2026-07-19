using System.Globalization;
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
/// Executable provider-matrix observations that can be handed to the Spec 094 evidence publisher.
/// This is deliberately limited to placement and command ordinary public contracts; it does not
/// claim the rows' recovery, bounded-query, or other catalog obligations.
/// </summary>
internal static class DistributedProviderEvidenceScenarios
{
    private const string Scope = "distributed-provider-contract";
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    public static async Task<GroundworkScenarioResult> RunPlacementClaimRenewAndTakeoverAsync(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions);
        await driver.ResetPhysicalAsync([new DistributedGroundworkStorageManifestSource()]);

        await using var firstClient = await driver.OpenPhysicalClientAsync(Access(Scope));
        await using var secondClient = await driver.OpenPhysicalClientAsync(Access(Scope));
        var first = new GroundworkExecutionPlacementStore(firstClient.DocumentStore, firstClient.BoundedDocumentStore);
        var second = new GroundworkExecutionPlacementStore(secondClient.DocumentStore, secondClient.BoundedDocumentStore);

        var firstClaim = await first.TryClaimAsync(Claim(NodeA, Now), Now);
        var renewal = await first.TryClaimAsync(Claim(NodeA, Now.AddSeconds(5)), Now.AddSeconds(5));
        var denied = await second.TryClaimAsync(Claim(NodeB, Now.AddSeconds(6)), Now.AddSeconds(6));
        var afterExpiry = renewal.Lease.ExpiresAt.AddSeconds(1);
        var takeover = await second.TryClaimAsync(Claim(NodeB, afterExpiry), afterExpiry);
        var persisted = await second.FindAsync("wf-placement");

        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, firstClaim.Outcome);
        Assert.Equal(ExecutionPlacementClaimOutcome.Renewed, renewal.Outcome);
        Assert.Equal(ExecutionPlacementClaimOutcome.Denied, denied.Outcome);
        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, takeover.Outcome);
        var winnerCount = persisted is null ? "0" : "1";
        Assert.NotNull(persisted);
        var currentOwner = persisted!.OwnerId;
        Assert.Equal(NodeA, denied.Lease.OwnerId);
        Assert.Equal(NodeB, takeover.Lease.OwnerId);
        Assert.Equal(NodeB, currentOwner);
        Assert.Equal(takeover.Lease.PlacementToken, persisted.PlacementToken);
        Assert.True(firstClaim.Lease.PlacementToken < renewal.Lease.PlacementToken);
        Assert.True(renewal.Lease.PlacementToken < takeover.Lease.PlacementToken);

        var definition = GroundworkStoreScenarioCatalog.Get("distributed-placement-claim-renew-and-takeover");
        var composition = GroundworkCompositionFingerprint.Create(string.Join(
            '\n',
            $"provider={driver.Descriptor.ProviderIdentity}",
            $"provider-version={driver.Descriptor.ProviderVersion}",
            $"topology={driver.Descriptor.Topology.Description}",
            $"manifest={typeof(DistributedGroundworkStorageManifestSource).AssemblyQualifiedName}"));
        return definition.CreateResult(
            "distributed-execution-placement",
            driver.Descriptor,
            composition,
            clients: 2,
            observations:
            [
                new GroundworkScenarioObservation("current-owner", currentOwner),
                new GroundworkScenarioObservation(
                    "token-order",
                    string.Join(
                        ',',
                        firstClaim.Lease.PlacementToken.ToString(CultureInfo.InvariantCulture),
                        renewal.Lease.PlacementToken.ToString(CultureInfo.InvariantCulture),
                        takeover.Lease.PlacementToken.ToString(CultureInfo.InvariantCulture))),
                new GroundworkScenarioObservation("winner-count", winnerCount)
            ]);
    }

    public static async Task RunPlacementStaleReleaseAsync(string providerKey)
    {
        await using var driver = await CreateDistributedDriverAsync(providerKey);
        await using var firstClient = await driver.OpenPhysicalClientAsync(Access(Scope));
        await using var secondClient = await driver.OpenPhysicalClientAsync(Access(Scope));
        var first = Placement(firstClient);
        var second = Placement(secondClient);

        var stale = await first.TryClaimAsync(Claim(NodeA, Now), Now);
        var current = await second.TryClaimAsync(Claim(NodeB, stale.Lease.ExpiresAt.AddSeconds(1)), stale.Lease.ExpiresAt.AddSeconds(1));
        await first.ReleaseAsync(stale.Lease);
        var persisted = await second.FindAsync("wf-placement");

        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, stale.Outcome);
        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, current.Outcome);
        Assert.NotNull(persisted);
        Assert.Equal(NodeB, persisted!.OwnerId);
        Assert.Equal(current.Lease.PlacementToken, persisted.PlacementToken);

    }

    public static async Task<GroundworkScenarioResult> RunCommandSendSequenceAsync(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions);
        await driver.ResetPhysicalAsync([new DistributedGroundworkStorageManifestSource()]);

        await using var firstClient = await driver.OpenPhysicalClientAsync(Access(Scope));
        await using var secondClient = await driver.OpenPhysicalClientAsync(Access(Scope));
        var first = new GroundworkExecutionCommandTransport(
            firstClient.DocumentStore,
            GroundworkTestAccess.AccessContext(Scope),
            firstClient.BoundedDocumentStore);
        var second = new GroundworkExecutionCommandTransport(
            secondClient.DocumentStore,
            GroundworkTestAccess.AccessContext(Scope),
            secondClient.BoundedDocumentStore);

        var sends = await Task.WhenAll(
            first.SendAsync("wf-command", Envelope("wf-command", "env-1"), Now).AsTask(),
            second.SendAsync("wf-command", Envelope("wf-command", "env-2"), Now).AsTask());
        var firstSend = sends[0];
        var secondSend = sends[1];
        var pending = await first.CountPendingAsync("wf-command");
        var leased = await first.LeaseAsync("wf-command", NodeA, Now, LeaseDuration, maxItems: 10);
        var orderedSequences = leased.Select(item => item.Sequence).ToArray();
        var duplicateIdentityCount = leased.Count - leased
            .Select(item => item.Envelope.EnvelopeId)
            .Distinct(StringComparer.Ordinal)
            .Count();

        Assert.Equal(new long[] { 1, 2 }, sends.Select(item => item.Sequence).Order());
        Assert.NotEqual(firstSend.TransportItemId, secondSend.TransportItemId);
        Assert.Equal(2, pending);
        Assert.Equal(new long[] { 1, 2 }, orderedSequences);
        Assert.Equal(["env-1", "env-2"], leased.Select(item => item.Envelope.EnvelopeId).Order(StringComparer.Ordinal));
        Assert.Equal(0, duplicateIdentityCount);

        var definition = GroundworkStoreScenarioCatalog.Get("distributed-command-send-sequence");
        var composition = GroundworkCompositionFingerprint.Create(string.Join(
            '\n',
            $"provider={driver.Descriptor.ProviderIdentity}",
            $"provider-version={driver.Descriptor.ProviderVersion}",
            $"topology={driver.Descriptor.Topology.Description}",
            $"manifest={typeof(DistributedGroundworkStorageManifestSource).AssemblyQualifiedName}"));
        return definition.CreateResult(
            "distributed-command-transport",
            driver.Descriptor,
            composition,
            clients: 2,
            observations:
            [
                new GroundworkScenarioObservation("duplicate-identity-count", duplicateIdentityCount.ToString(CultureInfo.InvariantCulture)),
                new GroundworkScenarioObservation("ordered-sequences", string.Join(',', orderedSequences)),
                new GroundworkScenarioObservation("persisted-command-count", pending.ToString(CultureInfo.InvariantCulture))
            ]);
    }

    public static async Task<GroundworkScenarioResult> RunCommandLeaseRetryAndStaleAcknowledgementAsync(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions);
        await driver.ResetPhysicalAsync([new DistributedGroundworkStorageManifestSource()]);

        await using var firstClient = await driver.OpenPhysicalClientAsync(Access(Scope));
        await using var secondClient = await driver.OpenPhysicalClientAsync(Access(Scope));
        var first = new GroundworkExecutionCommandTransport(firstClient.DocumentStore, GroundworkTestAccess.AccessContext(Scope), firstClient.BoundedDocumentStore);
        var second = new GroundworkExecutionCommandTransport(secondClient.DocumentStore, GroundworkTestAccess.AccessContext(Scope), secondClient.BoundedDocumentStore);
        await first.SendAsync("wf-lease", Envelope("wf-lease", "env-lease"), Now);
        var leasedByA = await first.LeaseAsync("wf-lease", NodeA, Now, LeaseDuration, maxItems: 10);
        Assert.Single(leasedByA);
        Assert.Empty(await second.LeaseAsync("wf-lease", NodeB, Now.AddSeconds(1), LeaseDuration, maxItems: 10));
        var afterExpiry = Now + LeaseDuration + TimeSpan.FromSeconds(1);
        var leasedByB = await second.LeaseAsync("wf-lease", NodeB, afterExpiry, LeaseDuration, maxItems: 10);
        Assert.Single(leasedByB);
        var staleAcknowledged = await first.AckAsync(
            "wf-lease",
            leasedByA[0].TransportItemId,
            NodeA,
            leasedByA[0].LeaseToken!.Value,
            afterExpiry);
        var currentAcknowledged = await second.AckAsync(
            "wf-lease",
            leasedByB[0].TransportItemId,
            NodeB,
            leasedByB[0].LeaseToken!.Value,
            afterExpiry);
        Assert.False(staleAcknowledged);
        Assert.True(currentAcknowledged);
        Assert.Equal(2, leasedByB[0].DeliveryAttemptCount);
        Assert.Equal(0, await second.CountPendingAsync("wf-lease"));

        return Definition("distributed-command-lease-retry-and-stale-acknowledgement").CreateResult(
            "distributed-command-transport",
            driver.Descriptor,
            Composition(driver),
            2,
            [
                new GroundworkScenarioObservation("current-owner", currentAcknowledged && !staleAcknowledged ? NodeB : NodeA),
                new GroundworkScenarioObservation("redelivery-count", (leasedByB[0].DeliveryAttemptCount - 1).ToString(CultureInfo.InvariantCulture)),
                new GroundworkScenarioObservation("stale-acknowledgement-count", staleAcknowledged ? "1" : "0")
            ]);
    }

    public static async Task<GroundworkScenarioResult> RunPlacementDisposeAndReopenAsync(string providerKey)
    {
        await using var driver = await CreateDistributedDriverAsync(providerKey);
        ExecutionPlacementLease claimed;
        await using (var client = await driver.OpenPhysicalClientAsync(Access(Scope)))
        {
            var result = await Placement(client).TryClaimAsync(Claim(NodeA, Now), Now);
            Assert.Equal(ExecutionPlacementClaimOutcome.Granted, result.Outcome);
            claimed = result.Lease;
        }

        await using var reopenedClient = await driver.OpenPhysicalClientAsync(Access(Scope));
        var reopened = await Placement(reopenedClient).FindAsync("wf-placement");
        Assert.NotNull(reopened);
        Assert.Equal(claimed, reopened);

        return ReopenResult(
            driver,
            "distributed-placement-dispose-and-reopen",
            "distributed-execution-placement",
            [
                new GroundworkScenarioObservation("reopen-state", "persisted"),
                new GroundworkScenarioObservation("placement-token", reopened!.PlacementToken.ToString(CultureInfo.InvariantCulture))
            ]);
    }

    public static async Task<GroundworkScenarioResult> RunCommandDisposeAndReopenAsync(string providerKey)
    {
        await using var driver = await CreateDistributedDriverAsync(providerKey);
        await using (var client = await driver.OpenPhysicalClientAsync(Access(Scope)))
        {
            await Transport(client).SendAsync("wf-reopen", Envelope("wf-reopen", "env-reopen"), Now);
        }

        await using var reopenedClient = await driver.OpenPhysicalClientAsync(Access(Scope));
        var pending = await Transport(reopenedClient).CountPendingAsync("wf-reopen");
        Assert.Equal(1, pending);

        return ReopenResult(
            driver,
            "distributed-command-dispose-and-reopen",
            "distributed-command-transport",
            [
                new GroundworkScenarioObservation("reopen-state", "persisted"),
                new GroundworkScenarioObservation("persisted-command-count", pending.ToString(CultureInfo.InvariantCulture))
            ]);
    }

    public static async Task<IReadOnlyList<DistributedNativeRouteEvidence>> CaptureBoundedRouteEvidenceAsync(string providerKey)
    {
        await new FoundationBoundedQueryContractTests()
            .Distributed_placement_and_command_routes_filter_order_distinct_count_and_limit_before_materialization(providerKey);

        var descriptor = await DescribeAsync(providerKey);
        var plans = await ProviderNativePlanTests.CaptureNativeRoutePlansAsync(
            GroundworkProviderDriverFactory.Create(providerKey));
        var scenarioId = "distributed-bounded-route-equivalence";
        var composition = GroundworkCompositionFingerprint.Create(string.Join(
            '\n',
            "distributed-provider-evidence-v1",
            $"provider={descriptor.ProviderIdentity}",
            $"provider-version={descriptor.ProviderVersion}",
            $"scenario={scenarioId}"));
        var path = GroundworkExecutionPath.Create(providerKey, scenarioId, composition);
        var routes = new[]
        {
            new DistributedNativeRoute("distributed-execution-placement", "list-owned-or-current-bounded", DistributedGroundworkStorageManifest.ListOwnedPlacementsQuery),
            new DistributedNativeRoute("distributed-command-transport", "lease-visible-by-execution-bounded", DistributedGroundworkStorageManifest.LeaseVisibleCommandsQuery),
            new DistributedNativeRoute("distributed-command-transport", "list-pending-execution-ids-bounded", DistributedGroundworkStorageManifest.ListPendingExecutionIdsQuery),
            new DistributedNativeRoute("distributed-command-transport", "count-pending-by-execution", DistributedGroundworkStorageManifest.CountPendingCommandsQuery)
        };

        return routes.Select(route =>
        {
            var plan = plans.Single(candidate => string.Equals(
                candidate.Request.QueryIdentity,
                route.NativeQueryIdentity,
                StringComparison.Ordinal));
            var result = GroundworkScenarioResult.Create(
                scenarioId,
                route.CoverageEntryId,
                descriptor,
                composition,
                path.Value,
                clients: 1,
                [
                    new GroundworkScenarioObservation("finite-limit", plan.FiniteLimit?.ToString(CultureInfo.InvariantCulture) ?? "count"),
                    new GroundworkScenarioObservation("materialized-candidate-count", plan.MaterializedCandidateCount.ToString(CultureInfo.InvariantCulture)),
                    new GroundworkScenarioObservation("query-identity", plan.Request.QueryIdentity)
                ],
                GroundworkScenarioOutcome.Pass,
                nativeEvidence: GroundworkNativePlanEvidence.Create(path, scenarioId, plan.Evidence));
            return new DistributedNativeRouteEvidence(route, result, plan);
        }).ToArray();
    }

    private static GroundworkStoreScenarioDefinition Definition(string scenarioId) =>
        GroundworkStoreScenarioCatalog.Get(scenarioId);

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

    private static async Task<GroundworkProviderDescriptor> DescribeAsync(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        return driver.Descriptor;
    }

    private static GroundworkScenarioResult ReopenResult(
        GroundworkProviderDriver driver,
        string scenarioId,
        string coverageEntryId,
        IReadOnlyList<GroundworkScenarioObservation> observations)
    {
        var composition = Composition(driver);
        return GroundworkScenarioResult.Create(
            scenarioId,
            coverageEntryId,
            driver.Descriptor,
            composition,
            GroundworkExecutionPath.Create(driver.Descriptor.ProviderKey, scenarioId, composition).Value,
            clients: 2,
            observations,
            GroundworkScenarioOutcome.Pass);
    }

    private static GroundworkCompositionFingerprint Composition(GroundworkProviderDriver driver) =>
        GroundworkCompositionFingerprint.Create(string.Join('\n',
            $"provider={driver.Descriptor.ProviderIdentity}",
            $"provider-version={driver.Descriptor.ProviderVersion}",
            $"topology={driver.Descriptor.Topology.Description}",
            $"manifest={typeof(DistributedGroundworkStorageManifestSource).AssemblyQualifiedName}"));

    private static DocumentStoreAccess Access(string scope) =>
        DocumentStoreAccess.Scoped(new StorageScope(scope));

    private static GroundworkExecutionPlacementStore Placement(GroundworkProviderClient client) =>
        new(client.DocumentStore, client.BoundedDocumentStore);

    private static GroundworkExecutionCommandTransport Transport(GroundworkProviderClient client) =>
        new(client.DocumentStore, GroundworkTestAccess.AccessContext(Scope), client.BoundedDocumentStore);

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
}

internal sealed record DistributedNativeRoute(
    string CoverageEntryId,
    string LedgerQueryShape,
    string NativeQueryIdentity);

internal sealed record DistributedNativeRouteEvidence(
    DistributedNativeRoute Route,
    GroundworkScenarioResult Result,
    GroundworkNativeRoutePlanResult Plan);
