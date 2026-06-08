using Elsa.Workflows.Design.Core.Contracts;
using Elsa3.Models;

namespace Elsa3.Mapping.Mappings;

/// <summary>
/// Converts an Elsa-3 workflow definition to an Elsa-4 <see cref="IWorkflowDefinitionVersion"/> via the
/// workflow factories (which generate the ids). Elsa-3's integer version becomes a semver string
/// (<c>n</c> → <c>"n.0.0"</c>).
/// </summary>
public sealed class Elsa3WorkflowDefinitionToWorkflowDefinitionVersion(
    Elsa3WorkflowDefinitionToState stateMapper,
    IWorkflowDefinitionFactory definitionFactory,
    IWorkflowDefinitionVersionFactory versionFactory)
{
    public async ValueTask<IWorkflowDefinitionVersion> Map(Elsa3WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var workflowDefinition = definitionFactory.Create(definition.Name, definition.Description, definition.DefinitionId);
        var state = await stateMapper.Map(definition, cancellationToken);

        return versionFactory.Create(
            workflowDefinition,
            version: $"{definition.Version}.0.0",
            state,
            sourceCreatedAt: definition.CreatedAt,
            id: definition.Id);
    }
}
