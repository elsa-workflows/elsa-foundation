using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowInstanceServicesTests
{
    private readonly InMemoryWorkflowExecutionStateStore _workflowStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStore = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
    private readonly InMemoryIncidentStateStore _incidentStore = new();
    private readonly InMemoryDurableValueStateStore _durableValueStore = new();
    private static readonly IActivityInspectionContextAsync AllowAll = new AllowAllActivityExecutionInspectionAuthorizationContext();

    private WorkflowInstanceListService NewListInstanceHandler(IWorkflowExecutionStateStore? workflowStore = null) =>
        new(workflowStore ?? _workflowStore, _activityStore, _incidentStore, AllowAll);

    private WorkflowInstanceDetailsService NewGetInstanceHandler(RuntimeCheckpointCadenceInspector? cadenceInspector = null) =>
        new(_workflowStore, _inspectionStore, _incidentStore, _durableValueStore, new DefaultRuntimePayloadCapturePolicy(), AllowAll,
            cadenceInspector ?? ImmediateCadenceInspector());

    private static RuntimeCheckpointCadenceInspector ImmediateCadenceInspector() =>
        new([]);

    private static RuntimeCheckpointCadenceInspector CoalescedCadenceInspector(int maxSegmentCheckpoints) =>
        new([new CoalescingRuntimeCheckpointPersistenceOptions { MaxSegmentCheckpoints = maxSegmentCheckpoints }]);

    private async Task SeedWorkflowInstancesAsync(int count)
    {
        for (var index = 0; index < count; index++)
            await _workflowStore.SaveAsync(Workflow($"wf-{index:D3}", WorkflowExecutionStatus.Completed, "definition-1", updatedAt: Now(-index)));
    }

    [Fact]
    public async Task ListWorkflowInstances_ReturnsFilteredSummariesWithActivityAndIncidentCounts()
    {
        await _workflowStore.SaveAsync(Workflow("wf-old", WorkflowExecutionStatus.Completed, "definition-1", updatedAt: Now(-20)));
        await _workflowStore.SaveAsync(Workflow("wf-new", WorkflowExecutionStatus.Running, "definition-1", correlationId: "correlation-1", updatedAt: Now(-1)));
        await _workflowStore.SaveAsync(Workflow("wf-other", WorkflowExecutionStatus.Running, "definition-2", updatedAt: Now(-2)));
        await _activityStore.SaveAsync(Activity("wf-new", "activity-1", ActivityExecutionStatus.Running));
        await _activityStore.SaveAsync(Activity("wf-new", "activity-2", ActivityExecutionStatus.Completed));
        await _incidentStore.TryAddAsync(Incident("wf-new", "incident-1"));
        var handler = NewListInstanceHandler();

        var result = await handler.ListAsync(new ListWorkflowInstances("Running", "definition-1", "correlation-1", 10), CancellationToken.None);

        var summary = Assert.Single(result.Items);
        Assert.Equal("wf-new", summary.WorkflowExecutionId);
        Assert.Equal("Running", summary.Status);
        Assert.Equal("definition-1", summary.DefinitionId);
        Assert.Equal("correlation-1", summary.CorrelationId);
        Assert.Equal(2, summary.ActivityCount);
        Assert.Equal(1, summary.IncidentCount);
        Assert.Equal(1, result.Count);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task ListWorkflowInstances_uses_the_bounded_store_query_instead_of_materializing_all_states()
    {
        await _workflowStore.SaveAsync(Workflow("wf-1", WorkflowExecutionStatus.Completed, "definition-1"));
        var store = new BoundedQueryOnlyWorkflowExecutionStateStore(_workflowStore);
        var handler = NewListInstanceHandler(store);

        var result = await handler.ListAsync(new ListWorkflowInstances(null, null, null, 10), CancellationToken.None);

        Assert.Equal("wf-1", Assert.Single(result.Items).WorkflowExecutionId);
        Assert.True(store.QueryPageCalled);
    }

    [Fact]
    public async Task ListWorkflowInstances_uses_provider_count_contracts_for_summary_counts()
    {
        await _workflowStore.SaveAsync(Workflow("wf-1", WorkflowExecutionStatus.Running, "definition-1"));
        await _activityStore.SaveAsync(Activity("wf-1", "activity-1", ActivityExecutionStatus.Running));
        await _incidentStore.TryAddAsync(Incident("wf-1", "incident-1"));
        var activities = new CountOnlyActivityExecutionStateStore(_activityStore);
        var incidents = new CountOnlyIncidentStateStore(_incidentStore);
        var handler = new WorkflowInstanceListService(_workflowStore, activities, incidents, AllowAll);

        var result = await handler.ListAsync(new ListWorkflowInstances(null, null, null, 10), CancellationToken.None);

        Assert.Equal(1, Assert.Single(result.Items).ActivityCount);
        Assert.Equal(1, Assert.Single(result.Items).IncidentCount);
        Assert.True(activities.CountCalled);
        Assert.True(incidents.CountCalled);
    }

    [Fact]
    public async Task ListWorkflowInstances_reads_a_page_without_overlapping_operations_on_the_shared_context()
    {
        // Relational stores resolve one scoped persistence context per request, so overlapping reads fail. Two rows
        // are seeded because the first page after a fresh start can serialize by accident and hide the overlap.
        await _workflowStore.SaveAsync(Workflow("wf-1", WorkflowExecutionStatus.Completed, "definition-1", updatedAt: Now(-1)));
        await _workflowStore.SaveAsync(Workflow("wf-2", WorkflowExecutionStatus.Completed, "definition-1", updatedAt: Now(-2)));
        await _activityStore.SaveAsync(Activity("wf-1", "activity-1", ActivityExecutionStatus.Completed));
        await _incidentStore.TryAddAsync(Incident("wf-2", "incident-1"));
        var gate = new SingleOperationGate();
        var handler = new WorkflowInstanceListService(
            _workflowStore,
            new SharedContextActivityExecutionStateStore(_activityStore, gate),
            new SharedContextIncidentStateStore(_incidentStore, gate),
            AllowAll);

        var result = await handler.ListAsync(new ListWorkflowInstances(null, null, null, 10), CancellationToken.None);

        Assert.Equal(["wf-1", "wf-2"], result.Items.Select(x => x.WorkflowExecutionId));
        Assert.Equal(1, result.Items.First().ActivityCount);
        Assert.Equal(1, result.Items.Last().IncidentCount);
    }

    [Fact]
    public async Task ListWorkflowInstances_FiltersAndProjectsDurableRunKind()
    {
        await _workflowStore.SaveAsync(Workflow("wf-test", WorkflowExecutionStatus.Completed, "definition-1", runKind: WorkflowRunKind.TestRun));
        await _workflowStore.SaveAsync(Workflow("wf-published", WorkflowExecutionStatus.Completed, "definition-1", runKind: WorkflowRunKind.PublishedRun));
        await _workflowStore.SaveAsync(Workflow("wf-weaver", WorkflowExecutionStatus.Completed, "definition-1", runKind: WorkflowRunKind.BackgroundWeaverRun));
        await _workflowStore.SaveAsync(Workflow("wf-legacy", WorkflowExecutionStatus.Completed, "definition-1"));
        var handler = NewListInstanceHandler();

        var result = await handler.ListAsync(new ListWorkflowInstances(null, null, null, 10, RunKind: "TestRun"), CancellationToken.None);
        var publishedResult = await handler.ListAsync(new ListWorkflowInstances(null, null, null, 10, RunKind: "PublishedRun"), CancellationToken.None);
        var weaverResult = await handler.ListAsync(new ListWorkflowInstances(null, null, null, 10, RunKind: "BackgroundWeaverRun"), CancellationToken.None);
        var legacyResult = await handler.ListAsync(new ListWorkflowInstances(null, null, null, 10, RunKind: "Unknown"), CancellationToken.None);

        var summary = Assert.Single(result.Items);
        Assert.Equal("wf-test", summary.WorkflowExecutionId);
        Assert.Equal("TestRun", summary.RunKind);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("wf-published", Assert.Single(publishedResult.Items).WorkflowExecutionId);
        Assert.Equal("wf-weaver", Assert.Single(weaverResult.Items).WorkflowExecutionId);
        Assert.Equal("wf-legacy", Assert.Single(legacyResult.Items).WorkflowExecutionId);
    }

    [Fact]
    public async Task ListWorkflowInstances_NavigatesStableCursorPagesAcrossEqualTimestamps()
    {
        var timestamp = Now(-1);
        foreach (var id in new[] { "wf-d", "wf-b", "wf-a", "wf-c" })
            await _workflowStore.SaveAsync(Workflow(id, WorkflowExecutionStatus.Completed, "definition-1", updatedAt: timestamp));
        var handler = NewListInstanceHandler();

        var first = await handler.ListAsync(new ListWorkflowInstances(null, null, null, 2), CancellationToken.None);
        var second = await handler.ListAsync(new ListWorkflowInstances(null, null, null, 2, first.NextCursor), CancellationToken.None);

        Assert.Equal(["wf-a", "wf-b"], first.Items.Select(x => x.WorkflowExecutionId));
        Assert.True(first.HasNext);
        Assert.Equal(["wf-c", "wf-d"], second.Items.Select(x => x.WorkflowExecutionId));
        Assert.False(second.HasNext);
        Assert.Null(second.NextCursor);
        Assert.Equal(4, second.TotalCount);
    }

    [Fact]
    public async Task ListWorkflowInstances_preserves_omitted_and_changed_take_cursor_semantics()
    {
        await SeedWorkflowInstancesAsync(40);
        var handler = NewListInstanceHandler();

        var first = await handler.ListAsync(new ListWorkflowInstances(null, null, null, 2), CancellationToken.None);
        var nextWithOmittedTake = await handler.ListAsync(new ListWorkflowInstances(null, null, null, null, first.NextCursor), CancellationToken.None);

        Assert.Equal(25, nextWithOmittedTake.Count);
        Assert.Equal("wf-002", nextWithOmittedTake.Items.First().WorkflowExecutionId);
    }

    [Fact]
    public async Task ListWorkflowInstances_rejects_a_cursor_reused_with_different_filters()
    {
        await SeedWorkflowInstancesAsync(3);
        var handler = NewListInstanceHandler();
        var first = await handler.ListAsync(new ListWorkflowInstances(null, "definition-1", null, 1), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => handler.ListAsync(
            new ListWorkflowInstances(null, "definition-2", null, 1, first.NextCursor),
            CancellationToken.None));

        Assert.Equal("cursor", exception.ParamName);
    }

    [Fact]
    public async Task ListWorkflowInstances_ClampsPageSizeAtBothBounds()
    {
        await SeedWorkflowInstancesAsync(105);
        var handler = NewListInstanceHandler();

        var maximum = await handler.ListAsync(new ListWorkflowInstances(null, null, null, 500), CancellationToken.None);
        var minimum = await handler.ListAsync(new ListWorkflowInstances(null, null, null, 0), CancellationToken.None);

        Assert.Equal(100, maximum.Count);
        Assert.True(maximum.HasNext);
        Assert.Single(minimum.Items);
    }

    [Theory]
    [InlineData(null, 25)]
    [InlineData(25, 25)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(500, 100)]
    public async Task Paged_route_preserves_its_25_default_and_100_maximum(int? take, int expected)
    {
        await SeedWorkflowInstancesAsync(501);
        var handler = NewListInstanceHandler();

        var result = await handler.ListAsync(
            new ListWorkflowInstances(null, null, null, take),
            CancellationToken.None);

        Assert.Equal(expected, result.Count);
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(25, 25)]
    [InlineData(100, 100)]
    [InlineData(101, 101)]
    [InlineData(500, 500)]
    public async Task Legacy_array_route_preserves_its_100_default_and_500_maximum(int? take, int expected)
    {
        await SeedWorkflowInstancesAsync(501);
        var handler = NewListInstanceHandler();

        var result = await handler.ListAsync(
            new ListWorkflowInstances(null, null, null, take).ForLegacyArray(),
            CancellationToken.None);

        Assert.Equal(expected, result.Count);
    }

    [Fact]
    public async Task ListWorkflowInstances_FiltersByOperationalIdentifiersAndTimeRange()
    {
        await _workflowStore.SaveAsync(Workflow("match", WorkflowExecutionStatus.Running, "definition-1", "correlation-1", Now(-5)));
        await _workflowStore.SaveAsync(Workflow("wrong-execution", WorkflowExecutionStatus.Running, "definition-1", "correlation-1", Now(-5)));
        await _workflowStore.SaveAsync(Workflow("wrong-definition", WorkflowExecutionStatus.Running, "definition-2", "correlation-1", Now(-5)));
        var handler = NewListInstanceHandler();

        var result = await handler.ListAsync(new ListWorkflowInstances(
            "Running",
            "definition-1",
            "correlation-1",
            10,
            WorkflowExecutionId: "match",
            ArtifactId: "artifact-definition-1",
            From: Now(-6),
            To: Now(-4)), CancellationToken.None);

        Assert.Equal("match", Assert.Single(result.Items).WorkflowExecutionId);
    }

    [Fact]
    public async Task ListWorkflowInstances_FiltersByPinnedSourceDefinitionWhenContentWasDeduplicated()
    {
        await _workflowStore.SaveAsync(Workflow(
            "published-source",
            WorkflowExecutionStatus.Running,
            "content-origin-definition",
            sourceDefinitionId: "published-definition"));
        var handler = NewListInstanceHandler();

        var result = await handler.ListAsync(
            new ListWorkflowInstances(null, "published-definition", null, 10),
            CancellationToken.None);

        Assert.Equal("published-source", Assert.Single(result.Items).WorkflowExecutionId);
    }

    [Fact]
    public async Task ListWorkflowInstances_RejectsMalformedCursor()
    {
        var handler = NewListInstanceHandler();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.ListAsync(new ListWorkflowInstances(null, null, null, 10, "not-a-cursor"), CancellationToken.None));

        Assert.Equal("cursor", exception.ParamName);
    }

    [Fact]
    public async Task ListWorkflowInstances_RejectsUnknownRunKind()
    {
        var handler = NewListInstanceHandler();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.ListAsync(new ListWorkflowInstances(null, null, null, 10, RunKind: "TestRnu"), CancellationToken.None));

        Assert.Equal("RunKind", exception.ParamName);
        Assert.Contains("TestRnu", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("active", 2, "wf-blocking", "wf-open")]
    [InlineData("blocking", 1, "wf-blocking", null)]
    [InlineData("none", 3, "wf-healthy", "wf-resolved")]
    public async Task ListWorkflowInstances_FiltersCurrentHealthBeforeCountsAndCursorPaging(
        string health, int total, string firstId, string? secondId)
    {
        var statuses = new (string Id, IncidentStatus? Status)[]
        {
            ("wf-blocking", IncidentStatus.Blocking), ("wf-open", IncidentStatus.Open),
            ("wf-healthy", null), ("wf-resolved", IncidentStatus.Resolved), ("wf-suppressed", IncidentStatus.Suppressed)
        };
        foreach (var (id, status) in statuses)
        {
            await _workflowStore.SaveAsync(Workflow(id, WorkflowExecutionStatus.Running, "definition-1", updatedAt: Now(-1)));
            if (status is { } value)
                await _incidentStore.SaveAsync(HealthIncident(id, value));
        }
        var handler = NewListInstanceHandler();
        var request = new ListWorkflowInstances("Running", "definition-1", null, 1) { IncidentHealth = health };
        var first = await handler.ListAsync(request, CancellationToken.None);
        var item = Assert.Single(first.Items);
        Assert.Equal(total, first.TotalCount);
        Assert.Equal(firstId, item.WorkflowExecutionId);
        Assert.Equal("Running", item.Status);
        Assert.Equal(health == "none" ? 0 : 1, item.ActiveIncidentCount);
        Assert.Equal(health == "blocking" || firstId == "wf-blocking" ? 1 : 0, item.BlockingIncidentCount);
        Assert.Equal(total > 1, first.HasNext);
        if (secondId is not null)
        {
            var second = await handler.ListAsync(request with { Cursor = first.NextCursor }, CancellationToken.None);
            var secondItem = Assert.Single(second.Items);
            Assert.Equal(secondId, secondItem.WorkflowExecutionId);
            Assert.Equal(total, second.TotalCount);
            if (health == "none")
            {
                Assert.Equal(1, secondItem.IncidentCount);
                Assert.Equal(0, secondItem.ActiveIncidentCount);
                var third = await handler.ListAsync(request with { Cursor = second.NextCursor }, CancellationToken.None);
                var thirdItem = Assert.Single(third.Items);
                Assert.Equal("wf-suppressed", thirdItem.WorkflowExecutionId);
                Assert.Equal(1, thirdItem.IncidentCount);
                Assert.Equal(0, thirdItem.ActiveIncidentCount);
                Assert.False(third.HasNext);
            }
        }
    }

    [Fact]
    public async Task ListWorkflowInstances_DispatchesHealthPagingToCapableProviderAndPreservesCursor()
    {
        await _workflowStore.SaveAsync(Workflow("wf-1", WorkflowExecutionStatus.Running, "definition-1"));
        await _incidentStore.SaveAsync(HealthIncident("wf-1", IncidentStatus.Blocking));
        var store = new HealthQueryWorkflowExecutionStateStore(_workflowStore, _incidentStore);
        var handler = NewListInstanceHandler(store);
        var request = new ListWorkflowInstances(null, "definition-1", null, 1) { IncidentHealth = "blocking" };
        var first = await handler.ListAsync(request, CancellationToken.None);
        Assert.Equal(42, first.TotalCount);
        Assert.Equal("health.blocking.provider-next", first.NextCursor);
        Assert.Equal(IncidentHealth.Blocking, store.LastHealth);
        Assert.Equal("definition-1", store.LastQuery!.DefinitionId);
        Assert.False(store.QueryPageCalled);
        await handler.ListAsync(request with { Cursor = first.NextCursor }, CancellationToken.None);
        Assert.Equal("provider-next", store.LastQuery.Cursor);
    }

    [Theory]
    [InlineData(false, "secret-correlation")]
    [InlineData(true, null)]
    public async Task ListWorkflowInstances_HealthProviderCannotBypassInspectionAuthorization(bool constrainedScope, string? correlation)
    {
        await _workflowStore.SaveAsync(Workflow("allowed", WorkflowExecutionStatus.Running, "definition-1", "secret-correlation"));
        await _incidentStore.SaveAsync(HealthIncident("allowed", IncidentStatus.Blocking));
        var store = new HealthQueryWorkflowExecutionStateStore(_workflowStore, _incidentStore);
        var handler = new WorkflowInstanceListService(store, _activityStore, _incidentStore,
            constrainedScope ? new RestrictedInspectionContext() : AllowAll);
        var result = await handler.ListAsync(new ListWorkflowInstances(null, null, correlation, 1) { IncidentHealth = "blocking" }, CancellationToken.None);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(IncidentHealth.Blocking, store.LastHealth);
        Assert.Equal(100, store.LastQuery!.PageSize);
    }

    [Theory]
    [InlineData("active", false, "wf-blocking,wf-open")]
    [InlineData("blocking", false, "wf-blocking")]
    [InlineData("none", false, "wf-healthy,wf-resolved,wf-suppressed")]
    [InlineData("active", true, "wf-blocking,wf-open")]
    [InlineData("blocking", true, "wf-blocking")]
    [InlineData("none", true, "wf-healthy,wf-resolved,wf-suppressed")]
    public async Task ListWorkflowInstances_RejectsNativeHealthPagingWhenSelectedIncidentStoreDoesNotMatch(
        string health,
        bool constrainedAuthorization,
        string expectedIds)
    {
        var selectedHealth = new (string Id, IncidentStatus? Status)[]
        {
            ("wf-blocking", IncidentStatus.Blocking),
            ("wf-open", IncidentStatus.Open),
            ("wf-healthy", null),
            ("wf-resolved", IncidentStatus.Resolved),
            ("wf-suppressed", IncidentStatus.Suppressed)
        };
        foreach (var (id, status) in selectedHealth)
        {
            await _workflowStore.SaveAsync(Workflow(id, WorkflowExecutionStatus.Running, "definition-1", updatedAt: Now(-1)));
            if (status is { } value)
                await _incidentStore.SaveAsync(HealthIncident(id, value));
        }

        // This native query represents a different incident source (for example, an unused EF table).
        // The selected in-memory incident store remains authoritative, so the service must use its fallback.
        var store = new HealthQueryWorkflowExecutionStateStore(_workflowStore, new InMemoryIncidentStateStore());
        IActivityInspectionContextAsync authorization = constrainedAuthorization
            ? new RestrictedInspectionContext("all-tenants")
            : AllowAll;
        var handler = new WorkflowInstanceListService(store, _activityStore, _incidentStore, authorization);

        var result = await handler.ListAsync(new ListWorkflowInstances(null, null, null, 10) { IncidentHealth = health }, CancellationToken.None);

        Assert.Equal(expectedIds.Split(','), result.Items.Select(item => item.WorkflowExecutionId).Order(StringComparer.Ordinal));
        Assert.Equal(expectedIds.Split(',').Length, result.TotalCount);
        Assert.Null(store.LastHealth);
        Assert.True(store.QueryPageCalled);
    }

    [Fact]
    public async Task ListWorkflowInstances_HealthFallbackTraversesBoundedFilteredPagesBeyondFirstProviderPage()
    {
        for (var index = 0; index < 150; index++)
        {
            var id = $"wf-{index:D3}";
            await _workflowStore.SaveAsync(Workflow(id, WorkflowExecutionStatus.Running, "definition-1", updatedAt: Now(-1)));
            if (index is 1 or 101 or 149)
                await _incidentStore.SaveAsync(HealthIncident(id, IncidentStatus.Blocking));
        }
        var store = new BoundedQueryOnlyWorkflowExecutionStateStore(_workflowStore);
        var handler = NewListInstanceHandler(store);
        var request = new ListWorkflowInstances(null, "definition-1", null, 1) { IncidentHealth = "blocking" };
        var first = await handler.ListAsync(request, CancellationToken.None);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal("wf-001", Assert.Single(first.Items).WorkflowExecutionId);
        var second = await handler.ListAsync(request with { Cursor = first.NextCursor }, CancellationToken.None);
        Assert.Equal(3, second.TotalCount);
        Assert.Equal("wf-101", Assert.Single(second.Items).WorkflowExecutionId);
        Assert.True(store.PageQueryCount > 1);
        Assert.InRange(store.MaxPageSize, 1, 100);
    }

    [Fact]
    public async Task ListWorkflowInstances_HealthCursorCannotBeReusedWithDifferentHealth()
    {
        foreach (var id in new[] { "wf-a", "wf-b" })
        {
            await _workflowStore.SaveAsync(Workflow(id, WorkflowExecutionStatus.Running, "definition-1"));
            await _incidentStore.SaveAsync(HealthIncident(id, IncidentStatus.Blocking));
        }
        var handler = NewListInstanceHandler();
        var request = new ListWorkflowInstances(null, null, null, 1) { IncidentHealth = "active" };
        var first = await handler.ListAsync(request, CancellationToken.None);
        foreach (var health in new string?[] { "blocking", "none", null })
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => handler.ListAsync(
                request with { IncidentHealth = health, Cursor = first.NextCursor }, CancellationToken.None));
            Assert.Equal("cursor", exception.ParamName);
        }
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData("secret-correlation", 1)]
    public async Task ListWorkflowInstances_HealthCountsAndPagesExcludeUnauthorizedRuns(string? correlation, int expected)
    {
        foreach (var id in new[] { "allowed", "structure-only", "foreign" })
        {
            await _workflowStore.SaveAsync(Workflow(id, WorkflowExecutionStatus.Running, "definition-1", "secret-correlation"));
            await _incidentStore.SaveAsync(HealthIncident(id, IncidentStatus.Blocking));
        }
        var handler = new WorkflowInstanceListService(_workflowStore, _activityStore, _incidentStore, new RestrictedInspectionContext());
        var request = new ListWorkflowInstances(null, null, correlation, 1) { IncidentHealth = "blocking" };
        var first = await handler.ListAsync(request, CancellationToken.None);
        Assert.Equal(expected, first.TotalCount);
        Assert.Equal("allowed", Assert.Single(first.Items).WorkflowExecutionId);
        Assert.Equal(expected > 1, first.HasNext);
        if (first.HasNext)
        {
            var second = await handler.ListAsync(request with { Cursor = first.NextCursor }, CancellationToken.None);
            var item = Assert.Single(second.Items);
            Assert.Equal("structure-only", item.WorkflowExecutionId);
            Assert.Null(item.CorrelationId);
            Assert.False(second.HasNext);
        }
    }

    [Fact]
    public async Task ListWorkflowInstances_AllTenantLabelDoesNotBypassStructureAuthorization()
    {
        await _workflowStore.SaveAsync(Workflow("foreign", WorkflowExecutionStatus.Running, "definition-1"));
        await _incidentStore.SaveAsync(HealthIncident("foreign", IncidentStatus.Blocking));
        var store = new HealthQueryWorkflowExecutionStateStore(_workflowStore, _incidentStore);
        var handler = new WorkflowInstanceListService(store, _activityStore, _incidentStore, new RestrictedInspectionContext("all-tenants"));
        foreach (var health in new string?[] { null, "blocking" })
        {
            var result = await handler.ListAsync(new ListWorkflowInstances(null, null, null, 1) { IncidentHealth = health }, CancellationToken.None);
            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
            Assert.Null(result.NextCursor);
        }
    }

    private sealed class RestrictedInspectionContext(string tenantScope = "tenant-a") : IActivityInspectionContextAsync
    {
        public string TenantScope => tenantScope;
        public string AuditSubject => "operator";
        public string RequestCorrelationId => "request";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("test");
        public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(state.WorkflowExecutionId != "foreign");
        public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(state.WorkflowExecutionId == "allowed");
        public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) =>
            CanInspectSensitiveValuesAsync(state, cancellationToken);
    }

    [Fact]
    public async Task ListWorkflowInstances_RejectsUnknownIncidentHealth()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => NewListInstanceHandler()
            .ListAsync(new ListWorkflowInstances(null, null, null, 10) { IncidentHealth = "faulted" }, CancellationToken.None));
        Assert.Equal("IncidentHealth", exception.ParamName);
    }

    private static IncidentState HealthIncident(string workflowExecutionId, IncidentStatus status)
    {
        var terminal = status is IncidentStatus.Resolved or IncidentStatus.Suppressed;
        return new IncidentState("incident-" + workflowExecutionId, workflowExecutionId, null, null, IncidentSeverity.Error,
            status, terminal ? new IncidentResolutionOutcome("Resolved", Now(-1), null, "test", null) : null,
            "test", "safe test incident", Now(-2), terminal ? Now(-1) : null);
    }

    [Fact]
    public async Task GetWorkflowInstance_ReturnsActivitiesAndIncidents()
    {
        await _workflowStore.SaveAsync(Workflow("wf-1", WorkflowExecutionStatus.Faulted, "definition-1"));
        await _activityStore.SaveAsync(Activity("wf-1", "activity-2", ActivityExecutionStatus.Faulted, scheduledAt: Now(-1)));
        await _activityStore.SaveAsync(Activity("wf-1", "activity-1", ActivityExecutionStatus.Completed, scheduledAt: Now(-2)));
        await _inspectionStore.SaveAsync(Inspection(Activity("wf-1", "activity-2", ActivityExecutionStatus.Faulted, scheduledAt: Now(-1))));
        await _inspectionStore.SaveAsync(Inspection(Activity("wf-1", "activity-1", ActivityExecutionStatus.Completed, scheduledAt: Now(-2))));
        await _incidentStore.TryAddAsync(Incident("wf-1", "incident-1"));
        var handler = NewGetInstanceHandler();

        var result = await handler.GetAsync(new GetWorkflowInstance("wf-1"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("wf-1", result!.Instance.WorkflowExecutionId);
        Assert.Equal("Faulted", result!.Instance.Status);
        Assert.Equal(["activity-1", "activity-2"], result!.Activities.Select(activity => activity.ActivityExecutionId));
        Assert.Equal("incident-1", Assert.Single(result!.Incidents).IncidentId);
    }

    [Fact]
    public async Task GetWorkflowInstance_OnImmediateHost_ReportsActivityLevelInspection()
    {
        await _workflowStore.SaveAsync(Workflow("wf-1", WorkflowExecutionStatus.Completed, "definition-1"));
        var handler = NewGetInstanceHandler(ImmediateCadenceInspector());

        var result = await handler.GetAsync(new GetWorkflowInstance("wf-1"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Immediate", result!.CheckpointCadence);
        Assert.Null(result!.MaxSegmentCheckpoints);
        Assert.Equal("activity-level", result!.InspectionGranularity);
    }

    [Fact]
    public async Task GetWorkflowInstance_OnCoalescedHost_ReportsBoundaryLevelInspectionWithTheCap()
    {
        await _workflowStore.SaveAsync(Workflow("wf-1", WorkflowExecutionStatus.Completed, "definition-1"));
        var handler = NewGetInstanceHandler(CoalescedCadenceInspector(32));

        var result = await handler.GetAsync(new GetWorkflowInstance("wf-1"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Coalesced", result!.CheckpointCadence);
        Assert.Equal(32, result!.MaxSegmentCheckpoints);
        Assert.Equal("boundary-level", result!.InspectionGranularity);
    }

    [Fact]
    public async Task GetWorkflowInstance_PrefersThePerRunStamp_OverAReconfiguredCoalescedHost()
    {
        // ADR 0032 R5 per-run stamp: the run executed under Immediate cadence; the host was later reconfigured to
        // Coalesced. The instance must report the cadence it actually ran under, not the host's current setting.
        await _workflowStore.SaveAsync(Workflow(
            "wf-1",
            WorkflowExecutionStatus.Completed,
            "definition-1",
            systemMetadata: new Dictionary<string, string>
            {
                [Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.CheckpointCadence] = "Immediate"
            }));
        var handler = NewGetInstanceHandler(CoalescedCadenceInspector(32));

        var result = await handler.GetAsync(new GetWorkflowInstance("wf-1"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Immediate", result!.CheckpointCadence);
        Assert.Null(result!.MaxSegmentCheckpoints);
        Assert.Equal("activity-level", result!.InspectionGranularity);
    }

    [Fact]
    public async Task GetWorkflowInstance_ReportsAStampedCoalescedRun_EvenOnAnImmediateHost()
    {
        await _workflowStore.SaveAsync(Workflow(
            "wf-1",
            WorkflowExecutionStatus.Completed,
            "definition-1",
            systemMetadata: new Dictionary<string, string>
            {
                [Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.CheckpointCadence] = "Coalesced",
                [Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.CheckpointMaxSegmentCheckpoints] = "8"
            }));
        var handler = NewGetInstanceHandler(ImmediateCadenceInspector());

        var result = await handler.GetAsync(new GetWorkflowInstance("wf-1"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Coalesced", result!.CheckpointCadence);
        Assert.Equal(8, result!.MaxSegmentCheckpoints);
        Assert.Equal("boundary-level", result!.InspectionGranularity);
    }

    [Fact]
    public async Task ListAndDetailReturnTheSameRunKindAndKeepLegacyStatesUnknown()
    {
        await _workflowStore.SaveAsync(Workflow("wf-published", WorkflowExecutionStatus.Running, "definition-1", runKind: WorkflowRunKind.PublishedRun));
        await _workflowStore.SaveAsync(Workflow("wf-legacy", WorkflowExecutionStatus.Completed, "definition-1"));
        var listHandler = NewListInstanceHandler();

        var list = await listHandler.ListAsync(new ListWorkflowInstances(null, null, null, 10), CancellationToken.None);
        var detail = await NewGetInstanceHandler().GetAsync(new GetWorkflowInstance("wf-published"), CancellationToken.None);

        Assert.Equal("PublishedRun", Assert.Single(list.Items, item => item.WorkflowExecutionId == "wf-published").RunKind);
        Assert.Equal("PublishedRun", detail!.Instance.RunKind);
        Assert.Equal("Unknown", Assert.Single(list.Items, item => item.WorkflowExecutionId == "wf-legacy").RunKind);
    }

    [Fact]
    public async Task GetWorkflowInstance_ReturnsNullForMissingInstance()
    {
        var handler = NewGetInstanceHandler();

        var result = await handler.GetAsync(new GetWorkflowInstance("missing"), CancellationToken.None);

        Assert.Null(result);
    }

    private static WorkflowExecutionState Workflow(
        string id,
        WorkflowExecutionStatus status,
        string definitionId,
        string? correlationId = null,
        DateTimeOffset? updatedAt = null,
        WorkflowRunKind runKind = WorkflowRunKind.Unknown,
        string? sourceDefinitionId = null,
        IReadOnlyDictionary<string, string>? systemMetadata = null) =>
        new(
            WorkflowExecutionId: id,
            PinnedExecutable: new WorkflowExecutableIdentity(
                ArtifactId: $"artifact-{definitionId}",
                DefinitionId: definitionId,
                DefinitionVersionId: $"version-{definitionId}",
                ArtifactVersion: "1.0.0",
                ArtifactHash: "sha256:test"),
            Status: status,
            SubStatus: null,
            CreatedAt: Now(-30),
            StartedAt: Now(-29),
            UpdatedAt: updatedAt,
            CompletedAt: status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Faulted ? Now(-1) : null,
            CorrelationId: correlationId,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: systemMetadata ?? new Dictionary<string, string>())
        {
            RunKind = runKind,
            PinnedSource = sourceDefinitionId is null
                ? null
                : new WorkflowExecutableSourceProvenance(
                    "reference-1",
                    "WorkflowDefinitionVersion",
                    "published-version",
                    "2.0.0",
                    sourceDefinitionId,
                    "published-version",
                    "2.0.0",
                    "publication-1",
                    "slot-default")
        };

    private class BoundedQueryOnlyWorkflowExecutionStateStore(IWorkflowExecutionStateStore inner) : IWorkflowExecutionStateStore
    {
        public bool QueryPageCalled { get; private set; }
        public int PageQueryCount { get; private set; }
        public int MaxPageSize { get; private set; }

        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default)
        {
            QueryPageCalled = true;
            PageQueryCount++;
            MaxPageSize = Math.Max(MaxPageSize, query.PageSize);
            return inner.QueryPageAsync(query, cancellationToken);
        }

        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The list handler must not materialize the complete state store.");

        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(state, cancellationToken);

        public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) =>
            inner.ListPinnedExecutableArtifactIdsAsync(cancellationToken);

        public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(workflowExecutionId, cancellationToken);
    }

    private sealed class HealthQueryWorkflowExecutionStateStore(
        IWorkflowExecutionStateStore inner,
        IIncidentStateStore supportedIncidentStore)
        : BoundedQueryOnlyWorkflowExecutionStateStore(inner), IWorkflowHealthQuery
    {
        public IncidentHealth? LastHealth { get; private set; }
        public WorkflowExecutionStatePageQuery? LastQuery { get; private set; }

        public bool SupportsIncidentStore(IIncidentStateStore selectedIncidentStore) =>
            ReferenceEquals(selectedIncidentStore, supportedIncidentStore);

        public async ValueTask<WorkflowExecutionStatePage> QueryHealthPageAsync(WorkflowExecutionStatePageQuery query, IncidentHealth health, CancellationToken cancellationToken = default)
        {
            LastHealth = health;
            LastQuery = query;
            var state = await inner.FindAsync("wf-1", cancellationToken);
            return state is not null
                ? new WorkflowExecutionStatePage([state], query.Cursor is null ? "provider-next" : null, query.Cursor is null, 42)
                : await inner.QueryPageAsync(query, cancellationToken);
        }
    }

    private sealed class CountOnlyActivityExecutionStateStore(IActivityExecutionStateStore inner) : IActivityExecutionStateStore
    {
        public bool CountCalled { get; private set; }

        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(state, cancellationToken);

        public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);

        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Instance summaries must use the provider count contract.");

        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListByParentPageAsync(query, cancellationToken);

        public async ValueTask<long> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            CountCalled = true;
            return await inner.CountAsync(workflowExecutionId, cancellationToken);
        }
    }

    private sealed class CountOnlyIncidentStateStore(IIncidentStateStore inner) : IIncidentStateStore
    {
        public bool CountCalled { get; private set; }

        public ValueTask<bool> TryAddAsync(IncidentState state, CancellationToken cancellationToken = default) =>
            inner.TryAddAsync(state, cancellationToken);

        public ValueTask<IncidentState> SaveAsync(IncidentState state, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(state, cancellationToken);

        public ValueTask<IncidentState?> FindAsync(string workflowExecutionId, string incidentId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, incidentId, cancellationToken);

        public ValueTask<IReadOnlyCollection<IncidentState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Instance summaries must use the provider count contract.");

        public ValueTask<IReadOnlyCollection<IncidentState>> ListBlockingAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.ListBlockingAsync(workflowExecutionId, cancellationToken);

        public async ValueTask<IncidentHealthCounts> CountHealthAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            CountCalled = true;
            return await inner.CountHealthAsync(workflowExecutionId, cancellationToken);
        }

        public ValueTask<int> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.CountAsync(workflowExecutionId, cancellationToken);
    }

    /// <summary>
    /// Stands in for the exclusivity one shared scoped persistence context demands. Entering a second operation
    /// while another is in flight is exactly what relational providers reject, so it is rejected here too. The
    /// yield gives an overlapping caller the chance to enter, which a completed read would otherwise hide.
    /// </summary>
    private sealed class SingleOperationGate
    {
        private int _inFlight;

        public async ValueTask<T> RunAsync<T>(Func<ValueTask<T>> operation)
        {
            if (Interlocked.Exchange(ref _inFlight, 1) == 1)
                throw new InvalidOperationException(
                    "A second operation was started on this context instance before a previous operation completed.");

            try
            {
                await Task.Yield();
                return await operation();
            }
            finally
            {
                Interlocked.Exchange(ref _inFlight, 0);
            }
        }
    }

    private sealed class SharedContextActivityExecutionStateStore(IActivityExecutionStateStore inner, SingleOperationGate gate)
        : IActivityExecutionStateStore
    {
        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default) =>
            gate.RunAsync(() => inner.SaveAsync(state, cancellationToken));

        public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) =>
            gate.RunAsync(() => inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken));

        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query, CancellationToken cancellationToken = default) =>
            gate.RunAsync(() => inner.ListPageAsync(query, cancellationToken));

        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query, CancellationToken cancellationToken = default) =>
            gate.RunAsync(() => inner.ListByParentPageAsync(query, cancellationToken));

        public ValueTask<long> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            gate.RunAsync(() => inner.CountAsync(workflowExecutionId, cancellationToken));
    }

    private sealed class SharedContextIncidentStateStore(IIncidentStateStore inner, SingleOperationGate gate) : IIncidentStateStore
    {
        public ValueTask<bool> TryAddAsync(IncidentState state, CancellationToken cancellationToken = default) =>
            gate.RunAsync(() => inner.TryAddAsync(state, cancellationToken));

        public ValueTask<IncidentState> SaveAsync(IncidentState state, CancellationToken cancellationToken = default) =>
            gate.RunAsync(() => inner.SaveAsync(state, cancellationToken));

        public ValueTask<IncidentState?> FindAsync(string workflowExecutionId, string incidentId, CancellationToken cancellationToken = default) =>
            gate.RunAsync(() => inner.FindAsync(workflowExecutionId, incidentId, cancellationToken));

        public ValueTask<IReadOnlyCollection<IncidentState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            gate.RunAsync(() => inner.ListAsync(workflowExecutionId, cancellationToken));

        public ValueTask<IReadOnlyCollection<IncidentState>> ListBlockingAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            gate.RunAsync(() => inner.ListBlockingAsync(workflowExecutionId, cancellationToken));

        public ValueTask<int> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            gate.RunAsync(() => inner.CountAsync(workflowExecutionId, cancellationToken));
    }

    private static ActivityExecutionState Activity(
        string workflowExecutionId,
        string activityExecutionId,
        ActivityExecutionStatus status,
        DateTimeOffset? scheduledAt = null) =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: activityExecutionId,
                WorkflowExecutionId: workflowExecutionId,
                ExecutableNodeId: $"node-{activityExecutionId}",
                AuthoredActivityId: $"authored-{activityExecutionId}",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: status,
            SubStatus: null,
            ScheduledAt: scheduledAt ?? Now(-10),
            StartedAt: Now(-9),
            CompletedAt: status == ActivityExecutionStatus.Completed ? Now(-8) : null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: status == ActivityExecutionStatus.Faulted ? 1 : 0,
            AggregateFaultCount: status == ActivityExecutionStatus.Faulted ? 1 : 0,
            Metadata: new Dictionary<string, string>());

    private static IncidentState Incident(string workflowExecutionId, string incidentId) =>
        new(
            incidentId: incidentId,
            workflowExecutionId: workflowExecutionId,
            activityExecutionId: "activity-2",
            executableNodeId: "node-activity-2",
            severity: IncidentSeverity.Error,
            status: IncidentStatus.Blocking,
            resolutionOutcome: null,
            failureType: "TestFailure",
            message: "The activity failed.",
            createdAt: Now(-7),
            resolvedAt: null);

    private static ActivityExecutionInspectionProjection Inspection(ActivityExecutionState state) =>
        ActivityExecutionInspectionProjection.FromState(
            state,
            checkpointId: $"checkpoint-{state.Execution.ActivityExecutionId}",
            committedAt: Now(-1));

    private static DateTimeOffset Now(int minutes) =>
        new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero) + TimeSpan.FromMinutes(minutes);

}
