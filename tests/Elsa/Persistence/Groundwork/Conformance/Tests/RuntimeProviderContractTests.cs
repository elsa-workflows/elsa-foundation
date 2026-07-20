using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// T087's public-contract matrix. Recovery-bearing Runtime scenarios deliberately remain in T090:
/// this suite covers the ordinary-document, checkpoint, and operational Runtime definitions only.
/// </summary>
public sealed class RuntimeProviderContractTests
{
    private const string ProviderMatrixOptIn = "ELSA_RUN_GROUNDWORK_RUNTIME_PROVIDER_MATRIX";

    private static readonly GroundworkStoreScenarioEvidence RecoveryEvidence =
        GroundworkStoreScenarioEvidence.Cancellation |
        GroundworkStoreScenarioEvidence.FailureInjection |
        GroundworkStoreScenarioEvidence.ProcessRestart;

    [Fact]
    public void Runtime_catalog_partition_is_closed_between_t087_and_t090()
    {
        var runtime = RuntimeDefinitions();
        var t087 = T087Definitions();
        var t090 = T090Definitions();
        var probes = T087Probes();

        Assert.Empty(
            t087.Select(definition => definition.ScenarioId)
                .Intersect(t090.Select(definition => definition.ScenarioId), StringComparer.Ordinal));
        Assert.Equal(
            runtime.Select(definition => definition.ScenarioId).Order(StringComparer.Ordinal),
            t087.Concat(t090).Select(definition => definition.ScenarioId).Order(StringComparer.Ordinal));
        Assert.Equal(
            t087.Select(definition => definition.ScenarioId).Order(StringComparer.Ordinal),
            probes.SelectMany(probe => probe.ScenarioIds).Order(StringComparer.Ordinal));
        Assert.All(
            t090,
            definition => Assert.NotEqual(
                GroundworkStoreScenarioEvidence.None,
                definition.RequiredEvidence & RecoveryEvidence));
    }

    [SkippableFact]
    [Trait("Category", "GroundworkProviderMatrix")]
    public async Task Runtime_t087_public_contracts_pass_on_every_provider()
    {
        RequireProviderMatrixOptIn();

        foreach (var providerKey in GroundworkStoreScenarioCatalog.MandatoryProviderKeys)
        {
            foreach (var probe in T087Probes())
                await probe.ExecuteAsync(providerKey);
        }
    }

    internal static async Task RunSchedulerStateRoundTripEvidenceAsync(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        await driver.ResetPhysicalAsync();
        const string workflowExecutionId = "runtime-provider-contract-scheduler";
        var state = new SchedulerState(workflowExecutionId, version: 7, pendingWork: []);

        await using (var client = await driver.OpenPhysicalClientAsync())
        {
            var store = new GroundworkSchedulerStateStore(
                client.DocumentStore,
                GroundworkProviderTestSerialization.Serializer,
                client.BoundedDocumentStore);
            await store.SaveAsync(state);
        }

        await using var reopenedClient = await driver.OpenPhysicalClientAsync();
        var reopenedStore = new GroundworkSchedulerStateStore(
            reopenedClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            reopenedClient.BoundedDocumentStore);
        var reopened = await reopenedStore.FindAsync(workflowExecutionId);
        Assert.NotNull(reopened);
        Assert.Equal(state.Version, reopened!.Version);
        Assert.Equal(state.WorkflowExecutionId, reopened.WorkflowExecutionId);
    }

    private static IReadOnlyList<GroundworkStoreScenarioDefinition> RuntimeDefinitions() =>
        GroundworkStoreScenarioCatalog.ForFamily(GroundworkStoreScenarioFamily.Runtime);

    private static IReadOnlyList<GroundworkStoreScenarioDefinition> T087Definitions() =>
        RuntimeDefinitions()
            .Where(definition => (definition.RequiredEvidence & RecoveryEvidence) == GroundworkStoreScenarioEvidence.None)
            .ToArray();

    private static IReadOnlyList<GroundworkStoreScenarioDefinition> T090Definitions() =>
        RuntimeDefinitions()
            .Where(definition => (definition.RequiredEvidence & RecoveryEvidence) != GroundworkStoreScenarioEvidence.None)
            .ToArray();

    private static IReadOnlyList<RuntimeScenarioProbe> T087Probes() =>
    [
        Probe(
            ["runtime-ordinary-state-scope-isolation"],
            providerKey => new StorageScopeContractTests()
                .Storage_scope_contract_is_enforced_by_every_persistent_provider(providerKey)),
        Probe(
            ["runtime-bounded-route-equivalence"],
            providerKey => new RuntimeBoundedQueryContractTests()
                .Every_B7_physical_route_admits_an_exact_empty_window_without_client_materialization(providerKey)),
        Probe(
            ["runtime-trigger-stimulus-lookup"],
            providerKey => new RuntimeBoundedQueryContractTests()
                .Active_trigger_binding_pages_are_equivalent_and_materialized_inside_the_requested_window(providerKey)),
        Probe(
            [
                "runtime-execution-ownership-fencing",
                "runtime-checkpoint-idempotency",
                "runtime-execution-liveness-takeover"
            ],
            providerKey => new RuntimeFenceContractTests()
                .Runtime_fence_contract_is_enforced_by_every_persistent_provider(providerKey)),
        Probe(
            ["runtime-timer-create-once-and-due-selection"],
            providerKey => new RuntimeTransitionContractTests()
                .DurableTimer_CreateClaimExpiryAndStaleCompletionAreConditionalOnEveryProvider(providerKey)),
        Probe(
            ["runtime-recurring-schedule-conditional-advance"],
            providerKey => new RuntimeTransitionContractTests()
                .RecurringSchedule_AdvanceHasOneCASWinnerOnEveryProvider(providerKey)),
        Probe(
            ["runtime-incident-create-once", "runtime-hold-conditional-transition"],
            providerKey => new RuntimeTransitionContractTests()
                .IncidentAndWorkflowHold_RacesSelectOneDurableWinnerOnEveryProvider(providerKey)),
        Probe(
            ["runtime-publication-projection-create-once"],
            providerKey => new RuntimeTransitionContractTests()
                .PublicationProjection_PrepareAndActivationAreConditionalAfterReopenOnEveryProvider(providerKey)),
        Probe(
            ["runtime-outbox-claim-retry-and-stale-acknowledgement"],
            providerKey => new RuntimeDeliveryContractTests()
                .Outbox_claim_expiry_retry_and_stale_ack_are_fenced_on_every_provider(providerKey)),
        Probe(
            ["runtime-scheduler-queue-claim-and-stale-acknowledgement"],
            providerKey => new RuntimeDeliveryContractTests()
                .Scheduler_claim_expiry_renewal_release_and_stale_ack_are_fenced_on_every_provider(providerKey)),
        Probe(
            ["runtime-scheduler-state-roundtrip"],
            RunSchedulerStateRoundTripEvidenceAsync)
    ];

    private static RuntimeScenarioProbe Probe(
        IReadOnlyList<string> scenarioIds,
        Func<string, Task> executeAsync) =>
        new(scenarioIds, executeAsync);

    private static void RequireProviderMatrixOptIn() =>
        Skip.If(
            !string.Equals(Environment.GetEnvironmentVariable(ProviderMatrixOptIn), "1", StringComparison.Ordinal),
            $"Set {ProviderMatrixOptIn}=1 to run the SQLite, SQL Server, PostgreSQL, and MongoDB Runtime contract matrix.");

    private sealed record RuntimeScenarioProbe(
        IReadOnlyList<string> ScenarioIds,
        Func<string, Task> ExecuteAsync);
}
