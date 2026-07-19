using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class RuntimeTransitionContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan VisibilityTimeout = TimeSpan.FromMinutes(5);

    public static TheoryData<string> Providers => new()
    {
        "sqlite",
        "sqlserver",
        "postgresql",
        "mongodb"
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task DurableTimer_CreateClaimExpiryAndStaleCompletionAreConditionalOnEveryProvider(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([
            new RuntimeTransitionManifestSource(ElsaRuntimeStorageManifest.DurableTimerDocumentKind)
        ]);

        await using var firstClient = await driver.OpenPhysicalClientAsync();
        await using var secondClient = await driver.OpenPhysicalClientAsync();
        var first = new GroundworkDurableTimerStore(
            firstClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            firstClient.BoundedDocumentStore);
        var second = new GroundworkDurableTimerStore(
            secondClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            secondClient.BoundedDocumentStore);

        var original = Timer("original");
        var competing = Timer("competing");
        var stored = await Task.WhenAll(
            first.SaveAsync(original).AsTask(),
            second.SaveAsync(competing).AsTask());
        Assert.Single(stored.Select(timer => timer.StimulusHash).Distinct(StringComparer.Ordinal));

        var initialClaims = await Task.WhenAll(
            first.ClaimDueAsync(ClaimRequest("owner-a", Now)).AsTask(),
            second.ClaimDueAsync(ClaimRequest("owner-b", Now)).AsTask());
        var initial = Assert.Single(initialClaims.SelectMany(claims => claims));

        await using var restartedClient = await driver.OpenPhysicalClientAsync();
        var restarted = new GroundworkDurableTimerStore(
            restartedClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            restartedClient.BoundedDocumentStore);
        var reclaimed = Assert.Single(await restarted.ClaimDueAsync(
            ClaimRequest("owner-restarted", Now.Add(VisibilityTimeout))));
        Assert.True(reclaimed.FencingToken > initial.FencingToken);
        Assert.Equal(
            RuntimeDurableTimerClaimTransitionStatus.Stale,
            (await first.CompleteClaimAsync(initial)).Status);
        Assert.Equal(
            RuntimeDurableTimerClaimTransitionStatus.Succeeded,
            (await restarted.CompleteClaimAsync(reclaimed)).Status);
        Assert.Null(await restarted.FindAsync("wf-timer-transition", "timer-1"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task RecurringSchedule_AdvanceHasOneCASWinnerOnEveryProvider(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([
            new RuntimeTransitionManifestSource(ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind)
        ]);

        await using var firstClient = await driver.OpenPhysicalClientAsync();
        await using var secondClient = await driver.OpenPhysicalClientAsync();
        var first = new GroundworkRecurringTriggerScheduleStore(
            firstClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            firstClient.BoundedDocumentStore);
        var second = new GroundworkRecurringTriggerScheduleStore(
            secondClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            secondClient.BoundedDocumentStore);
        var schedule = await first.SaveAsync(new RecurringTriggerSchedule(
            "schedule-1",
            "artifact-1",
            "Timer",
            "schedule-hash",
            RecurringScheduleKind.Interval,
            "PT5M",
            Now.AddMinutes(-1),
            Now.AddMinutes(-2)));
        var next = Now.AddMinutes(5);

        var outcomes = await Task.WhenAll(
            first.TryAdvanceAsync(schedule.ScheduleId, schedule.NextOccurrence, next).AsTask(),
            second.TryAdvanceAsync(schedule.ScheduleId, schedule.NextOccurrence, next).AsTask());

        Assert.Single(outcomes.Where(outcome => outcome));
        Assert.Equal(next, (await first.FindAsync(schedule.ScheduleId))!.NextOccurrence);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task IncidentAndWorkflowHold_RacesSelectOneDurableWinnerOnEveryProvider(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([
            new RuntimeTransitionManifestSource(
                ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
                ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind)
        ]);

        await using (var firstClient = await driver.OpenPhysicalClientAsync())
        await using (var secondClient = await driver.OpenPhysicalClientAsync())
        {
            var firstIncidentStore = new GroundworkIncidentStateStore(
                firstClient.DocumentStore,
                GroundworkProviderTestSerialization.Serializer,
                firstClient.BoundedDocumentStore);
            var secondIncidentStore = new GroundworkIncidentStateStore(
                secondClient.DocumentStore,
                GroundworkProviderTestSerialization.Serializer,
                secondClient.BoundedDocumentStore);
            var incidentOutcomes = await Task.WhenAll(
                firstIncidentStore.TryAddAsync(Incident("first")).AsTask(),
                secondIncidentStore.TryAddAsync(Incident("second")).AsTask());
            Assert.Single(incidentOutcomes.Where(outcome => outcome));

            var seedHoldStore = new GroundworkWorkflowHoldStateStore(
                firstClient.DocumentStore,
                GroundworkProviderTestSerialization.Serializer,
                firstClient.BoundedDocumentStore);
            await seedHoldStore.SaveAsync(Hold("seed"));

            var loadBarrier = new SharedLoadBarrier(
                ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind,
                "hold-1");
            var firstHoldStore = new GroundworkWorkflowHoldStateStore(
                new LoadBarrierDocumentStore(firstClient.DocumentStore, loadBarrier),
                GroundworkProviderTestSerialization.Serializer,
                firstClient.BoundedDocumentStore);
            var secondHoldStore = new GroundworkWorkflowHoldStateStore(
                new LoadBarrierDocumentStore(secondClient.DocumentStore, loadBarrier),
                GroundworkProviderTestSerialization.Serializer,
                secondClient.BoundedDocumentStore);
            var holdOutcomes = await Task.WhenAll(
                TryTransitionAsync(() => firstHoldStore.SaveAsync(Hold("first")).AsTask()),
                TryTransitionAsync(() => secondHoldStore.SaveAsync(Hold("second")).AsTask()));
            Assert.Single(holdOutcomes.Where(outcome => outcome));
        }

        await using var restartedClient = await driver.OpenPhysicalClientAsync();
        var restartedIncidentStore = new GroundworkIncidentStateStore(
            restartedClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            restartedClient.BoundedDocumentStore);
        var restartedHoldStore = new GroundworkWorkflowHoldStateStore(
            restartedClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            restartedClient.BoundedDocumentStore);
        Assert.Contains(
            (await restartedIncidentStore.FindAsync("wf-transition", "incident-1"))!.Message,
            new[] { "first", "second" });
        Assert.Contains(
            (await restartedHoldStore.FindAsync("hold-1"))!.Metadata["value"],
            new[] { "first", "second" });
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task PublicationProjection_PrepareAndActivationAreConditionalAfterReopenOnEveryProvider(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions);
        await driver.ResetPhysicalAsync([
            new RuntimeTransitionManifestSource(
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
                ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind,
                ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind)
        ]);

        string triggerWinner;
        string scheduleWinner;
        await using (var firstClient = await driver.OpenPhysicalClientAsync())
        await using (var secondClient = await driver.OpenPhysicalClientAsync())
        {
            var firstTriggerStore = new GroundworkWorkflowTriggerBindingStore(
                firstClient.DocumentStore,
                GroundworkProviderTestSerialization.Serializer,
                firstClient.BoundedDocumentStore);
            var secondTriggerStore = new GroundworkWorkflowTriggerBindingStore(
                secondClient.DocumentStore,
                GroundworkProviderTestSerialization.Serializer,
                secondClient.BoundedDocumentStore);
            var triggerPrepareOutcomes = await Task.WhenAll(
                TryTransitionAsync(() => firstTriggerStore.PreparePublicationAsync(
                    "publication-prepare-trigger",
                    [Binding("publication-prepare-trigger", "artifact-first", "node-first")]).AsTask()),
                TryTransitionAsync(() => secondTriggerStore.PreparePublicationAsync(
                    "publication-prepare-trigger",
                    [Binding("publication-prepare-trigger", "artifact-second", "node-second")]).AsTask()));
            Assert.Single(triggerPrepareOutcomes.Where(outcome => outcome));
            Assert.Single(await firstTriggerStore.ListByPublicationAsync("publication-prepare-trigger"));

            var firstScheduleStore = new GroundworkRecurringTriggerScheduleStore(
                firstClient.DocumentStore,
                GroundworkProviderTestSerialization.Serializer,
                firstClient.BoundedDocumentStore);
            var secondScheduleStore = new GroundworkRecurringTriggerScheduleStore(
                secondClient.DocumentStore,
                GroundworkProviderTestSerialization.Serializer,
                secondClient.BoundedDocumentStore);
            var schedulePrepareOutcomes = await Task.WhenAll(
                TryTransitionAsync(() => firstScheduleStore.PreparePublicationAsync(
                    "publication-prepare-schedule",
                    [Schedule("publication-prepare-schedule", "artifact-first", "node-first")]).AsTask()),
                TryTransitionAsync(() => secondScheduleStore.PreparePublicationAsync(
                    "publication-prepare-schedule",
                    [Schedule("publication-prepare-schedule", "artifact-second", "node-second")]).AsTask()));
            Assert.Single(schedulePrepareOutcomes.Where(outcome => outcome));
            Assert.Single(await firstScheduleStore.ListByPublicationAsync("publication-prepare-schedule"));

            await PrepareActivationRaceAsync(firstTriggerStore, firstScheduleStore);
            var triggerActivationOutcomes = await Task.WhenAll(
                TryTransitionAsync(() => firstTriggerStore.ActivatePublicationAsync(
                    "publication-a",
                    "publication-old").AsTask()),
                TryTransitionAsync(() => secondTriggerStore.ActivatePublicationAsync(
                    "publication-b",
                    "publication-old").AsTask()));
            Assert.Single(triggerActivationOutcomes.Where(outcome => outcome));
            triggerWinner = await FindActiveTriggerPublicationAsync(firstTriggerStore);

            var scheduleActivationOutcomes = await Task.WhenAll(
                TryTransitionAsync(() => firstScheduleStore.ActivatePublicationAsync(
                    "publication-a",
                    "publication-old").AsTask()),
                TryTransitionAsync(() => secondScheduleStore.ActivatePublicationAsync(
                    "publication-b",
                    "publication-old").AsTask()));
            Assert.Single(scheduleActivationOutcomes.Where(outcome => outcome));
            scheduleWinner = await FindActiveSchedulePublicationAsync(firstScheduleStore);
        }

        await using var restartedClient = await driver.OpenPhysicalClientAsync();
        var restartedTriggerStore = new GroundworkWorkflowTriggerBindingStore(
            restartedClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            restartedClient.BoundedDocumentStore);
        var restartedScheduleStore = new GroundworkRecurringTriggerScheduleStore(
            restartedClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            restartedClient.BoundedDocumentStore);
        Assert.Equal(triggerWinner, await FindActiveTriggerPublicationAsync(restartedTriggerStore));
        Assert.Equal(scheduleWinner, await FindActiveSchedulePublicationAsync(restartedScheduleStore));

        var triggerLoser = StringComparer.Ordinal.Equals(triggerWinner, "publication-a")
            ? "publication-b"
            : "publication-a";
        var scheduleLoser = StringComparer.Ordinal.Equals(scheduleWinner, "publication-a")
            ? "publication-b"
            : "publication-a";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            restartedTriggerStore.ActivatePublicationAsync(triggerLoser, "publication-old").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            restartedScheduleStore.ActivatePublicationAsync(scheduleLoser, "publication-old").AsTask());
    }

    private static RuntimeDurableTimerClaimRequest ClaimRequest(string ownerId, DateTimeOffset now) =>
        new(ownerId, now, VisibilityTimeout, limit: 1);

    private static DurableTimer Timer(string stimulusHash) =>
        new(
            "timer-1",
            "wf-timer-transition",
            "DurableTimer",
            stimulusHash,
            Now.AddMinutes(-1),
            Now.AddMinutes(-2));

    private static IncidentState Incident(string message) =>
        new(
            "incident-1",
            "wf-transition",
            activityExecutionId: null,
            executableNodeId: null,
            IncidentSeverity.Error,
            IncidentStatus.Open,
            IncidentResolutionAction.None,
            failureType: "System.Exception",
            message,
            Now,
            resolvedAt: null);

    private static WorkflowHoldState Hold(string value) =>
        new(
            "hold-1",
            "wf-transition",
            metadata: new Dictionary<string, string> { ["value"] = value });

    private static WorkflowTriggerBinding Binding(
        string publicationId,
        string artifactId,
        string nodeId) =>
        new(
            WorkflowTriggerBinding.BuildId(publicationId, artifactId, nodeId, "hash-transition"),
            artifactId,
            $"definition-{artifactId}",
            "1.0.0",
            $"hash-{artifactId}",
            nodeId,
            "Event",
            "hash-transition",
            CorrelationScope: null,
            new Dictionary<string, string>(),
            Now,
            PublicationId: publicationId,
            SlotId: "slot-a",
            IsActive: false);

    private static RecurringTriggerSchedule Schedule(
        string publicationId,
        string artifactId,
        string nodeId) =>
        new(
            RecurringTriggerSchedule.BuildId(publicationId, artifactId, nodeId),
            artifactId,
            "Timer",
            "hash-transition",
            RecurringScheduleKind.Interval,
            "PT5M",
            Now.AddMinutes(5),
            Now,
            PublicationId: publicationId,
            SlotId: "slot-a",
            IsActive: false);

    private static async Task PrepareActivationRaceAsync(
        IWorkflowTriggerBindingStore triggerStore,
        IRecurringTriggerScheduleStore scheduleStore)
    {
        foreach (var publicationId in new[] { "publication-old", "publication-a", "publication-b" })
        {
            var artifactId = publicationId.Replace("publication-", "artifact-", StringComparison.Ordinal);
            var nodeId = publicationId.Replace("publication-", "node-", StringComparison.Ordinal);
            await triggerStore.PreparePublicationAsync(
                publicationId,
                [Binding(publicationId, artifactId, nodeId)]);
            await scheduleStore.PreparePublicationAsync(
                publicationId,
                [Schedule(publicationId, artifactId, nodeId)]);
        }

        await triggerStore.ActivatePublicationAsync("publication-old", replacedPublicationId: null);
        await scheduleStore.ActivatePublicationAsync("publication-old", replacedPublicationId: null);
    }

    private static async Task<string> FindActiveTriggerPublicationAsync(
        IWorkflowTriggerBindingStore store)
    {
        var active = new List<string>();
        foreach (var publicationId in new[] { "publication-a", "publication-b" })
        {
            if (Assert.Single(await store.ListByPublicationAsync(publicationId)).IsActive)
                active.Add(publicationId);
        }

        return Assert.Single(active);
    }

    private static async Task<string> FindActiveSchedulePublicationAsync(
        IRecurringTriggerScheduleStore store)
    {
        var active = new List<string>();
        foreach (var publicationId in new[] { "publication-a", "publication-b" })
        {
            if (Assert.Single(await store.ListByPublicationAsync(publicationId)).IsActive)
                active.Add(publicationId);
        }

        return Assert.Single(active);
    }

    private static async Task<bool> TryTransitionAsync(Func<Task> transition)
    {
        try
        {
            await transition();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class RuntimeTransitionManifestSource(params string[] documentKinds)
        : IGroundworkStorageManifestSource
    {
        public string FeatureIdentity => "runtime-transition-conformance";

        public async ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
            CancellationToken cancellationToken = default)
        {
            var runtime = await new RuntimeGroundworkStorageManifestSource()
                .CreateDeclarationAsync(cancellationToken);
            var transitionManifest = runtime.Manifest with
            {
                StorageUnits = runtime.Manifest.StorageUnits
                    .Where(unit => documentKinds.Contains(unit.Identity.Value, StringComparer.Ordinal))
                    .ToArray()
            };

            return new GroundworkStorageManifestDeclaration(
                FeatureIdentity,
                transitionManifest,
                [],
                [],
                [],
                []);
        }
    }

    private sealed class SharedLoadBarrier(string documentKind, string id)
    {
        private readonly TaskCompletionSource _allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public async Task WaitAsync(
            string observedDocumentKind,
            string observedId,
            CancellationToken cancellationToken)
        {
            if (!StringComparer.Ordinal.Equals(documentKind, observedDocumentKind) ||
                !StringComparer.Ordinal.Equals(id, observedId))
            {
                return;
            }

            if (Interlocked.Increment(ref _arrivals) == 2)
                _allArrived.TrySetResult();

            await _allArrived.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class LoadBarrierDocumentStore(
        IDocumentStore inner,
        SharedLoadBarrier barrier) : IDocumentStore
    {
        public DocumentStoreAccess Access => inner.Access;
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public async Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default)
        {
            var envelope = await inner.LoadAsync(documentKind, id, cancellationToken);
            await barrier.WaitAsync(documentKind, id, cancellationToken);
            return envelope;
        }

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
            DocumentStoreQuery query,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);

        public Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            inner.BeginAsync(scope, cancellationToken);
    }
}
