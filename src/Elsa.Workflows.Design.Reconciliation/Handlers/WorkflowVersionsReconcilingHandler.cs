using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Core;

namespace Elsa.Workflows.Design.Reconciliation.Handlers;

/// <summary>
/// Universal handler for <see cref="OnWorkflowVersionsReconciling"/>. Reads every registered
/// <see cref="IWorkflowReconciliationSource"/> in turn and contributes one
/// <c>WorkflowDefinitionVersion</c> per entry via <c>event.AddVersion(...)</c>. Source modules
/// extend the reconciliation feature by registering their own
/// <see cref="IWorkflowReconciliationSource"/>; they do not write their own handlers.
/// </summary>
public sealed class WorkflowVersionsReconcilingHandler(
    IIdentityGenerator identityGenerator,
    IEnumerable<IWorkflowReconciliationSource> sources)
    : IDomainEventHandler<OnWorkflowVersionsReconciling>
{
    public async ValueTask Handle(OnWorkflowVersionsReconciling domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            var entries = await source.Read(cancellationToken);

            foreach (var entry in entries)
            {
                var definition = new WorkflowDefinition
                {
                    Id = string.IsNullOrWhiteSpace(entry.DefinitionId)
                        ? identityGenerator.Generate()
                        : entry.DefinitionId,
                    Name = entry.Name,
                    Description = entry.Description,
                };

                var version = new WorkflowDefinitionVersion(definition.Id, entry.Version, sourceCreatedAt: entry.SourceCreatedAt)
                {
                    Id = identityGenerator.Generate(),
                    Definition = definition,
                    State = entry.State,
                };

                domainEvent.AddVersion(version);
            }
        }
    }
}
