using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions;

public sealed class GetVersionRequestHandler(
    IWorkflowDefinitionVersionStore store,
    IWorkflowDefinitionVersionLayoutStore layoutStore) : IRequestHandler<GetVersion, WorkflowDefinitionVersionDetailsView>
{
    public async Task<WorkflowDefinitionVersionDetailsView> Handle(GetVersion request, CancellationToken cancellationToken)
    {
        if (request.VersionId.StartsWith("draft:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Synthetic draft identifiers are not persisted workflow definition versions.", nameof(request));

        var result = await store.GetWithDefinitionAsync(request.VersionId, cancellationToken);
        var layout = await layoutStore.FindByVersionIdAsync(request.VersionId, cancellationToken);
        return result.ToDetailsView(layout?.Records, layout?.ActivityPresentation);
    }
}
