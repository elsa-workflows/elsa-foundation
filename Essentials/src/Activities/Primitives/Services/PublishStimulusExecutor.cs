using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Services;

/// <summary>
/// Delivers a committed <see cref="PublishEvent"/> publish intent: after the activity's own checkpoint
/// commits, it routes the carried named-event stimulus through the existing <see cref="IStimulusRouter"/> in
/// <see cref="StimulusRoutingMode.StartAndResume"/> mode — starting every published message-start workflow that listens
/// on the name AND resuming every parked same-name catch, in one delivery. The router is <b>never</b> called during the
/// activity's execution; delivery is fire-and-continue, retried by the outbox per the registered retry policy.
/// </summary>
public sealed class PublishStimulusExecutor : IRuntimePostCommitDurableSourceIntentHandler
{
    private const string DeliveryFailureCode = "publish-stimulus-delivery-failed";
    private const string DeliveryFailureSummary = "The named-event stimulus could not be routed.";
    private const string ShedFailureCode = "publish-stimulus-start-shed";

    private readonly IStimulusRouter _stimulusRouter;

    public PublishStimulusExecutor(IStimulusRouter stimulusRouter)
    {
        ArgumentNullException.ThrowIfNull(stimulusRouter);
        _stimulusRouter = stimulusRouter;
    }

    /// <summary>Intent-only delivery cannot select recipients; a committed physical source is required.</summary>
    public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException(new RuntimePostCommitSourceBoundaryException(null, intent.Kind,
            new NotSupportedException("Named-event delivery requires its committed physical source and saved cohort."), false));
    }

    async ValueTask IRuntimePostCommitDurableSourceIntentHandler.HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source); cancellationToken.ThrowIfCancellationRequested();
        if (source.Intent.Kind != PublishStimulusConstants.PublishStimulusIntentKind)
            throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind,
                new InvalidDataException("The named-event handler received another intent kind."), false);
        StimulusRoutingResult result;
        try
        {
            result = await _stimulusRouter.RouteSourceAsync(source, cancellationToken);
        }
        catch (StimulusDeliveryBoundaryException exception)
        { throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, exception, exception.IsUnavailable); }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A checkpoint rule violation in a routed start or resume recurs on every attempt, and a retried start that is
            // now a duplicate would report the delivery as a success. Anything else may be infrastructure.
            var kind = RuntimeCheckpointCommitValidationException.IsCauseOf(exception)
                ? PostCommitFailureKind.Permanent
                : PostCommitFailureKind.Transient;
            throw new RuntimePostCommitDeliveryException(kind, DeliveryFailureCode, DeliveryFailureSummary, exception);
        }

        // Admission shed a start at capacity (#2548): nothing was written and its key is unconsumed, so reporting the
        // delivery as done would lose that start. Failing it as transient lets the outbox retry the whole delivery; the
        // starts and resumes that did go through answer the retry as duplicates under the same keys.
        if (result.ShedStartCount > 0)
        {
            throw new RuntimePostCommitDeliveryException(
                PostCommitFailureKind.Transient,
                ShedFailureCode,
                $"{result.ShedStartCount} start(s) were refused by runtime admission at capacity and will be retried.");
        }
    }
}
