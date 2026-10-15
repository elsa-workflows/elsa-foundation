using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Models;

namespace Elsa3.Mapping.Mappings;

/// <summary>
/// Wraps converted Elsa 4 workflow state in an <see cref="IWorkflowDefinitionVersion"/> via the workflow factories
/// (which generate the ids). Elsa-3's integer version becomes a semver string (<c>n</c> → <c>"n.0.0"</c>).
/// </summary>
public sealed class Elsa3WorkflowDefinitionToWorkflowDefinitionVersion(
    IWorkflowDefinitionFactory definitionFactory,
    IWorkflowDefinitionVersionFactory versionFactory)
{
    public IWorkflowDefinitionVersion Map(Elsa3WorkflowDefinition definition, WorkflowDefinitionState state)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);
        var workflowDefinition = definitionFactory.Create(definition.Name, definition.Description, definition.DefinitionId);
        return versionFactory.Create(
            workflowDefinition,
            version: $"{definition.Version}.0.0",
            state,
            sourceCreatedAt: definition.CreatedAt,
            id: definition.Id);
    }
}
