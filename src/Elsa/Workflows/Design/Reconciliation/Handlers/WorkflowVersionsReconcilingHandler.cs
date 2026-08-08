using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Core;

namespace Elsa.Workflows.Design.Reconciliation.Handlers;

/// <summary>
/// Universal handler for <see cref="WorkflowVersionsReconciling"/>. Reads every registered
/// <see cref="IWorkflowReconciliationSource"/> in turn and contributes one definition version per
/// entry (built via the factories) by adding to <c>event.Versions</c>. Source modules extend the
/// reconciliation feature by registering their own <see cref="IWorkflowReconciliationSource"/>; they
/// do not write their own handlers.
/// </summary>
public sealed class WorkflowVersionsReconcilingHandler(
    IWorkflowDefinitionFactory definitionFactory,
    IWorkflowDefinitionVersionFactory versionFactory,
    IEnumerable<IWorkflowReconciliationSource> sources)
    : IEventHandler<WorkflowVersionsReconciling>
{
    public async Task Handle(WorkflowVersionsReconciling domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            var entries = await source.Read(cancellationToken);

            foreach (var entry in entries)
            {
                var definition = definitionFactory.Create(entry.Name, entry.Description, entry.DefinitionId, entry.Deleted);
                var version = versionFactory.Create(definition, entry.Version, entry.State, entry.SourceCreatedAt);
                domainEvent.Versions.Add(version);
                // Provenance beside the version: source identity is not persisted on design entities, so
                // this claim is what survives to WorkflowVersionsReconciled (and the publish-on-reconcile
                // step). DefinitionId is read back from the factory-created definition so a generated id
                // (entry.DefinitionId omitted) is the final one.
                domainEvent.Claims.Add(new WorkflowVersionSourceClaim(
                    DefinitionId: definition.Id,
                    Version: entry.Version,
                    SemVerSortKey: SemVer.ToSortKey(entry.Version),
                    SourceId: source.SourceId,
                    SourceKind: source.SourceKind,
                    PublishRequested: source.RequestsPublication,
                    Deleted: entry.Deleted));
            }
        }
    }
}
