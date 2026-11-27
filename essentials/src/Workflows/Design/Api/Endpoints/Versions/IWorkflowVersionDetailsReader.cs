using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions;

/// <summary>
/// Composes the details view for one persisted workflow definition version: the version with its
/// definition and its stored layout.
/// </summary>
/// <remarks>
/// Shared by the version read path and the promote mutation, which returns the newly created
/// version's details. It replaces the internal <c>GetVersion</c> mediator round-trip the promote
/// handler used, so the read composition exists once without any dispatch indirection.
/// </remarks>
public interface IWorkflowVersionDetailsReader
{
    Task<WorkflowDefinitionVersionDetailsView> ReadAsync(string versionId, CancellationToken cancellationToken);
}

public sealed class WorkflowVersionDetailsReader(
    IWorkflowDefinitionVersionStore versionStore,
    IWorkflowDefinitionVersionLayoutStore layoutStore) : IWorkflowVersionDetailsReader
{
    public async Task<WorkflowDefinitionVersionDetailsView> ReadAsync(string versionId, CancellationToken cancellationToken)
    {
        var version = await versionStore.GetWithDefinitionAsync(versionId, cancellationToken);
        var layout = await layoutStore.FindByVersionIdAsync(versionId, cancellationToken);
        return version.ToDetailsView(layout?.Records, layout?.ActivityPresentation);
    }
}
