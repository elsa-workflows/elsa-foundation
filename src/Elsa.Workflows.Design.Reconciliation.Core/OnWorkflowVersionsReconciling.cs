using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Reconciliation.Core;

/// <summary>
/// Contribution event published by <see cref="IWorkflowVersionReconciler"/> on each pass.
/// Source modules (JSON file, Elsa3 import, CRM pull, …) handle this event and contribute the
/// workflow versions they currently observe via <see cref="AddVersion"/>.
///
/// Exposes a method-based contribution API per framework §2.6.1's "intent-revealing methods,
/// not raw collections" sub-rule (Unit C Phase-3 amendment, 2026-05-28). The backing list is
/// private; read access is via the public <see cref="Versions"/> property typed as
/// <see cref="IReadOnlyList{T}"/> — handlers cannot replace the list or mutate it.
/// </summary>
public sealed class OnWorkflowVersionsReconciling : IDomainEvent
{
    private readonly List<IWorkflowDefinitionVersion> _versions = new();

    /// <summary>
    /// Contribute a workflow version observed by this source module. Handlers call this for
    /// every version they observe.
    /// </summary>
    public void AddVersion(IWorkflowDefinitionVersion version) => _versions.Add(version);

    /// <summary>
    /// Read-only view of the accumulated contributions. Consumed by the dispatcher (the
    /// reconciler) after the handler chain completes.
    /// </summary>
    public IReadOnlyList<IWorkflowDefinitionVersion> Versions => _versions;
}
