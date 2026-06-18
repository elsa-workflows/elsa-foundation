using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Persistence.EFCore.Services;

public sealed class WorkflowDefinitionLookup(IWorkflowDefinitionVersionStore versionStore, IWorkflowDefinitionStore definitionStore) : IWorkflowDefinitionLookup
{
    public async Task<IWorkflowDefinitionVersion?> FindLatestVersion(string definitionId, CancellationToken cancellationToken = default)
    {
        var result = await versionStore.FindLatestVersionAsync(definitionId, cancellationToken);
        return result;
    }

    public async Task<IWorkflowDefinition> GetDefinition(string id, CancellationToken cancellationToken = default)
    {
        var result = await definitionStore.GetAsync(id, cancellationToken);
        return result;
    }

    public async Task<IWorkflowDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default)
    {
        var result = await versionStore.GetAsync(versionId, cancellationToken);
        return result;
    }

    public async Task<IEnumerable<IWorkflowDefinition>> ListDefinitions(string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        var filter = new WorkflowDefinitionFilter
        {
            SearchTerm = searchTerm
        };

        var result = await definitionStore.ListAsync(filter, cancellationToken);
        return result;
    }

    public async Task<IEnumerable<WorkflowDefinitionVersionInfo>> ListVersions(string definitionId, CancellationToken cancellationToken = default)
    {
        var versions = await versionStore.ListByDefinitionAsync(definitionId, cancellationToken);
        return versions.Select(e => new WorkflowDefinitionVersionInfo(e.Id, e.Version, e.CreatedAt));
    }
}
