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

    /// <summary>
    /// When <see langword="true"/>, the source asks for the latest reconciled version of each
    /// definition it contributes to be published after a successful pass (spec 147). The flag is
    /// snapshotted per contribution onto <c>WorkflowVersionSourceClaim.PublishRequested</c>; the
    /// publish step itself lives on the Publishing side of the seam and only acts when a publishing
    /// feature subscribing to <c>WorkflowVersionsReconciled</c> is composed. Defaults to
    /// <see langword="false"/> — existing sources keep today's import-only behaviour.
    /// </summary>
    bool RequestsPublication => false;
}
