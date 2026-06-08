using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Core;

namespace Elsa.Workflows.Design.Reconciliation.Handlers;

/// <summary>
/// Universal handler for <see cref="OnWorkflowVersionsReconciling"/>. Reads every registered
/// <see cref="IWorkflowReconciliationSource"/> in turn and contributes one definition version per
/// entry (built via the factories) by adding to <c>event.Versions</c>. Source modules extend the
/// reconciliation feature by registering their own <see cref="IWorkflowReconciliationSource"/>; they
/// do not write their own handlers.
/// </summary>
public sealed class WorkflowVersionsReconcilingHandler(
    IWorkflowDefinitionFactory definitionFactory,
    IWorkflowDefinitionVersionFactory versionFactory,
    IEnumerable<IWorkflowReconciliationSource> sources)
    : IEventHandler<OnWorkflowVersionsReconciling>
{
    public async Task Handle(OnWorkflowVersionsReconciling domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            var entries = await source.Read(cancellationToken);

            foreach (var entry in entries)
            {
                var definition = definitionFactory.Create(entry.Name, entry.Description, entry.DefinitionId);
                var version = versionFactory.Create(definition, entry.Version, entry.State, entry.SourceCreatedAt);
                domainEvent.Versions.Add(version);
            }
        }
    }
}
