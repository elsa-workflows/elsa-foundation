using Elsa.Mapping.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Models;
using Elsa3.Models;

namespace Elsa3.Mapping.Mappings;

public sealed class Elsa3WorkflowDefinitionToWorkflowDefinitionVersion(IObjectMapping<Elsa3WorkflowDefinition, WorkflowDefinitionState> stateMapper)
    : IObjectMapping<Elsa3WorkflowDefinition, IWorkflowDefinitionVersion>
{
    public async ValueTask<IWorkflowDefinitionVersion> Map(Elsa3WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var definitionImport = new WorkflowDefinitionImport(
          definition.DefinitionId,
          definition.Name,
          definition.Description,
          definition.CreatedAt,
          definition.CreatedAt,
          Draft: null
      );

        var state = await stateMapper.Map(definition, cancellationToken);

        return new WorkflowDefinitionVersionImport(
            definition.Id,
            definition.Version,
            definition.CreatedAt,
            state,
            definitionImport
        );
    }
}
