using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.Stores;
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
/// T090's recovery-only provider matrix. The ordinary public contracts belong to T087, T088, and T089; this
/// suite owns the exact catalog definitions that require cancellation, injected failure, or process restart.
/// </summary>
public sealed class ProviderRecoveryContractTests
{
    private const string ProviderMatrixOptIn = "ELSA_RUN_GROUNDWORK_PROVIDER_RECOVERY_MATRIX";
    private const string Tenant = "provider-recovery-contract";
    private const string DistributedScope = "provider-recovery-distributed";
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly GroundworkStoreScenarioEvidence RecoveryEvidence =
        GroundworkStoreScenarioEvidence.Cancellation |
        GroundworkStoreScenarioEvidence.FailureInjection |
        GroundworkStoreScenarioEvidence.ProcessRestart;

    [Fact]
    public void Recovery_catalog_mapping_is_closed_across_every_public_store_family()
    {
        var all = AllDefinitions();
        var recovery = RecoveryDefinitions();
        var ordinary = all
            .Where(definition => (definition.RequiredEvidence & RecoveryEvidence) == GroundworkStoreScenarioEvidence.None)
            .ToArray();
        var probes = RecoveryProbes();
        var mappedScenarioIds = probes.SelectMany(probe => probe.ScenarioIds).ToArray();

        Assert.Empty(
            ordinary.Select(definition => definition.ScenarioId)
                .Intersect(recovery.Select(definition => definition.ScenarioId), StringComparer.Ordinal));
        Assert.Equal(
            all.Select(definition => definition.ScenarioId).Order(StringComparer.Ordinal),
            ordinary.Concat(recovery).Select(definition => definition.ScenarioId).Order(StringComparer.Ordinal));
        Assert.Equal(mappedScenarioIds.Length, mappedScenarioIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            recovery.Select(definition => definition.ScenarioId).Order(StringComparer.Ordinal),
            mappedScenarioIds.Order(StringComparer.Ordinal));
        Assert.All(recovery, definition => Assert.NotEqual(
            GroundworkStoreScenarioEvidence.None,
            definition.RequiredEvidence & RecoveryEvidence));

        foreach (var definition in recovery.Where(definition =>
                     definition.RequiredEvidence.HasFlag(GroundworkStoreScenarioEvidence.FailureInjection)))
        {
            var probe = Assert.Single(probes, candidate => candidate.ScenarioIds.Contains(
                definition.ScenarioId,
                StringComparer.Ordinal));
            Assert.Equal(definition.FailureWindows, probe.DeclaredFailureWindows);
        }
    }

    [SkippableFact]
    [Trait("Category", "GroundworkProviderMatrix")]
    public async Task Recovery_contracts_pass_on_every_provider()
    {
        RequireProviderMatrixOptIn();

        foreach (var providerKey in GroundworkStoreScenarioCatalog.MandatoryProviderKeys)
        {
            await AssertDriverRestartAndTopologyAsync(providerKey);
            await AssertDeclaredDuringRecoveryBoundaryAsync();
            foreach (var probe in RecoveryProbes())
                await probe.ExecuteAsync(providerKey);
        }
    }

    private static async Task AssertDriverRestartAndTopologyAsync(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.ExternalProcessRestart);

        if (driver is IGroundworkTopologyRejectionProbe rejectionProbe)
        {
            var rejection = await rejectionProbe.CaptureTopologyRejectionAsync();
            Assert.Equal("topology-rejection", rejection.Kind);
            Assert.Contains("outcome=rejected", rejection.Content, StringComparison.Ordinal);
        }

        await driver.ResetAsync();
        var save = await driver.RunInNewProcessAsync(new GroundworkProcessProbeRequest(
            $"recovery-driver-save-{providerKey}",
            GroundworkProcessProbeOperation.Save,
            "recovery-driver-restart",
            "survived"));
        var load = await driver.RunInNewProcessAsync(new GroundworkProcessProbeRequest(
            $"recovery-driver-load-{providerKey}",
            GroundworkProcessProbeOperation.Load,
            "recovery-driver-restart"));

