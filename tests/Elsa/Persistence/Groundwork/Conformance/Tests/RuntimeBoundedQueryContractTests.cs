using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class RuntimeBoundedQueryContractTests
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
    public async Task Due_work_is_equivalent_and_materialized_inside_the_requested_window(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([new DueWorkManifestSource()]);

        await using var client = await driver.OpenPhysicalClientAsync();
        var bounded = new RecordingBoundedDocumentStore(
            client.BoundedDocumentStore
            ?? throw new InvalidOperationException(
                "The physical provider did not expose its admitted bounded-query runtime."));
        var timers = new GroundworkDurableTimerStore(
            client.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            bounded);
        var schedules = new GroundworkRecurringTriggerScheduleStore(
            client.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            bounded);

        await timers.SaveAsync(Timer("timer-b", TimeSpan.FromMinutes(-2)));
        await timers.SaveAsync(Timer("timer-a", TimeSpan.FromMinutes(-2)));
        await timers.SaveAsync(Timer("timer-early", TimeSpan.FromMinutes(-5)));
        await timers.SaveAsync(Timer("timer-future", TimeSpan.FromMinutes(5)));

        await schedules.SaveAsync(Schedule("schedule-b", TimeSpan.FromMinutes(-2)));
        await schedules.SaveAsync(Schedule("schedule-a", TimeSpan.FromMinutes(-2)));
        await schedules.SaveAsync(Schedule("schedule-early", TimeSpan.FromMinutes(-5)));
        await schedules.SaveAsync(Schedule("schedule-inactive", TimeSpan.FromMinutes(-10)) with
        {
            IsActive = false
        });
        await schedules.SaveAsync(Schedule("schedule-future", TimeSpan.FromMinutes(5)));

        var dueTimers = await timers.ListDueAsync(Now, limit: 2);
        var dueSchedules = await schedules.ListDueAsync(Now, limit: 2);

        Assert.Equal(
            ["timer-early", "timer-a"],
            dueTimers.Select(timer => timer.TimerId));
        Assert.Equal(
            ["schedule-early", "schedule-a"],
            dueSchedules.Select(schedule => schedule.ScheduleId));
        Assert.Collection(
            bounded.Observations,
            observation => AssertBounded(observation, ElsaRuntimeStorageManifest.ListDueDurableTimersQuery),
            observation => AssertBounded(observation, ElsaRuntimeStorageManifest.ListDueRecurringTriggerSchedulesQuery));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Active_trigger_binding_pages_are_equivalent_and_materialized_inside_the_requested_window(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync(
            [new RuntimeUnitManifestSource(
                "runtime-bounded-trigger-binding-contract",
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind)]);

        await using var client = await driver.OpenPhysicalClientAsync();
        var bounded = new RecordingBoundedDocumentStore(
            client.BoundedDocumentStore
            ?? throw new InvalidOperationException(
                "The physical provider did not expose its admitted bounded-query runtime."));
        IWorkflowTriggerBindingStore store = new GroundworkWorkflowTriggerBindingStore(
            client.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            bounded);

        await store.SaveAsync(Binding("binding-a"));
        await store.SaveAsync(Binding("binding-b", isActive: false));
        await store.SaveAsync(Binding("binding-c"));
        await store.SaveAsync(Binding("binding-d", isActive: false));
        await store.SaveAsync(Binding("binding-e"));
        await store.SaveAsync(Binding("binding-ignored", stimulusType: "Signal"));

        var first = await store.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "shared", limit: 2));
        await Assert.ThrowsAsync<InvalidDocumentQueryContinuationException>(async () =>
            await store.ListByStimulusAsync(
                new WorkflowTriggerBindingPageQuery(
                    "Event",
                    "another-stimulus",
                    limit: 1,
                    continuationToken: first.NextContinuationToken)));
        var second = await store.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery(
                "Event",
                "shared",
                limit: 1,
                continuationToken: first.NextContinuationToken));

        Assert.Equal(
            ["binding-a", "binding-c", "binding-e"],
            first.Items
                .Concat(second.Items)
                .Select(binding => binding.TriggerBindingId)
                .Order(StringComparer.Ordinal));
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(3, second.TotalCount);
        Assert.NotNull(first.NextContinuationToken);
        Assert.Null(second.NextContinuationToken);
        Assert.All(
            bounded.Observations,
            observation =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusAndTypeQuery, observation.Query.QueryIdentity);
                Assert.InRange(observation.Query.Take!.Value, 1, 2);
                Assert.InRange(observation.MaterializedDocuments, 0, observation.Query.Take.Value);
                Assert.Contains(
                    observation.Query.Clauses.SelectMany(clause => clause.Comparisons),
                    comparison =>
                        comparison.Path == ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField &&
                        comparison.Values.SequenceEqual([bool.TrueString.ToLowerInvariant()]));
            });
        Assert.All(bounded.Observations, observation => Assert.Null(observation.Query.Skip));
        Assert.Null(bounded.Observations[0].Query.Continuation);
        Assert.Equal(first.NextContinuationToken, bounded.Observations[1].Query.Continuation);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Trigger_binding_continuation_has_live_view_semantics_across_independent_publication(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync(
            [new RuntimeUnitManifestSource(
                "runtime-trigger-binding-live-view-contract",
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind)]);

        await using var readerClient = await driver.OpenPhysicalClientAsync();
        IWorkflowTriggerBindingStore reader = new GroundworkWorkflowTriggerBindingStore(
            readerClient.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            readerClient.BoundedDocumentStore);
        var originalIds = Enumerable.Range(0, 6).Select(index => $"original-{index:D2}").ToArray();
        foreach (var id in originalIds)
            await reader.SaveAsync(Binding(id));

        var first = await reader.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "shared", limit: 2));
        Assert.NotNull(first.NextContinuationToken);

        await using (var writerClient = await driver.OpenPhysicalClientAsync())
        {
            IWorkflowTriggerBindingStore writer = new GroundworkWorkflowTriggerBindingStore(
                writerClient.DocumentStore,
                GroundworkProviderTestSerialization.Serializer,
                writerClient.BoundedDocumentStore);
            foreach (var id in Enumerable.Range(0, 32).Select(index => $"published-after-page-{index:D2}"))
                await writer.SaveAsync(Binding(id));
        }

        var resumed = new List<WorkflowTriggerBinding>();
        var continuation = first.NextContinuationToken;
        while (continuation is not null)
        {
            var page = await reader.ListByStimulusAsync(
                new WorkflowTriggerBindingPageQuery(
                    "Event",
                    "shared",
                    limit: 3,
                    continuationToken: continuation));
            Assert.Equal(38, page.TotalCount);
            resumed.AddRange(page.Items);
            continuation = page.NextContinuationToken;
        }

        var traversed = first.Items.Concat(resumed).Select(binding => binding.TriggerBindingId).ToArray();
        Assert.Equal(traversed.Length, traversed.Distinct(StringComparer.Ordinal).Count());
        Assert.All(originalIds, id => Assert.Contains(id, traversed));
        var publishedSeen = traversed.Count(id => id.StartsWith("published-after-page-", StringComparison.Ordinal));
        Assert.InRange(publishedSeen, 1, 31);
    }

    private static void AssertBounded(
        QueryObservation observation,
        string queryIdentity)
    {
        Assert.Equal(queryIdentity, observation.Query.QueryIdentity);
        Assert.Equal(2, observation.Query.Take);
        Assert.InRange(observation.Query.Order.Count, 2, 3);
        Assert.InRange(observation.MaterializedDocuments, 0, 2);
    }

    private static DurableTimer Timer(string id, TimeSpan dueOffset) =>
        new(
            id,
            "workflow-due-contract",
            "DurableTimer",
            $"sha256:{id}",
            Now.Add(dueOffset),
            Now,
            JsonSerializer.SerializeToElement(new { reason = "contract" }));

    private static RecurringTriggerSchedule Schedule(string id, TimeSpan nextOffset) =>
        new(
            id,
            "artifact-due-contract",
            "Timer",
            $"sha256:{id}",
            RecurringScheduleKind.Interval,
            "PT5M",
            Now.Add(nextOffset),
            Now);

    private static WorkflowTriggerBinding Binding(
        string id,
        bool isActive = true,
        string stimulusType = "Event") =>
        new(
            id,
            $"artifact-{id}",
            $"definition-{id}",
            "1",
            $"hash-{id}",
            $"node-{id}",
            stimulusType,
            "shared",
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: Now,
            IsActive: isActive);

    private sealed class DueWorkManifestSource() : RuntimeUnitManifestSource(
        "runtime-bounded-due-work-contract",
        ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
        ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind);

    private class RuntimeUnitManifestSource(
        string featureIdentity,
        params string[] documentKinds) : IGroundworkStorageManifestSource
    {
        public string FeatureIdentity => featureIdentity;

        public async ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
            CancellationToken cancellationToken = default)
        {
            var runtime = await new RuntimeGroundworkStorageManifestSource()
                .CreateDeclarationAsync(cancellationToken);
            return new GroundworkStorageManifestDeclaration(
                FeatureIdentity,
                runtime.Manifest with
                {
                    StorageUnits = runtime.Manifest.StorageUnits
                        .Where(unit => documentKinds.Contains(unit.Identity.Value, StringComparer.Ordinal))
                        .ToArray()
                },
                [],
                [],
                [],
                []);
        }
    }

    private sealed class RecordingBoundedDocumentStore(IBoundedDocumentStore inner) : IBoundedDocumentStore
    {
        public List<QueryObservation> Observations { get; } = [];

        public async Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.QueryAsync(query, cancellationToken);
            Observations.Add(new QueryObservation(query, result.Documents.Count));
            return result;
        }

        public Task<long> CountAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.CountAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);
    }

    private sealed record QueryObservation(
        DocumentQuery Query,
        int MaterializedDocuments);
}
