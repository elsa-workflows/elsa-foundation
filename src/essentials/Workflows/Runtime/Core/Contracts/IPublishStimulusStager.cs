using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Narrow activity-facing capability for staging one durable-first named-event publish (spec 135 D1). It mirrors
/// <see cref="IWorkflowDispatchStager"/>'s layering exactly, but is <b>publish-typed</b>: the staging method takes the
/// send's authored facets (event name, optional correlation id, optional payload), never a raw
/// <see cref="RuntimePostCommitIntent"/>. The buffer/consumer builds the <c>PublishStimulusIntentKind</c> intent
/// internally, so an activity cannot forge an arbitrary intent kind through this seam (spoof resistance). A staged
/// publish rides the activity's own checkpoint commit and is delivered post-commit by the outbox.
/// </summary>
public interface IPublishStimulusStager
{
    void StagePublishStimulus(PublishStimulusRequest request);
}

/// <summary>
/// Engine-facing side of the publish-stimulus staging seam (spec 135 D1). Requests are isolated by runtime invocation
/// identity because transient activities can be activated in child scopes. Supports multiple staged publishes per
/// invocation, returned in staging (ordinal) order so their built intents carry stable, distinct deterministic ids.
/// </summary>
public interface IPublishStimulusStagingAccessor
{
    void Reset(string workflowExecutionId, string activityExecutionId);

    /// <summary>
    /// Removes and returns the built post-commit intents for one invocation's staged publishes, in staging order.
    /// Empty when nothing was staged. Building the intents (stimulus identity, deterministic id/idempotency key) is the
    /// implementation's concern so runtime core stays free of the event-hash convention.
    /// </summary>
    IReadOnlyList<RuntimePostCommitIntent> TakePublishStimuli(string workflowExecutionId, string activityExecutionId);
}
