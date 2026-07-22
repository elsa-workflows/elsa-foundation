using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Core.Indexing;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Microsoft.Extensions.Options;
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

    /// <summary>
    /// This literal is intentionally not derived from <see cref="ElsaGroundworkQueryRoutes.All"/>. It is the
    /// T077 denominator: adding, removing, or silently replacing a B7 physical route must make this test fail
    /// until the provider conformance probe is deliberately updated as well.
    /// </summary>
    public static IReadOnlyList<string> ExpectedB7PhysicalRoutes { get; } =
    [
        "activityExecutionInspection/page-summaries-by-workflow-execution",
        "activityExecutionHierarchy/find-latest-by-workflow-execution",
        "activityExecutionHierarchy/page-by-workflow-execution",
        "activityExecutionHierarchy/page-by-workflow-execution-and-scope",
        "activityExecutionState/page-by-workflow-execution",
        "activityExecutionState/page-by-parent-activity-execution",
        "bookmarkState/list-by-workflow-execution",
        "bookmarkState/list-by-stimulus-and-type",
        "bookmarkState/list-by-stimulus-type",
        "durableValueState/list-by-workflow-execution",
        "incidentState/page-attention-by-status",
        "workflowExecutableSourceReference/page-by-artifact",
        "workflowExecutableSourceReference/page-all",
        "workflowExecutableSourceReference/page-by-scope",
        "workflowExecutableSourceReference/page-live-all",
        "workflowExecutableSourceReference/page-live-by-scope",
        "workflowExecutableSourceReference/batch-expired",
        "workflowExecutableSourceReference/batch-retired",
        "workflowExecutableSourceReference/find-live-by-artifact",
        "workflowExecutable/page-all",
        "executableActivityTemplate/find-by-template-hash",
        "executableActivityTemplate/page-all",
        "workflowExecutionState/page-faulted-for-attention",
        "workflowExecutionState/page-history",
        "workflowExecutionState/page-pinned-executable-artifact-ids",
        "workflowTriggerBinding/list-by-publication",
        "workflowTriggerBinding/list-by-stimulus-and-type",
        "workflowTriggerBinding/list-by-artifact",
        "workflowTriggerBinding/list-by-stimulus-type"
    ];

    [Fact]
    public void B7_catalog_has_a_closed_physical_route_denominator_and_primary_read_contracts()
    {
        var physicalRoutes = ElsaGroundworkQueryRoutes.All
            .Where(route => route.Kind == ElsaGroundworkQueryRouteKind.BoundedRoute)
            .SelectMany(route => route.PhysicalRoutes.Select(physical => PhysicalRouteKey(route.DocumentKind, physical.Identity)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedB7PhysicalRoutes.Order(StringComparer.Ordinal), physicalRoutes);

        var primaryReads = ElsaGroundworkQueryRoutes.All
            .Where(route => route.Kind == ElsaGroundworkQueryRouteKind.PrimaryIdentityRead)
            .Select(route => (route.DocumentKind, route.PrimaryReadIdentity, route.MaximumResultCount, route.ResultOperation))
            .OrderBy(route => route.DocumentKind, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new (string DocumentKind, string? PrimaryReadIdentity, int MaximumResultCount, ElsaGroundworkQueryResultOperation ResultOperation)[]
            {
                (ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind, "primary.activity-execution-inspection.workflow-and-activity.v1", 1, ElsaGroundworkQueryResultOperation.First),
                (ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind, "primary.activity-execution-state.workflow-and-activity.v1", 1, ElsaGroundworkQueryResultOperation.First),
                (ElsaRuntimeStorageManifest.BookmarkStateDocumentKind, "primary.bookmark-state.workflow-and-bookmark.v1", 1, ElsaGroundworkQueryResultOperation.First),
                (ElsaRuntimeStorageManifest.DurableValueStateDocumentKind, "primary.durable-value-state.workflow-and-value.v1", 1, ElsaGroundworkQueryResultOperation.First),
                (ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind, "primary.workflow-executable.artifact.v1", 1, ElsaGroundworkQueryResultOperation.First),
                (ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind, "primary.workflow-executable-source-reference.reference.v1", 1, ElsaGroundworkQueryResultOperation.First),
                (ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, "primary.workflow-execution-state.execution.v1", 1, ElsaGroundworkQueryResultOperation.First)
            }.OrderBy(route => route.DocumentKind, StringComparer.Ordinal),
            primaryReads);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Every_B7_physical_route_admits_an_exact_empty_window_without_client_materialization(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        var manifestSource = new RuntimeUnitManifestSource("runtime-b7-physical-route-contract", B7DocumentKinds);
        var declaration = await manifestSource.CreateDeclarationAsync();
        await driver.ResetPhysicalAsync([manifestSource]);

        await using var client = await driver.OpenPhysicalClientAsync();
        var bounded = new RecordingBoundedDocumentStore(
            client.BoundedDocumentStore
            ?? throw new InvalidOperationException(
                "The physical provider did not expose its admitted bounded-query runtime."));
        var expected = ElsaGroundworkQueryRoutes.All
            .Where(route => route.Kind == ElsaGroundworkQueryRouteKind.BoundedRoute)
            .SelectMany(route => route.PhysicalRoutes.Select(physical => (Route: route, Physical: physical)))
            .GroupBy(pair => PhysicalRouteKey(pair.Route.DocumentKind, pair.Physical.Identity), StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(pair => PhysicalRouteKey(pair.Route.DocumentKind, pair.Physical.Identity), StringComparer.Ordinal)
            .ToArray();

        foreach (var (route, physical) in expected)
        {
            var unit = declaration.Manifest.StorageUnits.Single(candidate =>
                candidate.Identity.Value == route.DocumentKind);
            var physicalDefinition = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(
                unit.PhysicalStorage?.Policy).Definition;
            var query = CreateProbeQuery(route, physical, physicalDefinition);
            switch (route.ResultOperation)
            {
                case ElsaGroundworkQueryResultOperation.Any:
                    Assert.False(await bounded.AnyAsync(query));
                    break;
                case ElsaGroundworkQueryResultOperation.First:
                    Assert.Null(await bounded.FirstOrDefaultAsync(query));
                    break;
                default:
                {
                    var result = await bounded.QueryAsync(query);
                    Assert.Empty(result.Documents);
                    Assert.Equal(0, result.TotalCount);
                    Assert.Null(result.NextContinuation);
                    break;
                }
            }
        }

        Assert.Equal(
            ExpectedB7PhysicalRoutes.Order(StringComparer.Ordinal),
            bounded.AllObservedQueries
                .Select(query => PhysicalRouteKey(query.DocumentKind, query.QueryIdentity))
                .Order(StringComparer.Ordinal));
        Assert.All(bounded.Observations, observation =>
        {
            Assert.Equal(1, observation.Query.Take);
            Assert.InRange(observation.MaterializedDocuments, 0, 1);
            Assert.Null(observation.Query.Skip);
        });
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Hierarchy_pages_exclude_a_completed_root_without_assuming_root_sequence_order(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync(
            [new RuntimeUnitManifestSource(
                "runtime-hierarchy-root-order-contract",
                ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind)]);

        await using var client = await driver.OpenPhysicalClientAsync();
        var bounded = new RecordingBoundedDocumentStore(
            client.BoundedDocumentStore
            ?? throw new InvalidOperationException(
                "The physical provider did not expose its admitted bounded-query runtime."));
        var cursorCodec = new HmacActivityExecutionHierarchyCursorCodec(
            Options.Create(new ActivityExecutionHierarchyCursorOptions
            {
                SigningKey = "groundwork-hierarchy-conformance-key-32-bytes"
            }));
        var store = new GroundworkActivityExecutionHierarchyStore(
            client.DocumentStore,
            GroundworkProviderTestSerialization.Serializer,
            cursorCodec,
            bounded);
        await store.SaveAsync(HierarchyRecord("child-a", "outer", "outer", 2));
        await store.SaveAsync(HierarchyRecord("child-b", "outer", "child-a", 3));
        await store.SaveAsync(HierarchyRecord("outer", "outer", null, 4, boundary: true));

        var first = await store.ReadPageAsync(HierarchyQuery(limit: 1));
        var second = await store.ReadPageAsync(HierarchyQuery(limit: 1, first!.NextCursor));

        Assert.Equal("child-a", Assert.Single(first.Items).ActivityExecutionId);
        Assert.Equal("child-b", Assert.Single(second!.Items).ActivityExecutionId);
        Assert.NotNull(first.NextCursor);
        Assert.Null(second.NextCursor);
        Assert.All(
            bounded.Observations.Where(observation =>
                observation.Query.QueryIdentity ==
                ElsaRuntimeStorageManifest.PageActivityExecutionHierarchyByScopeQuery),
            observation =>
            {
                Assert.Contains(
                    observation.Query.Clauses.SelectMany(clause => clause.Comparisons),
                    comparison =>
                        comparison.Path ==
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyIsScopeRootField &&
                        comparison.Values.SequenceEqual([bool.FalseString.ToLowerInvariant()]));
                Assert.DoesNotContain(
                    observation.Query.Clauses.SelectMany(clause => clause.Comparisons),
                    comparison =>
                        comparison.Path ==
                            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField &&
                        comparison.Operator == QueryComparisonOperator.GreaterThan);
            });
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Primary_runtime_reads_use_document_identity_without_a_bounded_query_fallback(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([new RuntimeUnitManifestSource("runtime-primary-read-contract", PrimaryReadDocumentKinds)]);

        await using var client = await driver.OpenPhysicalClientAsync();
        var bounded = new RecordingBoundedDocumentStore(
            client.BoundedDocumentStore
            ?? throw new InvalidOperationException(
                "The physical provider did not expose its admitted bounded-query runtime."));
        var serializer = GroundworkProviderTestSerialization.Serializer;

        Assert.Null(await new GroundworkActivityExecutionInspectionStore(client.DocumentStore, serializer, bounded)
            .FindAsync("workflow", "activity"));
        Assert.Null(await new GroundworkActivityExecutionStateStore(client.DocumentStore, serializer, bounded)
            .FindAsync("workflow", "activity"));
        Assert.Null(await new GroundworkBookmarkStateStore(client.DocumentStore, serializer, bounded)
            .FindAsync("workflow", "bookmark"));
        Assert.Null(await new GroundworkDurableValueStateStore(client.DocumentStore, serializer, bounded)
            .FindAsync("workflow", "value"));
        Assert.Null(await new GroundworkWorkflowExecutableSourceReferenceStore(client.DocumentStore, serializer, bounded)
            .FindAsync("source"));
        Assert.Null(await new GroundworkWorkflowExecutableStore(client.DocumentStore, serializer, bounded)
            .FindAsync("artifact"));
        Assert.Null(await new GroundworkWorkflowExecutionStateStore(
                client.DocumentStore,
                serializer,
                GroundworkTestAccess.DefaultAccessContextAccessor,
                bounded)
            .FindAsync("workflow"));

        Assert.Empty(bounded.AllObservedQueries);
    }

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
                .Select(binding => binding.TriggerBindingId));
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
                Assert.Collection(
                    observation.Query.Order,
                    order =>
                    {
                        Assert.Equal(ElsaRuntimeStorageManifest.TriggerBindingIdField, order.Path);
                        Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
                    });
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
    public async Task Active_trigger_binding_type_pages_are_equivalent_and_materialized_inside_the_requested_window(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync(
            [new RuntimeUnitManifestSource(
                "runtime-bounded-trigger-binding-type-contract",
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

        var first = await store.ListByStimulusTypeAsync(
            new WorkflowTriggerBindingTypePageQuery("Event", limit: 2));
        await Assert.ThrowsAsync<InvalidDocumentQueryContinuationException>(async () =>
            await store.ListByStimulusTypeAsync(
                new WorkflowTriggerBindingTypePageQuery(
                    "Signal",
                    limit: 1,
                    continuationToken: first.NextContinuationToken)));
        var second = await store.ListByStimulusTypeAsync(
            new WorkflowTriggerBindingTypePageQuery(
                "Event",
                limit: 1,
                continuationToken: first.NextContinuationToken));

        Assert.Equal(
            ["binding-a", "binding-c", "binding-e"],
            first.Items
                .Concat(second.Items)
                .Select(binding => binding.TriggerBindingId));
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(3, second.TotalCount);
        Assert.NotNull(first.NextContinuationToken);
        Assert.Null(second.NextContinuationToken);
        Assert.All(
            bounded.Observations,
            observation =>
            {
                Assert.Equal(
                    ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusTypeQuery,
                    observation.Query.QueryIdentity);
                Assert.Collection(
                    observation.Query.Order,
                    order =>
                    {
                        Assert.Equal(ElsaRuntimeStorageManifest.TriggerBindingIdField, order.Path);
                        Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
                    });
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
        Assert.Equal(32, publishedSeen);
    }

    private static readonly string[] B7DocumentKinds =
    [
        ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind,
        ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind,
        ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
        ElsaRuntimeStorageManifest.DurableValueStateDocumentKind,
        ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind,
        ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind
    ];

    private static readonly string[] PrimaryReadDocumentKinds =
    [
        ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
        ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind,
        ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
        ElsaRuntimeStorageManifest.DurableValueStateDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind
    ];

    private static ActivityExecutionHierarchyQuery HierarchyQuery(int limit, string? cursor = null) =>
        new(
            "wf",
            "outer",
            cursor,
            limit,
            new HashSet<ActivityExecutionHierarchyInclude>(),
            "structure",
            "tenant:a");

    private static ActivityExecutionHierarchyRecord HierarchyRecord(
        string id,
        string scope,
        string? parent,
        long sequence,
        bool boundary = false)
    {
        var metadata = boundary
            ? new Dictionary<string, string>
            {
                ["activity.definitionId"] = $"def-{id}",
                ["activity.definitionVersionId"] = $"ver-{id}",
                ["activity.version"] = "1.0.0",
                ["activity.templateHash"] = $"hash-{id}"
            }
            : new Dictionary<string, string>();
        var projection = new ActivityExecutionInspectionProjection(
            id,
            "wf",
            $"node-{id}",
            $"authored-{id}",
            boundary ? "elsa.graph-activity" : "elsa.test",
            "1",
            ActivityExecutionStatus.Completed,
            null,
            sequence,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "first",
            "last",
            DateTimeOffset.UnixEpoch,
            ActivitySchedulingProvenance.From("wf", parent, parent, null, null, null, scope, "test"),
            ["Done"],
            [],
            [],
            [],
            metadata,
            scope,
            null);
        return ActivityExecutionHierarchyProjector.FromInspection(projection);
    }

    private static string PhysicalRouteKey(string documentKind, string routeIdentity) =>
        $"{documentKind}/{routeIdentity}";

    private static DocumentQuery CreateProbeQuery(
        ElsaGroundworkQueryRoute route,
        ElsaGroundworkPhysicalQueryRoute physical,
        PhysicalTableDefinition physicalDefinition)
    {
        var query = new DocumentQuery(
            route.DocumentKind,
            physical.Identity,
            physical.Predicates
                .Select(predicate => DocumentQueryClause.Of(
                    CreateProbeComparison(route.DocumentKind, predicate, physicalDefinition)))
                .ToArray(),
            physical.OrderingFields
                .Select((path, index) => new DocumentQueryOrder(
                    path,
                    physical.OrderingDirections?[index] ?? PhysicalSortDirection.Ascending))
                .ToArray(),
            take: 1);
        return physical.LatestPerKeyPath is { } latestPerKeyPath
            ? query.LatestPerKey(latestPerKeyPath)
            : query;
    }

    private static DocumentQueryComparison CreateProbeComparison(
        string documentKind,
        ElsaGroundworkQueryPredicate predicate,
        PhysicalTableDefinition physicalDefinition)
    {
        var value = ProbeValue(documentKind, predicate.Path, physicalDefinition);
        var operations = predicate.Operations;

        if (operations.Contains(PortableQueryOperation.Equal))
            return DocumentQueryComparison.Equal(predicate.Path, value);
        if (operations.Contains(PortableQueryOperation.GreaterThan))
            return DocumentQueryComparison.GreaterThan(predicate.Path, value);
        if (operations.Contains(PortableQueryOperation.GreaterThanOrEqual))
            return DocumentQueryComparison.GreaterThanOrEqual(predicate.Path, value);
        if (operations.Contains(PortableQueryOperation.LessThan))
            return DocumentQueryComparison.LessThan(predicate.Path, value);
        if (operations.Contains(PortableQueryOperation.LessThanOrEqual))
            return DocumentQueryComparison.LessThanOrEqual(predicate.Path, value);

        throw new Xunit.Sdk.XunitException(
            $"The B7 route probe does not know how to materialize predicate '{predicate.Path}' for '{documentKind}'.");
    }

    private static string ProbeValue(
        string documentKind,
        string path,
        PhysicalTableDefinition physicalDefinition)
    {
        if (path == ElsaRuntimeStorageManifest.CollectionField)
        {
            return documentKind switch
            {
                ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind =>
                    ElsaRuntimeStorageManifest.WorkflowExecutableCollection,
                ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind =>
                    ElsaRuntimeStorageManifest.ExecutableActivityTemplateCollection,
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind =>
                    ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceCollection,
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind =>
                    ElsaRuntimeStorageManifest.WorkflowExecutionStateCollection,
                _ => "contract"
            };
        }

        var type = physicalDefinition.ProjectedColumns
            .SingleOrDefault(column => column.Path == path)
            ?.Type;
        return type switch
        {
            PortablePhysicalType.Boolean => bool.TrueString.ToLowerInvariant(),
            PortablePhysicalType.Int32 or
            PortablePhysicalType.Int64 or
            PortablePhysicalType.Decimal => "1",
            PortablePhysicalType.DateTime =>
                Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            PortablePhysicalType.Guid => "11111111-1111-1111-1111-111111111111",
            _ when path is ElsaRuntimeStorageManifest.IsRetiredField => bool.TrueString,
            _ => "contract"
        };
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
            $"node-{id}",
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
        public List<DocumentQuery> FirstQueries { get; } = [];
        public List<DocumentQuery> AnyQueries { get; } = [];

        public IEnumerable<DocumentQuery> AllObservedQueries => Observations
            .Select(observation => observation.Query)
            .Concat(FirstQueries)
            .Concat(AnyQueries);

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

        public async Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.FirstOrDefaultAsync(query, cancellationToken);
            FirstQueries.Add(query);
            return result;
        }

        public async Task<bool> AnyAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.AnyAsync(query, cancellationToken);
            AnyQueries.Add(query);
            return result;
        }
    }

    private sealed record QueryObservation(
        DocumentQuery Query,
        int MaterializedDocuments);
}
