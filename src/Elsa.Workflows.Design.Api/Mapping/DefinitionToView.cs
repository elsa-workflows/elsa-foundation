using Elsa.Mapping.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Api.Mapping;

public sealed class DefinitionToView : IObjectMapping<WorkflowDefinition, WorkflowDefinitionView>
{
    public ValueTask<WorkflowDefinitionView> Map(WorkflowDefinition source, CancellationToken cancellationToken)
    {
        var result = new WorkflowDefinitionView(
            source.Id,
            source.Name,
            source.Description,
            source.CreatedAt,
            source.LastModifiedAt
        );

        return new(result);
    }
}
