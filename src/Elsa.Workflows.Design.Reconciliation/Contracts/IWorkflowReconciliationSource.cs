using Elsa.Workflows.Design.Reconciliation.Models;

namespace Elsa.Workflows.Design.Reconciliation.Contracts;

/// <summary>
/// A source of workflow-definition versions for the reconciliation lifecycle. Implementations
/// read from their backing store (file system, Elsa3 import, git, CRM, …) and return a flat
/// collection of <see cref="WorkflowVersionReconciliationModel"/> entries. The source itself
/// carries its identity (<see cref="SourceKind"/>, <see cref="SourceId"/>) — consumers don't
/// configure that from the outside.
/// </summary>
public interface IWorkflowReconciliationSource
{
    ValueTask<IEnumerable<WorkflowVersionReconciliationModel>> Read(CancellationToken cancellationToken);

    string SourceId { get; }

    string SourceKind { get; }
}
