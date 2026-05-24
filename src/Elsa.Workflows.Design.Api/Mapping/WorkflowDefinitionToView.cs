using Elsa.Mapping.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Api.Mapping;

public sealed class WorkflowDefinitionToView : IObjectMapping<WorkflowDefinition, WorkflowDefinitionView>
{
    public WorkflowDefinitionView Map(WorkflowDefinition source)
    {
        return new(
            source.Id,
            source.Name,
            source.Description,
            source.MetaData?.IsSystem ?? false,
            source.CreatedAt,
            source.LastModifiedAt
        );
    }
}
