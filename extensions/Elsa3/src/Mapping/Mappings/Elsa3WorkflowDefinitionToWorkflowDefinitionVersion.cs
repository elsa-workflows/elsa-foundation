using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Models;

namespace Elsa3.Mapping.Mappings;

/// <summary>
/// Wraps converted Elsa 4 workflow state in an <see cref="IWorkflowDefinitionVersion"/> via the workflow factories
/// (which generate the ids). Elsa-3's integer version becomes a semver string (<c>n</c> → <c>"n.R.0"</c>, R the
/// converter revision, 0 without one), and the version id follows <see cref="ReusableActivityImportIdentity.WorkflowVersion"/>.
/// </summary>
public sealed class Elsa3WorkflowDefinitionToWorkflowDefinitionVersion(
    IWorkflowDefinitionFactory definitionFactory,
    IWorkflowDefinitionVersionFactory versionFactory,
    IElsa3ConverterRevision? converterRevision = null)
{
    private readonly int _converterRevision = converterRevision?.Current ?? 0;

    public IWorkflowDefinitionVersion Map(Elsa3WorkflowDefinition definition, WorkflowDefinitionState state)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);
        var workflowDefinition = definitionFactory.Create(definition.Name, definition.Description, definition.DefinitionId);
        return versionFactory.Create(
            workflowDefinition,
            version: ReusableActivityImportIdentity.ImportedVersion(definition.Version, _converterRevision),
            state,
            sourceCreatedAt: definition.CreatedAt,
            id: ReusableActivityImportIdentity.WorkflowVersion(definition.Id, _converterRevision));
    }
}
