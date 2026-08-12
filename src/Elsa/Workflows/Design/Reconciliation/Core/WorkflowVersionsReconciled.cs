using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Design.Reconciliation.Core;

/// <summary>
/// Notification published by the workflow-version reconciler after a pass completed with every
/// contributed version materialized (or verified present). Not published when the pass aborts.
/// Unlike <see cref="WorkflowVersionsReconciling"/> this is not a fan-in contribution event:
/// features subscribe independently (publication, cache invalidation, telemetry) and there is no
/// contributor interface or aggregating-handler constraint.
/// </summary>
/// <remarks>
/// Delivery is <b>Sequential</b> (inline): subscribers run inside the reconcile startup task — under
/// its single-node lock, before shell activation completes and therefore before readiness turns
/// ready. The corollary is that there is no exception shielding: subscribers MUST NOT throw. A
/// subscriber that lets an exception escape fails the reconcile pass and shell activation;
/// recoverable per-item failures are logged and swallowed by the subscriber itself.
/// </remarks>
public sealed class WorkflowVersionsReconciled(IReadOnlyList<WorkflowVersionSourceClaim> claims) : IEvent
{
    /// <summary>One claim per <em>reconciled</em> envelope, in contribution order (source registration
    /// order, then entry order within a source). Envelopes the pass skipped as outdated — a newer version
    /// is already in the catalog — contribute no claim, so a subscriber never acts on a version this pass
    /// declined to import. See <see cref="WorkflowVersionSourceClaim"/>.</summary>
    public IReadOnlyList<WorkflowVersionSourceClaim> Claims { get; } = claims;
}
