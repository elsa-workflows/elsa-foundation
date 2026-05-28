using Elsa.Activities.Design.Core.Contracts;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Reconciliation.Core;

/// <summary>
/// Contribution event published by <see cref="IActivityVersionReconciler"/> on each pass.
/// Source modules (JSON file, CLR discovery, workflow bridge, …) handle this event and
/// contribute the activity versions they currently observe via <see cref="AddVersion"/>.
///
/// Exposes a method-based contribution API per framework §2.6.1's "intent-revealing methods,
/// not raw collections" sub-rule (Unit C Phase-3 amendment, 2026-05-28). The backing list
/// is private; read access is via the public <see cref="Versions"/> property typed as
/// <see cref="IReadOnlyList{T}"/> — handlers cannot replace the list or mutate it
/// (no Add/Remove/Clear on the read surface), so the encapsulation goal holds without
/// needing assembly-scoped visibility.
/// </summary>
public sealed class OnActivityVersionsReconciling : IDomainEvent
{
    private readonly List<IActivityDefinitionVersion> _versions = new();

    /// <summary>
    /// Contribute an activity version observed by this source module. Handlers call this
    /// for every version they observe.
    /// </summary>
    public void AddVersion(IActivityDefinitionVersion version) => _versions.Add(version);

    /// <summary>
    /// Read-only view of the accumulated contributions. Consumed by the dispatcher (the
    /// reconciler) after the handler chain completes. Public read access is safe — the
    /// type is <see cref="IReadOnlyList{T}"/>, so no caller can replace the list or
    /// mutate it; only <see cref="AddVersion"/> can add to it.
    /// </summary>
    public IReadOnlyList<IActivityDefinitionVersion> Versions => _versions;
}
