using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions;

public sealed class GetVersionRequestHandler(IWorkflowVersionDetailsReader reader)
    : IRequestHandler<GetVersion, WorkflowDefinitionVersionDetailsView>
{
    public Task<WorkflowDefinitionVersionDetailsView> Handle(GetVersion request, CancellationToken cancellationToken)
    {
        if (request.VersionId.StartsWith("draft:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Synthetic draft identifiers are not persisted workflow definition versions.", nameof(request));

        return reader.ReadAsync(request.VersionId, cancellationToken);
    }
}
