using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Workflows.Primitives.Models;

namespace Elsa.Activities.Composition.Design.Reconciliation;

/// <summary>
/// An <see cref="IActivityReconciliationSource"/> that contributes one catalog row per usable-as-activity
/// workflow version (§2.6.1). Each row carries the Workflow-kind descriptor (<see cref="WorkflowIdentity"/>)
/// so a typed child-workflow boundary can compile a canonical request for it;
/// the workflow's surfaced inputs/outputs are mirrored directly because both sides use the same
/// <c>InputDefinition</c>/<c>OutputDefinition</c> shapes (spec 006 FR-012/FR-014, carried over from 005).
/// </summary>
/// <remarks>
/// A pure mapper over <see cref="IUsableAsActivityWorkflowSource"/>: workflow discovery and reading live
/// behind that port, so this type has no Workflows Design dependency. It also introduces no Runtime →
/// Design dependency (Elsa §E2.2 concerns only the runtime side, in <c>Elsa.Activities.Composition.Runtime</c>).
/// </remarks>
public sealed class WorkflowActivityReconciliationSource(IUsableAsActivityWorkflowSource workflows)
    : IActivityReconciliationSource
{
    public string SourceId => "WorkflowActivities";

    public string SourceKind => "Workflow";

    public async ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken)
    {
        var usable = await workflows.Read(cancellationToken);

        return usable.Select(w => new ActivityVersionReconciliationModel(
            Id: null,
            Version: w.Version,
            // The workflow definition is the stable "activity type"; each usable-as-activity version is a
            // distinct row under it (version-distinct catalog rows, 005 US1).
            ActivityTypeKey: w.DefinitionId,
            DisplayName: w.Name,
            Category: w.Category,
            Description: w.Description,
            DescriptorType: typeof(WorkflowIdentity).FullName!,
            Descriptor: new WorkflowIdentity(w.DefinitionId, w.VersionId, w.Version),
            Inputs: w.Inputs,
            Outputs: w.Outputs,
            DesignFacets: [])).ToList();
    }
}
