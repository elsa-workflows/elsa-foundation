using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Primitives.Models;

namespace Elsa.Activities.Composition.Design.Reconciliation;

/// <summary>
/// An <see cref="IActivityReconciliationSource"/> that contributes one catalog row per workflow
/// definition version marked usable-as-activity (§2.6.1). Each row carries the Workflow-kind descriptor
/// (<see cref="WorkflowIdentity"/>) so the runtime <c>WorkflowActivityConstructor</c> can build a
/// <c>WorkflowDefinitionActivity</c> for it; the workflow's own surfaced inputs/outputs are mirrored
/// directly onto the activity because both sides use the same <c>InputDefinition</c>/<c>OutputDefinition</c>
/// shapes (spec 006 FR-012/FR-014, carried over from 005).
/// </summary>
/// <remarks>
/// <para>
/// This is a Design → Design read: it reads authored workflow state to catalog workflows as activities,
/// so it references the Workflows Design read ports. It introduces no Runtime → Design dependency
/// (Elsa §E2.2 concerns only the runtime side, which stays in <c>Elsa.Activities.Composition.Runtime</c>).
/// </para>
/// <para>
/// Discovery is a full scan over definitions and their versions, filtered on
/// <c>WorkflowActivityOptions.UsableAsActivity</c>. Reconciliation is a startup/catalog-rebuild
/// operation rather than a request-time path, so a scan is acceptable; a targeted "usable-as-activity"
/// read port can be introduced later if catalog size makes the scan costly.
/// </para>
/// </remarks>
public sealed class WorkflowActivityReconciliationSource(
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionVersionStore versionStore) : IActivityReconciliationSource
{
    public string SourceId => "WorkflowActivities";

    public string SourceKind => "Workflow";

    public async ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken)
    {
        var models = new List<ActivityVersionReconciliationModel>();
        var definitions = await definitionStore.ListAsync(new WorkflowDefinitionFilter(), cancellationToken);

        foreach (var definition in definitions)
        {
            var versions = await versionStore.ListByDefinitionAsync(definition.Id, cancellationToken);

            foreach (var version in versions)
            {
                var state = version.State;
                var options = state?.WorkflowActivityOptions;

                if (options?.UsableAsActivity != true)
                    continue;

                models.Add(new ActivityVersionReconciliationModel(
                    Id: null,
                    Version: version.Version,
                    // The workflow definition is the stable "activity type"; each usable-as-activity
                    // version is a distinct row under it (version-distinct catalog rows, 005 US1).
                    ActivityTypeKey: definition.Id,
                    DisplayName: definition.Name,
                    Category: options.ActivityCategory,
                    Description: definition.Description,
                    DescriptorType: typeof(WorkflowIdentity).FullName!,
                    Descriptor: new WorkflowIdentity(definition.Id, version.Id, version.Version),
                    Inputs: state!.Inputs,
                    Outputs: state.Outputs,
                    DesignFacets: []));
            }
        }

        return models;
    }
}
