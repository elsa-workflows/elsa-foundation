using Elsa.Mapping.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Api.Mapping;

public sealed class WorkflowDefinitionVersionToDetailsView(IObjectMapping<WorkflowDefinition, WorkflowDefinitionView> defMapping, IObjectMapping<WorkflowDefinitionState, WorkflowDefinitionStateView> stateMapping) 
    : IObjectMapping<WorkflowDefinitionVersion, WorkflowDefinitionVersionDetailsView>
{
    public WorkflowDefinitionVersionDetailsView Map(WorkflowDefinitionVersion source)
    {
        if (source.Definition is null)
            throw new InvalidOperationException($"Mapping failed: source.Definition is null");
        if (source.State is null)
            throw new InvalidOperationException($"Mapping failed: source.State is null");

        var definition = defMapping.Map(source.Definition);
        var state = stateMapping.Map(source.State);

        return new(
            source.Id,
            source.Version,
            definition,
            state
        );
    }
}