        Assert.NotEqual(Environment.ProcessId, save.ProcessId);
        Assert.NotEqual(Environment.ProcessId, load.ProcessId);
        Assert.NotEqual(save.ProcessId, load.ProcessId);
    }

    // The catalog's fourth marker is a recovery-coordinator cancellation boundary. The current public stores do
    // not expose a provider-decision hook at that point, so exercise the declared controller boundary separately
    // instead of misreporting it as a durable provider-write failure.
    private static async Task AssertDeclaredDuringRecoveryBoundaryAsync()
    {
        var failures = new GroundworkFailureController();
        var boundary = new GroundworkFailureWindow("during-recovery");
        failures.CancelAt(boundary);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            failures.ReachAsync(boundary).AsTask());
        Assert.True(exception.CancellationToken.IsCancellationRequested);
        Assert.Equal([boundary], failures.ReachedWindows);
    }

    internal static async Task RunRuntimeStateRestartEvidenceAsync(string providerKey)
    {
        await using var driver = await CreatePhysicalDriverAsync(providerKey, new RuntimeGroundworkStorageManifestSource());
        const string workflowExecutionId = "recovery-runtime-scheduler";
        var state = new SchedulerState(workflowExecutionId, version: 11, pendingWork: []);

        await using (var client = await driver.OpenPhysicalClientAsync())
        {
            var store = SchedulerState(client);
            await store.SaveAsync(state);
        }

        await using var reopenedClient = await driver.OpenPhysicalClientAsync();
        var reopened = await SchedulerState(reopenedClient).FindAsync(workflowExecutionId);
        Assert.NotNull(reopened);
        Assert.Equal(state.Version, reopened!.Version);
    }

    private static async Task AssertRuntimeFailureRecoveryAsync(string providerKey)
    {
        var windows = GroundworkDocumentStoreFailureWindows.OperationalRuntime;
        await new RuntimeFailureWindowTests().Operational_rows_converge_after_each_named_failure_window(
            providerKey,
            windows.BeforeProviderDecision.Value,
            decisionIsDurable: false);
        await new RuntimeFailureWindowTests().Operational_rows_converge_after_each_named_failure_window(
            providerKey,
            windows.DuringProviderDecision!.Value.Value,
            decisionIsDurable: true);
        await new RuntimeFailureWindowTests().Operational_rows_converge_after_each_named_failure_window(
            providerKey,
            windows.AfterDurableDecision.Value,
            decisionIsDurable: true);
    }

    private static async Task AssertRuntimeCancellationAndReuseAsync(string providerKey)
    {
        await using var driver = await CreatePhysicalDriverAsync(providerKey, new RuntimeGroundworkStorageManifestSource());
        await using var client = await driver.OpenPhysicalClientAsync();
        var store = SchedulerState(client);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.FindAsync("recovery-runtime-cancelled", cancelled.Token).AsTask());

        var state = new SchedulerState("recovery-runtime-cancelled", version: 1, pendingWork: []);
        await store.SaveAsync(state);
        Assert.Equal(state.Version, (await store.FindAsync(state.WorkflowExecutionId))!.Version);
    }

    private static async Task AssertIdentityRecoveryAsync(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        var result = await new AspNetCoreIdentityProviderAcceptanceRunner(driver).RunAsync();

        AspNetCoreIdentityProviderAcceptanceCatalog.RequireExactCoverage(result.CompletedObjectiveIds);
        Assert.True(result.FindProcessId > 0);
        Assert.True(result.DuplicateProcessId > 0);

        await using var client = await driver.OpenPhysicalClientAsync(Access(Tenant));
        var applications = new GroundworkApplicationStore(
            client.DocumentStore,
            new FixedAccessContextAccessor(Tenant));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            applications.FindAsync(Tenant, "recovery-application", cancelled.Token).AsTask());

        var application = new ApplicationRecord(
            "recovery-application",
            Tenant,
            "recovery-client",
            "Recovery application",
            ApplicationType.Confidential,
            ResourceOwnership.Foundation,
            new HashSet<string> { "client_credentials" },
            new HashSet<string>());
        await applications.SaveAsync(application);
        Assert.NotNull(await applications.FindAsync(Tenant, application.Id));
    }

    private static async Task AssertSecretRestartAsync(string providerKey)
    {
        await using var driver = await CreatePhysicalDriverAsync(providerKey, new SecretsGroundworkStorageManifestSource());
        const string name = "recovery-secret";

        await using (var client = await driver.OpenPhysicalClientAsync(Access(Tenant)))
            await SecretRepository(client).SaveAsync(Secret(name, "first"));

        await using var reopenedClient = await driver.OpenPhysicalClientAsync(Access(Tenant));
        var reopened = await SecretRepository(reopenedClient).FindAsync(Tenant, name);
        Assert.NotNull(reopened);
        Assert.Equal("first", reopened!.LatestActiveVersion!.Payload.Value);
    }

    private static async Task AssertSecretCancellationAndReuseAsync(string providerKey)
    {
        await using var driver = await CreatePhysicalDriverAsync(providerKey, new SecretsGroundworkStorageManifestSource());
        await using var client = await driver.OpenPhysicalClientAsync(Access(Tenant));
        var store = SecretRepository(client);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.FindAsync(Tenant, "recovery-secret-cancelled", cancelled.Token).AsTask());

        await store.SaveAsync(Secret("recovery-secret-cancelled", "reused"));
        Assert.Equal("reused", (await store.FindAsync(Tenant, "recovery-secret-cancelled"))!.LatestActiveVersion!.Payload.Value);
    }

    private static async Task AssertDistributedFailureAndRestartAsync(string providerKey)
    {
        foreach (var window in new[]
                 {
                     GroundworkDocumentStoreFailureWindows.OperationalRuntime.BeforeProviderDecision,
                     GroundworkDocumentStoreFailureWindows.OperationalRuntime.DuringProviderDecision!.Value,
                     GroundworkDocumentStoreFailureWindows.OperationalRuntime.AfterDurableDecision
                 })
        {
            await using var driver = await CreatePhysicalDriverAsync(providerKey, new DistributedGroundworkStorageManifestSource());
            var workflowExecutionId = $"recovery-distributed-{window.Value}";
            var failures = new GroundworkFailureController();
            failures.FailAt(window);

            await using (var client = await driver.OpenPhysicalClientAsync(Access(DistributedScope)))
            {
                var decorated = new GroundworkFailureInjectingDocumentStore(
                    client.DocumentStore,
                    client.BoundedDocumentStore!,
                    client.DocumentStore.Access,
                    failures,
                    GroundworkDocumentStoreFailureWindows.OperationalRuntime);
                var transport = new GroundworkExecutionCommandTransport(
                    decorated,
                    new FixedAccessContextAccessor(DistributedScope),
                    decorated);

                var exception = await Assert.ThrowsAsync<InjectedGroundworkFailureException>(() =>
                    transport.SendAsync(workflowExecutionId, Envelope(workflowExecutionId), Now).AsTask());
                Assert.Equal(window, exception.Window);
            }

            await using var reopenedClient = await driver.OpenPhysicalClientAsync(Access(DistributedScope));
            var reopened = Transport(reopenedClient);
            var expectedCount = StringComparer.Ordinal.Equals(
                window.Value,
                GroundworkDocumentStoreFailureWindows.OperationalRuntime.BeforeProviderDecision.Value)
                ? 0
                : 1;
            Assert.Equal(expectedCount, await reopened.CountPendingAsync(workflowExecutionId));
        }
    }

    private static async Task AssertDistributedCancellationAndReuseAsync(string providerKey)
    {
        await using var driver = await CreatePhysicalDriverAsync(providerKey, new DistributedGroundworkStorageManifestSource());
        await using var client = await driver.OpenPhysicalClientAsync(Access(DistributedScope));
        var store = new GroundworkExecutionPlacementStore(client.DocumentStore, client.BoundedDocumentStore);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.FindAsync("recovery-distributed-cancelled", cancelled.Token).AsTask());

        var claim = new ExecutionPlacementClaim(
            "recovery-distributed-cancelled",
            "node-reused",
            Now,
            Now + LeaseDuration);
        var saved = await store.TryClaimAsync(claim, Now);
        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, saved.Outcome);
        Assert.Equal("node-reused", (await store.FindAsync(claim.WorkflowExecutionId))!.OwnerId);
    }

    private static async ValueTask<GroundworkProviderDriver> CreatePhysicalDriverAsync(
        string providerKey,
        IGroundworkStorageManifestSource manifestSource)
    {
        var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([manifestSource]);
        return driver;
    }

    private static GroundworkSchedulerStateStore SchedulerState(GroundworkProviderClient client) =>
        new(client.DocumentStore, GroundworkProviderTestSerialization.Serializer, client.BoundedDocumentStore);

    private static GroundworkSecretRepository SecretRepository(GroundworkProviderClient client) =>
        new(client.DocumentStore, client.BoundedDocumentStore);

    private static GroundworkExecutionCommandTransport Transport(GroundworkProviderClient client) =>
        new(client.DocumentStore, new FixedAccessContextAccessor(DistributedScope), client.BoundedDocumentStore);

    private static WorkflowExecutionCommandEnvelope Envelope(string workflowExecutionId) =>
        new(
            $"envelope-{workflowExecutionId}",
            workflowExecutionId,
            new WorkflowExecutionCommand(
                $"command-{workflowExecutionId}",
                workflowExecutionId,
                WorkflowExecutionCommandKind.RunSchedulerWork,
                Now,
                null,
                new Dictionary<string, string>()),
            $"idempotency-{workflowExecutionId}",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            Now,
            partition: new WorkflowExecutionPartition(DistributedScope));

    private static Secret Secret(string name, string value) => new()
    {
        TenantId = Tenant,
        Name = name,
        DisplayName = name,
        TypeName = SecretTypeNames.Text,
        StoreName = SecretStoreNames.Encrypted,
        Versions = [new SecretVersion { Version = 1, Payload = SecretPayload.FromValue(value) }]
    };

    private static DocumentStoreAccess Access(string scope) =>
        DocumentStoreAccess.Scoped(new StorageScope(scope));

    private static IReadOnlyList<GroundworkStoreScenarioDefinition> AllDefinitions() =>
        GroundworkStoreScenarioCatalog.ForFamily(GroundworkStoreScenarioFamily.Runtime)
            .Concat(GroundworkStoreScenarioCatalog.ForFamily(GroundworkStoreScenarioFamily.Identity))
            .Concat(GroundworkStoreScenarioCatalog.ForFamily(GroundworkStoreScenarioFamily.Secrets))
            .Concat(GroundworkStoreScenarioCatalog.ForFamily(GroundworkStoreScenarioFamily.DistributedRuntime))
            .OrderBy(definition => definition.ScenarioId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<GroundworkStoreScenarioDefinition> RecoveryDefinitions() =>
        AllDefinitions()
            .Where(definition => (definition.RequiredEvidence & RecoveryEvidence) != GroundworkStoreScenarioEvidence.None)
            .ToArray();

    private static IReadOnlyList<RecoveryScenarioProbe> RecoveryProbes() =>
    [
        Probe(["runtime-ordinary-state-revision-and-reopen"], RunRuntimeStateRestartEvidenceAsync),
        Probe(["runtime-checkpoint-failure-recovery"], AssertRuntimeFailureRecoveryAsync, FailureWindows()),
        Probe(["runtime-scheduler-poison-restart"], providerKey => new RuntimeDeliveryContractTests()
            .Scheduler_poison_record_survives_restart_on_every_provider(providerKey)),
        Probe(["runtime-cancellation-dispose-and-reuse"], AssertRuntimeCancellationAndReuseAsync),
        Probe(
            [
                "identity-authority-adaptation",
                "identity-authority-relationship-atomicity",
                "iam-dispose-reopen-and-process-restart",
                "iam-cancellation-dispose-and-reuse"
            ],
            AssertIdentityRecoveryAsync,
            FailureWindows()),
        Probe(["secret-dispose-reopen-and-process-restart"], AssertSecretRestartAsync),
        Probe(["secret-cancellation-dispose-and-reuse"], AssertSecretCancellationAndReuseAsync),
        Probe(["distributed-command-failure-and-restart"], AssertDistributedFailureAndRestartAsync, FailureWindows()),
        Probe(["distributed-cancellation-dispose-and-reuse"], AssertDistributedCancellationAndReuseAsync)
    ];

    private static IReadOnlyList<string> FailureWindows() =>
        new[]
        {
            "before-provider-decision",
            "during-provider-decision",
            "after-durable-decision-before-caller-acknowledgement",
            "during-recovery"
        }.Order(StringComparer.Ordinal).ToArray();

    private static RecoveryScenarioProbe Probe(
        IReadOnlyList<string> scenarioIds,
        Func<string, Task> executeAsync,
        IReadOnlyList<string>? declaredFailureWindows = null) =>
        new(scenarioIds, executeAsync, declaredFailureWindows ?? []);

    private static void RequireProviderMatrixOptIn() =>
        Skip.If(
            !string.Equals(Environment.GetEnvironmentVariable(ProviderMatrixOptIn), "1", StringComparison.Ordinal),
            $"Set {ProviderMatrixOptIn}=1 to run the SQLite, SQL Server, PostgreSQL, and MongoDB recovery contract matrix.");

    private sealed record RecoveryScenarioProbe(
        IReadOnlyList<string> ScenarioIds,
        Func<string, Task> ExecuteAsync,
        IReadOnlyList<string> DeclaredFailureWindows);

    private sealed class FixedAccessContextAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
