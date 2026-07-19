using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Entities;
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
            request.Search,
            ParseState(request.State),
            request.ContinuationToken,
            request.FolderId,
            request.Unfiled);
        query.Validate();
        var folderStore = serviceProvider.GetService<IWorkflowFolderStore>();
        var folderDetailsById = new Dictionary<string, WorkflowFolderDetails>(StringComparer.Ordinal);
        if (query.FolderId is not null)
        {
            if (folderStore?.IsAvailable != true)
                throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), query.FolderId);
            var requestedFolder = await folderStore.FindManyWithAncestorsAsync([query.FolderId], cancellationToken);
            if (!requestedFolder.TryGetValue(query.FolderId, out var details))
                throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), query.FolderId);
            folderDetailsById.Add(query.FolderId, details);
        }
        var page = await pageStore.QueryPageAsync(query, cancellationToken);
        var missingFolderIds = page.Items
            .Select(definition => definition.FolderId)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Where(folderId => !folderDetailsById.ContainsKey(folderId))
            .ToArray();
        if (missingFolderIds.Length > 0)
        {
            if (folderStore?.IsAvailable != true)
            {
                throw new InvalidOperationException(
                    "The workflow-definition page contains filed definitions but workflow-folder browsing is unavailable.");
            }
            var resolved = await folderStore.FindManyWithAncestorsAsync(missingFolderIds, cancellationToken);
            foreach (var (folderId, details) in resolved)
                folderDetailsById.Add(folderId, details);
        }
        var items = await WorkflowDefinitionViewMapper.CreatePageAsync(
            page.Items,
            projectionStore,
            folderDetailsById,
            cancellationToken);

        return new WorkflowDefinitionPageView(items, page.NextContinuationToken);
    }

    private static WorkflowDefinitionPageState ParseState(string? state) => state?.ToLowerInvariant() switch
    {
        "deleted" => WorkflowDefinitionPageState.Deleted,
        "all" => WorkflowDefinitionPageState.All,
        _ => WorkflowDefinitionPageState.Active
    };
}
