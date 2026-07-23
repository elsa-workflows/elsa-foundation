using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Services;

/// <summary>
/// Delivers a committed <see cref="PublishEvent"/> publish intent (spec 135 D1): after the activity's own checkpoint
/// commits, it routes the carried named-event stimulus through the existing <see cref="IStimulusRouter"/> in
/// <see cref="StimulusRoutingMode.StartAndResume"/> mode — starting every published message-start workflow that listens
/// on the name AND resuming every parked same-name catch, in one delivery. The router is <b>never</b> called during the
/// activity's execution; delivery is fire-and-continue, retried by the outbox per the registered retry policy.
/// </summary>
public sealed class PublishStimulusExecutor : IRuntimePostCommitIntentHandler
{
    private const string DeliveryFailureCode = "publish-stimulus-delivery-failed";
    private const string DeliveryFailureSummary = "The named-event stimulus could not be routed.";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IStimulusRouter _stimulusRouter;

    public PublishStimulusExecutor(IStimulusRouter stimulusRouter)
    {
        ArgumentNullException.ThrowIfNull(stimulusRouter);
        _stimulusRouter = stimulusRouter;
    }

    public async ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!StringComparer.Ordinal.Equals(intent.Kind, PublishStimulusConstants.PublishStimulusIntentKind))
            throw new InvalidOperationException($"PublishStimulusExecutor cannot handle post-commit intent kind '{intent.Kind}'.");
        if (intent.Payload is not { } payloadElement)
            throw new InvalidOperationException($"PublishEvent publish intent '{intent.IntentId}' has no payload.");

        var payload = payloadElement.Deserialize<PublishStimulusIntentPayload>(SerializerOptions)
            ?? throw new InvalidOperationException($"PublishEvent publish intent '{intent.IntentId}' has an invalid payload.");

        // Correlation is threaded verbatim into the dispatch request (spec 135 FR-5); narrowing is inert until #1001,
        // so an un-narrowed (null) correlation broadcasts to every same-name listener — the shipped fabric's semantics.
        var request = new StimulusDispatchRequest(
            stimulusType: payload.StimulusType,
            stimulusHash: payload.StimulusHash,
            input: payload.Payload,
            correlationId: payload.CorrelationId,
            mode: StimulusRoutingMode.StartAndResume,
            idempotencyKey: intent.IdempotencyKey,
            requestedBy: PublishStimulusConstants.RequestedBy);

        try
        {
            await _stimulusRouter.RouteAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RuntimePostCommitDeliveryException(PostCommitFailureKind.Transient, DeliveryFailureCode, DeliveryFailureSummary, exception);
        }
    }
}
