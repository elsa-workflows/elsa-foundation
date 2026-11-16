using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.ActivityExecutions;

/// <summary>
/// The one resolution of an activity occurrence's logical operation keys, shared by every place that builds an
/// activity context (invoke, background work, typed resume) and by the operator-retry alteration.
/// </summary>
/// <remarks>
/// A structural parent assigns a key through its child-schedule request metadata. The scheduler persists that
/// metadata on the child's <see cref="ActivitySchedulingProvenance.Metadata"/>, not on
/// <see cref="ActivityExecutionState.Metadata"/>, so the provenance is read first; the state metadata is a fallback
/// for successors written by an alteration. Without an assigned key the occurrence's first-attempt identity is used,
/// which is stable across boundary retries, and then the invocation id, which is stable across crash re-attempts.
/// </remarks>
public static class ActivityOperationKeyResolver
{
    /// <summary>Resolves the operation key: assigned key, then first-attempt identity, then invocation id.</summary>
    public static string ResolveOperationKey(ActivityExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.OperationKey;
    }

    /// <summary>Resolves the target operation key, which exists only when a structural parent assigned one.</summary>
    public static string? ResolveTargetOperationKey(ActivityExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.TargetOperationKey;
    }

    /// <summary>Returns <paramref name="context"/> carrying the keys resolved from <paramref name="state"/>.</summary>
    public static ActivityExecutionContext WithResolvedOperationKeys(this ActivityExecutionContext context, ActivityExecutionState? state)
    {
        ArgumentNullException.ThrowIfNull(context);
        return state is null
            ? context
            : context.WithOperationKeys(ResolveOperationKey(state), ResolveTargetOperationKey(state));
    }

}
