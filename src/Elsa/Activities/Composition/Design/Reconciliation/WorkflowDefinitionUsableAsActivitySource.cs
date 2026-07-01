using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Activities.Composition.Design.Reconciliation;

/// <summary>
/// The single class that reaches into the Workflows Design read ports to discover usable-as-activity
/// workflow versions (§2.7 adapter). It quarantines the cross-sub-domain dependency: everything else in
/// the Workflow activity kind's design side depends only on <see cref="IUsableAsActivityWorkflowSource"/>.
/// </summary>
/// <remarks>
/// Discovery is a full scan over definitions and their published versions, filtered on
/// <c>WorkflowActivityOptions.UsableAsActivity</c>. Reconciliation is a startup/catalog-rebuild path
/// rather than request-time, so a scan is acceptable. When a persisted usable-as-activity index lands
/// (EF Core shadow column + Groundwork index), the targeted query replaces this scan here — the
/// reconciliation source and its tests do not change.
/// </remarks>
public sealed class WorkflowDefinitionUsableAsActivitySource(
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionVersionStore versionStore) : IUsableAsActivityWorkflowSource
{
    public async ValueTask<IEnumerable<UsableAsActivityWorkflow>> Read(CancellationToken cancellationToken)
    {
        var results = new List<UsableAsActivityWorkflow>();
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

                results.Add(new UsableAsActivityWorkflow(
                    DefinitionId: definition.Id,
                    VersionId: version.Id,
                    Version: version.Version,
                    Name: definition.Name,
                    Description: definition.Description,
                    Category: options.ActivityCategory,
                    Inputs: state!.Inputs,
                    Outputs: state.Outputs));
            }
        }

        return results;
    }
}
