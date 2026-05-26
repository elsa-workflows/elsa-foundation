using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Extensions;
using Elsa.Workflows.Design.Persistence.Core.Filters;

namespace Elsa.Workflows.Design.Persistence.EFCore.Services;

public sealed class WorkflowDefinitionLookup(IQueries<WorkflowDefinitionVersion> versionQueries, IQueries<WorkflowDefinition> defQueries) : IWorkflowDefinitionLookup
{
    public async Task<IWorkflowDefinitionVersion?> FindLatestVersion(string definitionId, CancellationToken cancellationToken = default)
    {
        var result = await versionQueries.FindLastVersion(definitionId, cancellationToken);
        return result;
    }

    public async Task<IWorkflowDefinition> GetDefinition(string id, CancellationToken cancellationToken = default)
    {
        var result = await defQueries.Get(id, cancellationToken);
        return result;
    }

    public async Task<IWorkflowDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default)
    {
        var result = await versionQueries.Get(versionId, cancellationToken);
        return result;
    }

    public async Task<IEnumerable<IWorkflowDefinition>> ListDefinitions(bool? isSystem = null, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        var filter = new WorkflowDefinitionFilter
        {
            IsSystem = isSystem,
            SearchTerm = searchTerm
        };

        var result = await defQueries.Query(filter, cancellationToken);
        return result;
    }

    public async Task<IEnumerable<WorkflowDefinitionVersionInfo>> ListVersions(string definitionId, CancellationToken cancellationToken = default)
    {
        var filter = new WorkflowDefinitionVersionFilter
        {
            DefinitionId = definitionId
        };

        var result = await versionQueries.Query<WorkflowDefinitionVersionInfo>(
            filter,
            (e) => new(e.Id, e.Version, e.CreatedAt),
            cancellationToken
        );

        return result;
    }
}
