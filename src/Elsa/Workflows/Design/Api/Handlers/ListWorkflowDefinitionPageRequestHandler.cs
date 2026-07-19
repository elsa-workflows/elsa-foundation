using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class ListWorkflowDefinitionPageRequestHandler(
    IServiceProvider serviceProvider,
    IWorkflowDefinitionListProjectionStore projectionStore)
    : IRequestHandler<ListWorkflowDefinitionPage, WorkflowDefinitionPageView>
{
    private const int DefaultPageSize = 25;
    private const int MaximumPageSize = 100;

    public async Task<WorkflowDefinitionPageView> Handle(
        ListWorkflowDefinitionPage request,
        CancellationToken cancellationToken)
    {
        var pageStore = serviceProvider.GetService<IWorkflowDefinitionPageStore>() ?? throw new InvalidOperationException(
            "Workflow-definition paging is unavailable because the active persistence provider has not admitted its bounded browse route.");
        var query = new WorkflowDefinitionPageQuery(
            Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaximumPageSize),
            request.SearchTerm,
            ParseState(request.State),
            request.ContinuationToken);
        var page = await pageStore.QueryPageAsync(query, cancellationToken);
        var items = await WorkflowDefinitionViewMapper.CreateAsync(page.Items, projectionStore, cancellationToken);

        return new WorkflowDefinitionPageView(items, page.NextContinuationToken);
    }

    private static WorkflowDefinitionPageState ParseState(string? state) => state?.ToLowerInvariant() switch
    {
        "deleted" => WorkflowDefinitionPageState.Deleted,
        "all" => WorkflowDefinitionPageState.All,
        _ => WorkflowDefinitionPageState.Active
    };
}
