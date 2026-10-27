using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Routes an external stimulus to workflows. This is the service Elsa 4 was missing that closes the
/// largest parity gap with Elsa 3: a single stimulus, with no explicit execution id, can start new workflow
/// instances and/or fan in to every waiting instance across executions.
/// </summary>
public interface IStimulusRouter
{
    /// <summary>Route only the immutable persisted source's accepted recipient cohort; legacy routers refuse.</summary>
    /// <exception cref="Exceptions.StimulusDeliveryBoundaryException">Source, adapter or selected-provider support is
    /// unavailable/contradictory; no fallback to per-attempt matching is permitted.</exception>
    /// <exception cref="Exceptions.RuntimePostCommitOutboxStaleClaimException">The persisted source claim is stale.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    ValueTask<StimulusRoutingResult> RouteSourceAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException<StimulusRoutingResult>(new Exceptions.StimulusDeliveryBoundaryException(source.SourceOutboxItemId,
            new NotSupportedException("The router cannot deliver an immutable saved recipient cohort."), false));
    }

    ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default);
}
