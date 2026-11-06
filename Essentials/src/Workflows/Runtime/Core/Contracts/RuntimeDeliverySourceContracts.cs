using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Marks the scoped source facet of the selected Runtime persistence family; no default is registered.</summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class RuntimeDeliverySourceStoreReplacementContractAttribute : Attribute;

/// <summary>Unregistered provider SPI. Capability qualification concerns actual scoped stores, never connection strings.</summary>
/// <remarks>Implementations reread canonical content, scope, current claims and exact registered plan types before
/// effects/progress. Commit atomically settles work, family progress and derived follow-ups. Uncertain commits throw
/// with their cause/operation identity; callers reconcile canonical evidence, never assume noncommit.</remarks>
[RuntimeDeliverySourceStoreReplacementContract]
public interface IRuntimeDeliverySourceStore
{
    bool CanParticipate(RuntimeDeliveryBoundary boundary) => false;
    ValueTask<RuntimeDeliverySourceSnapshot?> ReadAsync(RuntimeDeliverySourceAddress source, CancellationToken cancellationToken = default);
    /// <summary>Claims only within an explicit checked discovery partition; unspecified requests refuse.</summary>
    /// <remarks>RequirePartition is structural only. Resolve trusted canonical family/host authority and complete exact
    /// scope/tenant/execution-partition/run/test/authority predicates before Take and before any claim mutation. Hashes
    /// are query projections, never sufficient authority; no broad page followed by foreign-row filtering is supported.</remarks>
    ValueTask<IReadOnlyList<RuntimeDeliveryWorkClaim>> ClaimAsync(RuntimeDeliveryClaimRequest request, CancellationToken cancellationToken = default);
    ValueTask<RuntimeDeliveryWorkClaim?> RenewAsync(RuntimeDeliveryWorkClaim claim, TimeSpan visibility, CancellationToken cancellationToken = default);
    ValueTask<RuntimeSourceCommitResult> CommitAsync(RuntimeSourceCommit commit, CancellationToken cancellationToken = default);
}

/// <summary>Scoped additive family planner. Reads/proposals only: no reservations, acceptance or target effects.</summary>
public interface IRuntimeDeliverySourcePlanner
{
    ValueTask<RuntimeSourceCommit> PrepareAsync(RuntimeDeliveryContext source, CancellationToken cancellationToken = default);
}

/// <summary>Scoped additive family handler for an already-committed exact Dispatch child, never discovery work.</summary>
/// <remarks>Revalidate the canonical child/source/recipient capability before effects. Transport diagnostics are not
/// delivery proof; completion proposes a plan which the provider validates against canonical target evidence.</remarks>
public interface IRuntimeDeliveryDispatchHandler
{
    ValueTask<RuntimeDeliveryAttempt> HandleAsync(RuntimeDeliveryContext child, CancellationToken cancellationToken = default);
    ValueTask<RuntimeSourceCommit> PrepareCompletionAsync(RuntimeDeliveryContext child, RuntimeDeliveryAttempt attempt, CancellationToken cancellationToken = default);
}

/// <summary>Family-closed, versioned typed plan marker. Only exact registered sealed types are eligible.</summary>
/// <remarks>The marker grants no authority. A provider must understand the exact type/family/version and reread
/// canonical evidence and plan contents/fingerprint at commit, even after constructor checks. Family plans must be
/// immutable. No generic JSON/SQL/delegate mutation plan or caller-asserted Delivered result is supported.</remarks>
public interface IRuntimeSourcePlan
{
    string Family { get; }
    int Version { get; }
}

/// <summary>Prepares a typed finalization without saving acceptance or causing recipient effects.</summary>
/// <remarks>Only the existing fenced outbox claim-completion transaction may persist the prepared participant.
/// Both inherited bare paths refuse by default, so an old dispatcher cannot discard the preparation result.</remarks>
public interface IRuntimePostCommitCompletionAwareIntentHandler : IRuntimePostCommitDurableSourceIntentHandler
{
    ValueTask<RuntimePostCommitSourceFinalization> PrepareCompletionAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default);
    ValueTask IRuntimePostCommitIntentHandler.HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken) =>
        RuntimeDeliveryContractRefusal.Refuse(intent, null, cancellationToken);
    ValueTask IRuntimePostCommitDurableSourceIntentHandler.HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken) =>
        RuntimeDeliveryContractRefusal.Refuse(source, cancellationToken);
}

/// <summary>Prepares legacy-handled or typed finalization work; no registration or processor adoption is implied.</summary>
public interface IRuntimePostCommitCompletionAwareDispatcher : IRuntimePostCommitIntentDispatcher
{
    ValueTask<RuntimePostCommitDispatchPreparation> PrepareDispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default);
    ValueTask IRuntimePostCommitIntentDispatcher.DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken) =>
        RuntimeDeliveryContractRefusal.Refuse(intent, null, cancellationToken);
    ValueTask IRuntimePostCommitIntentDispatcher.DispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken) =>
        RuntimeDeliveryContractRefusal.Refuse(source, cancellationToken);
}

internal static class RuntimeDeliveryContractRefusal
{
    internal static ValueTask Refuse(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Refuse(source.Intent, source.SourceOutboxItemId, cancellationToken);
    }
    internal static ValueTask Refuse(RuntimePostCommitIntent intent, string? sourceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException(new RuntimePostCommitSourceBoundaryException(sourceId, intent.Kind,
            new NotSupportedException("Completion-aware work requires the typed preparation and atomic completion path."), false));
    }
}
