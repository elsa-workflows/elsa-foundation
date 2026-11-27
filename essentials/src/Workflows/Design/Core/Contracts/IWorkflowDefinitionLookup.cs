using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

public interface IWorkflowDefinitionLookup
{
    Task<IWorkflowDefinition> GetDefinition(string id, CancellationToken cancellationToken = default);

    Task<IEnumerable<IWorkflowDefinition>> ListDefinitions(string? searchTerm = null, CancellationToken cancellationToken = default);

    Task<IWorkflowDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default);

    Task<IWorkflowDefinitionVersion?> FindLatestVersion(string definitionId, CancellationToken cancellationToken = default);

    Task<IEnumerable<WorkflowDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default);
}
