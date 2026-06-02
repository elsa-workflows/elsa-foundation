using Elsa.Activities.Design.Core.Contracts;
using Elsa.Events.Core.Contracts;

namespace Elsa.Activities.Design.Reconciliation.Core;

/// <summary>
/// Contribution event published by <see cref="IActivityVersionReconciler"/> on each pass. The
/// single <c>ActivityVersionsReconcilingHandler</c> resolves every
/// <see cref="IActivityReconciliationSource"/> and adds the activity versions they observe to
/// <see cref="Versions"/>; the reconciler reads the accumulated set after dispatch.
/// </summary>
/// <remarks>
/// <see cref="Versions"/> is a directly-accessible <see cref="ICollection{T}"/>: the aggregating
/// handler writes into it; the reconciler reads it after the chain completes.
/// </remarks>
public sealed class OnActivityVersionsReconciling : IEvent
{
    /// <summary>The accumulated activity versions. Populated by the aggregating handler.</summary>
    public ICollection<IActivityDefinitionVersion> Versions { get; } = [];
}
