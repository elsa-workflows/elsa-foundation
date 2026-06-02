using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Reconciliation.Core;

/// <summary>
/// Contribution event published by <see cref="IWorkflowVersionReconciler"/> on each pass. The
/// single <c>WorkflowVersionsReconcilingHandler</c> resolves every workflow reconciliation source
/// and adds the workflow versions they observe to <see cref="Versions"/>; the reconciler reads the
/// accumulated set after dispatch.
/// </summary>
/// <remarks>
/// <see cref="Versions"/> is a directly-accessible <see cref="ICollection{T}"/>: the aggregating
/// handler writes into it; the reconciler reads it after the chain completes.
/// </remarks>
public sealed class OnWorkflowVersionsReconciling : IEvent
{
    /// <summary>The accumulated workflow versions. Populated by the aggregating handler.</summary>
    public ICollection<IWorkflowDefinitionVersion> Versions { get; } = [];
}
