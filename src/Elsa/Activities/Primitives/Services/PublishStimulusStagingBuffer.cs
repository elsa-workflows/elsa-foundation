using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Primitives.Services;

/// <summary>
/// Invocation-keyed hand-off between transient <see cref="PublishEvent"/> activations and runtime checkpoint owners
/// (spec 135 D1). It mirrors <c>WorkflowDispatchStagingBuffer</c>'s shape, but is publish-typed: it accepts the send's
/// authored facets and builds the <c>PublishStimulusIntentKind</c> post-commit intent itself — deriving the shared
/// <c>Event</c> stimulus routing pair via <see cref="EventStimulus"/> and a bounded deterministic intent id/idempotency
/// key from <c>(activityExecutionId, ordinal)</c> — so a redelivered outbox item is idempotent and multiple publishes
/// from one activity never collide. An activity cannot supply an intent kind through this seam (spoof resistance).
/// </summary>
public sealed class PublishStimulusStagingBuffer :
    IPublishStimulusStager,
    IPublishStimulusStagingAccessor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<(string WorkflowExecutionId, string ActivityExecutionId), List<PublishStimulusRequest>> _requests = new();
    private readonly TimeProvider _timeProvider;

    public PublishStimulusStagingBuffer() : this(TimeProvider.System)
    {
    }

    public PublishStimulusStagingBuffer(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    public void StagePublishStimulus(PublishStimulusRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = (request.WorkflowExecutionId, request.ActivityExecutionId);
        var list = _requests.GetOrAdd(key, static _ => []);
        lock (list)
            list.Add(request);
    }

    public void Reset(string workflowExecutionId, string activityExecutionId)
    {
        ValidateIdentity(workflowExecutionId, activityExecutionId);
        _requests.TryRemove((workflowExecutionId, activityExecutionId), out _);
    }

    public IReadOnlyList<RuntimePostCommitIntent> TakePublishStimuli(string workflowExecutionId, string activityExecutionId)
    {
        ValidateIdentity(workflowExecutionId, activityExecutionId);
        if (!_requests.TryRemove((workflowExecutionId, activityExecutionId), out var staged))
            return [];

        var now = _timeProvider.GetUtcNow();
        lock (staged)
            return staged.Select((request, ordinal) => BuildIntent(request, ordinal, now)).ToArray();
    }

    private static RuntimePostCommitIntent BuildIntent(PublishStimulusRequest request, int ordinal, DateTimeOffset recordedAt)
    {
        var step = ordinal.ToString(CultureInfo.InvariantCulture);
        var intentId = RuntimeChainId.Derive($"publish-stimulus:{request.ActivityExecutionId}", step);
        var idempotencyKey = $"publish-stimulus:{request.ActivityExecutionId}:{step}";
        var payload = new PublishStimulusIntentPayload(
            EventStimulus.StimulusType,
            EventStimulus.Hash(request.EventName),
            request.EventName,
            request.CorrelationId,
            request.Payload,
            request.IsLocalEvent);

        return new RuntimePostCommitIntent(
            intentId: intentId,
            workflowExecutionId: request.WorkflowExecutionId,
            kind: PublishStimulusConstants.PublishStimulusIntentKind,
            recordedAt: recordedAt,
            activityExecutionId: request.ActivityExecutionId,
            idempotencyKey: idempotencyKey,
            payload: JsonSerializer.SerializeToElement(payload, SerializerOptions));
    }

    private static void ValidateIdentity(string workflowExecutionId, string activityExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
    }
}
