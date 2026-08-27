using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.List;

[Get("definitions")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(
    IWorkflowDefinitionStore store,
    IWorkflowDefinitionListProjectionStore projectionStore) : ApiEndpoint<ListDefinitions, WorkflowDefinitionListView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsList";
        options.Accepts = ["*/*", "application/json"];
    }

    public override async Task<WorkflowDefinitionListView> HandleAsync(ListDefinitions request, CancellationToken cancellationToken)
    {
        var filter = new WorkflowDefinitionFilter
        {
            Id = request.Id,
            Description = request.Description,
            Name = request.Name,
            SearchTerm = request.SearchTerm,
            TenantAgnostic = request.TenantAgnostic
        };

        var definitions = (await store.ListAsync(filter, cancellationToken))
            .Where(definition => request.State?.ToLowerInvariant() switch
            {
                "deleted" => definition.DeletedAt is not null,
                "all" => true,
                _ => definition.DeletedAt is null
            })
            .ToArray();
        var projections = await projectionStore.ListByDefinitionIdsAsync(
            definitions.Select(definition => definition.Id).ToArray(),
            cancellationToken);
        var projectionsByDefinitionId = projections.ToDictionary(
            projection => projection.WorkflowDefinitionId,
            StringComparer.Ordinal);

        var items = definitions.Select(definition =>
        {
            projectionsByDefinitionId.TryGetValue(definition.Id, out var projection);
            return new WorkflowDefinitionView(
                definition.Id,
                definition.Name,
                definition.Description,
                definition.CreatedAt,
                definition.LastModifiedAt,
                definition.DeletedAt,
                projection?.DraftId,
                projection?.LatestVersionId,
                projection?.LatestVersion,
                projection?.VersionCount ?? 0);
        }).ToArray();
        return new WorkflowDefinitionListView(items);
    }
}
