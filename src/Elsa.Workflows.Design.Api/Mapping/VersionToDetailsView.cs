using Elsa.Mapping.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Api.Mapping;

public sealed class VersionToDetailsView(IObjectMapping<WorkflowDefinition, WorkflowDefinitionView> definitionToView, IObjectMapping<WorkflowDefinitionState, WorkflowDefinitionStateView> stateMappings)

    : IObjectMapping<WorkflowDefinitionVersion, WorkflowDefinitionVersionDetailsView>
{
    public async ValueTask<WorkflowDefinitionVersionDetailsView> Map(WorkflowDefinitionVersion source, CancellationToken cancellationToken)
    {
        if (source.Definition is null)
            throw new InvalidOperationException($"Mapping failed: source.Definition is null");
        if (source.State is null)
            throw new InvalidOperationException($"Mapping failed: source.State is null");

        var definition = await definitionToView.Map(source.Definition);
        var state = await stateMappings.Map(source.State);

        var result = new WorkflowDefinitionVersionDetailsView(
            source.Id,
            source.Version,
            definition,
            state
        );

        return result;
    }
}
