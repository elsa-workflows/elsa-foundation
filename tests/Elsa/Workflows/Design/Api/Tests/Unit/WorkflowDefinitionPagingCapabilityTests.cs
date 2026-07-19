using Elsa.Workflows.Design.Api.Capabilities;
using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class WorkflowDefinitionPagingCapabilityTests
{
    [Fact]
    public async Task Paged_workflow_definition_relation_is_advertised_only_when_a_bounded_store_is_active()
    {
        var absent = await new WorkflowDesignOperationalCapabilitySource().GetCapabilitiesAsync();
        var present = await new WorkflowDesignOperationalCapabilitySource(pageStore: new StubPagedStore()).GetCapabilitiesAsync();

        Assert.DoesNotContain(absent.SelectMany(declaration => declaration.Links), link => link.Rel == "workflow-definitions-page");
        var relation = Assert.Single(present.SelectMany(declaration => declaration.Links), link => link.Rel == "workflow-definitions-page");
        Assert.Equal("design/workflows/definitions/page", relation.Href);
    }

    [Fact]
    public async Task Paged_relation_is_not_advertised_for_a_registered_but_unadmitted_store()
    {
        var capabilities = await new WorkflowDesignOperationalCapabilitySource(pageStore: new StubPagedStore { IsAvailable = false })
            .GetCapabilitiesAsync();

        Assert.DoesNotContain(capabilities.SelectMany(declaration => declaration.Links), link => link.Rel == "workflow-definitions-page");
    }

    [Fact]
    public async Task Paged_handler_composes_lifecycle_search_page_size_and_continuation()
    {
        var pageStore = new StubPagedStore
        {
            Result = new WorkflowDefinitionPage(
                [new WorkflowDefinition { Id = "definition-1", Name = "Orders" }],
                "next-token")
        };
        var handler = new ListWorkflowDefinitionPageRequestHandler(
            new PageStoreServiceProvider(pageStore),
            new EmptyProjectionStore());

        var response = await handler.Handle(
            new ListWorkflowDefinitionPage(999, "previous-token", "orders", "deleted"),
            CancellationToken.None);

        Assert.Equal(100, pageStore.Query!.PageSize);
        Assert.Equal("previous-token", pageStore.Query.ContinuationToken);
        Assert.Equal("orders", pageStore.Query.SearchTerm);
        Assert.Equal(WorkflowDefinitionPageState.Deleted, pageStore.Query.State);
        Assert.Equal("next-token", response.NextContinuationToken);
        Assert.Equal("definition-1", Assert.Single(response.Items).Id);
    }

    [Theory]
    [InlineData(null, WorkflowDefinitionPageState.Active)]
    [InlineData("unexpected", WorkflowDefinitionPageState.Active)]
    [InlineData("deleted", WorkflowDefinitionPageState.Deleted)]
    [InlineData("all", WorkflowDefinitionPageState.All)]
    public async Task Paged_handler_parses_lifecycle_state(string? state, WorkflowDefinitionPageState expected)
    {
        var pageStore = new StubPagedStore();
        var handler = new ListWorkflowDefinitionPageRequestHandler(new PageStoreServiceProvider(pageStore), new EmptyProjectionStore());

        await handler.Handle(new ListWorkflowDefinitionPage(null, null, null, state), CancellationToken.None);

        Assert.Equal(expected, pageStore.Query!.State);
    }

    [Fact]
    public async Task Paged_handler_rejects_an_unavailable_runtime_before_processing_the_request()
    {
        var handler = new ListWorkflowDefinitionPageRequestHandler(new EmptyServiceProvider(), new EmptyProjectionStore());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ListWorkflowDefinitionPage(null, null, null, null), CancellationToken.None));
    }

    private sealed class StubPagedStore : IWorkflowDefinitionPageStore
    {
        public bool IsAvailable { get; init; } = true;
        public WorkflowDefinitionPage? Result { get; init; }
        public WorkflowDefinitionPageQuery? Query { get; private set; }

        public Task<WorkflowDefinitionPage> QueryPageAsync(
            WorkflowDefinitionPageQuery query,
            CancellationToken cancellationToken = default)
        {
            Query = query;
            return Task.FromResult(Result ?? new WorkflowDefinitionPage([], null));
        }
    }

    private sealed class PageStoreServiceProvider(IWorkflowDefinitionPageStore pageStore) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IWorkflowDefinitionPageStore) ? pageStore : null;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class EmptyProjectionStore : IWorkflowDefinitionListProjectionStore
    {
        public Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionListProjection>>([]);
    }
}
