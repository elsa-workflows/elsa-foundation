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

        // A nonblank correlation narrows only the resume fan-in to same-name Event waits that retained the same
        // authored value (#1001). It does not filter published-trigger starts; null correlation remains broadcast.
        //
        // A local publish (#1117) instead confines delivery to the publishing execution: ResumeOnly plus the
        // target id, which the router uses to narrow the fan-in. The target is read from the intent — the runtime's
        // own record of which execution committed the send — never from the (activity-authored) payload, so an
        // activity cannot redirect a local publish at someone else's instance.
        var localTarget = payload.IsLocalEvent ? intent.WorkflowExecutionId : null;
        var request = new StimulusDispatchRequest(
            stimulusType: payload.StimulusType,
            stimulusHash: payload.StimulusHash,
            input: payload.Payload,
            correlationId: payload.CorrelationId,
            mode: localTarget is null ? StimulusRoutingMode.StartAndResume : StimulusRoutingMode.ResumeOnly,
            idempotencyKey: intent.IdempotencyKey,
            requestedBy: PublishStimulusConstants.RequestedBy,
            targetWorkflowExecutionId: localTarget);

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
