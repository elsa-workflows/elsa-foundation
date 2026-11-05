using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Services;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Durable-first coverage for the <see cref="PublishEvent"/> send surface: the activity stages a publish
/// intent on its own commit and completes immediately (fire-and-continue) — it never routes in-execution; the staging
/// buffer builds a <c>PublishStimulusIntentKind</c> intent carrying the shared <c>Event</c> stimulus identity; and the
/// post-commit handler routes it in <see cref="StimulusRoutingMode.StartAndResume"/> mode with the carried values.
/// </summary>
public sealed class PublishEventTests
{
    private const string WorkflowExecutionId = "wfexec-publish";
    private const string ActivityExecutionId = "invocation-publish";
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_StagesPublishRequest_AndCompletesDone_WithoutRouting()
    {
        var stager = new RecordingPublishStimulusStager();
        var activity = new PublishEvent(stager) { EventName = "order.placed", CorrelationId = "corr-1" };

        // Fire-and-continue: completes Done immediately.
        var completion = await ExecuteAsync(activity);
        Assert.Equal(ActivityOutcomes.Done, completion.Outcome);

        // Durable-first: exactly one request STAGED (not routed). The activity has no router dependency at all.
        var request = Assert.Single(stager.Requests);
        Assert.Equal(WorkflowExecutionId, request.WorkflowExecutionId);
        Assert.Equal(ActivityExecutionId, request.ActivityExecutionId);
        Assert.Equal("order.placed", request.EventName);
        Assert.Equal("corr-1", request.CorrelationId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Execute_WithBlankName_Throws(string? name)
    {
        var activity = new PublishEvent(new RecordingPublishStimulusStager()) { EventName = name! };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await ExecuteAsync(activity));
    }

    [Fact]
    public async Task Buffer_BuildsIntent_WithEventStimulusIdentity_AndNoRouterCall()
    {
        var router = new RecordingStimulusRouter();
        var buffer = new PublishStimulusStagingBuffer(new FakeTimeProvider(Now));
        var payload = JsonSerializer.SerializeToElement(new { orderId = 7 });
        buffer.StagePublishStimulus(new PublishStimulusRequest(WorkflowExecutionId, ActivityExecutionId, "order.placed", "corr-1", payload));

        var intent = Assert.Single(buffer.TakePublishStimuli(WorkflowExecutionId, ActivityExecutionId));

        Assert.Equal(PublishStimulusConstants.PublishStimulusIntentKind, intent.Kind);
        Assert.Equal(WorkflowExecutionId, intent.WorkflowExecutionId);
        Assert.Equal(ActivityExecutionId, intent.ActivityExecutionId);
        Assert.Equal(Now, intent.RecordedAt);
        Assert.False(string.IsNullOrWhiteSpace(intent.IdempotencyKey));

        var intentPayload = intent.Payload!.Value.Deserialize<PublishStimulusIntentPayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(EventStimulus.StimulusType, intentPayload.StimulusType);
        Assert.Equal(EventStimulus.Hash("order.placed"), intentPayload.StimulusHash);
        Assert.Equal("order.placed", intentPayload.EventName);
        Assert.Equal("corr-1", intentPayload.CorrelationId);
        Assert.Equal(payload.GetRawText(), intentPayload.Payload!.Value.GetRawText());

        // The buffer never touches the router — routing is strictly post-commit.
        Assert.False(router.WasInvoked);
    }

    [Fact]
    public void Buffer_SupportsMultiplePublishesPerInvocation_OrdinalOrdered_WithDistinctIds()
    {
        var buffer = new PublishStimulusStagingBuffer(new FakeTimeProvider(Now));
        buffer.StagePublishStimulus(new PublishStimulusRequest(WorkflowExecutionId, ActivityExecutionId, "first"));
        buffer.StagePublishStimulus(new PublishStimulusRequest(WorkflowExecutionId, ActivityExecutionId, "second"));

        var intents = buffer.TakePublishStimuli(WorkflowExecutionId, ActivityExecutionId);

        Assert.Equal(2, intents.Count);
        Assert.NotEqual(intents[0].IntentId, intents[1].IntentId);
        Assert.NotEqual(intents[0].IdempotencyKey, intents[1].IdempotencyKey);
        Assert.Equal("first", ReadEventName(intents[0]));
        Assert.Equal("second", ReadEventName(intents[1]));
    }

    [Fact]
    public void Buffer_TakeIsIdempotent_ReturnsEmptyAfterDrainOrReset()
    {
        var buffer = new PublishStimulusStagingBuffer(new FakeTimeProvider(Now));
        buffer.StagePublishStimulus(new PublishStimulusRequest(WorkflowExecutionId, ActivityExecutionId, "e"));

        Assert.Single(buffer.TakePublishStimuli(WorkflowExecutionId, ActivityExecutionId));
        Assert.Empty(buffer.TakePublishStimuli(WorkflowExecutionId, ActivityExecutionId));

        buffer.StagePublishStimulus(new PublishStimulusRequest(WorkflowExecutionId, ActivityExecutionId, "e"));
        buffer.Reset(WorkflowExecutionId, ActivityExecutionId);
        Assert.Empty(buffer.TakePublishStimuli(WorkflowExecutionId, ActivityExecutionId));
    }


    [Fact]
    public async Task LocalEvent_StagesAndCarriesTheLocalFlag_ThroughToTheIntent()
    {
        var stager = new RecordingPublishStimulusStager();
        var activity = new PublishEvent(stager) { EventName = "order.placed", IsLocalEvent = true };

        await ExecuteAsync(activity);

        Assert.True(Assert.Single(stager.Requests).IsLocalEvent);

        var buffer = new PublishStimulusStagingBuffer(new FakeTimeProvider(Now));
        buffer.StagePublishStimulus(new PublishStimulusRequest(WorkflowExecutionId, ActivityExecutionId, "order.placed", isLocalEvent: true));
        var intent = Assert.Single(buffer.TakePublishStimuli(WorkflowExecutionId, ActivityExecutionId));

        Assert.True(ReadPayload(intent).IsLocalEvent);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Handler_Intent_only_signature_refuses_before_old_router_effects(bool local)
    {
        var router = new RecordingStimulusRouter(); var buffer = new PublishStimulusStagingBuffer(new FakeTimeProvider(Now));
        buffer.StagePublishStimulus(new PublishStimulusRequest(WorkflowExecutionId, ActivityExecutionId, "e", isLocalEvent: local));
        var intent = Assert.Single(buffer.TakePublishStimuli(WorkflowExecutionId, ActivityExecutionId));
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => new PublishStimulusExecutor(router).HandleAsync(intent).AsTask());
        Assert.Empty(router.Requests);
    }

    [Fact]
    public async Task Handler_WrapsRouterFailure_AsTransientDeliveryFailure()
    {
        var exception = await HandleRoutingFailureAsync(new InvalidOperationException("boom"));

        Assert.Equal(PostCommitFailureKind.Transient, exception.Kind);
    }

    /// <summary>
    /// A start admission shed at capacity wrote nothing and left its key unconsumed, so the delivery must fail as
    /// transient for the outbox to retry it; reporting it delivered would lose the start (#2548).
    /// </summary>
    [Fact]
    public async Task Handler_FailsAsTransient_WhenAdmissionShedsAStart()
    {
        var router = new SavedResultRouter(
            StimulusStartOutcome.Started("binding-1", "artifact-1", "wfexec-started"),
            StimulusStartOutcome.Shed("binding-2", "artifact-2", TimeSpan.FromSeconds(1)));
        var buffer = new PublishStimulusStagingBuffer(new FakeTimeProvider(Now));
        buffer.StagePublishStimulus(new PublishStimulusRequest(WorkflowExecutionId, ActivityExecutionId, "e"));
        var intent = Assert.Single(buffer.TakePublishStimuli(WorkflowExecutionId, ActivityExecutionId));

        var exception = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(async () =>
            await ((IRuntimePostCommitDurableSourceIntentHandler)new PublishStimulusExecutor(router)).HandleAsync(ConstructedSource(intent)));

        Assert.Equal(PostCommitFailureKind.Transient, exception.Kind);
        Assert.Equal("publish-stimulus-start-shed", exception.Code);
        Assert.Single(router.Sources);
    }

    /// <summary>
    /// A routed start or resume whose checkpoint breaks a checkpoint rule fails the same way on every attempt, so the
    /// delivery is permanent, including when the drain hands the violation back wrapped with its observers' failures.
    /// </summary>
    [Fact]
    public async Task Handler_ClassifiesCheckpointRuleViolation_AsPermanentDeliveryFailure()
    {
        var violation = new RuntimeCheckpointCommitValidationException("checkpoint rule violated");
        var routed = new AggregateException(violation);

        var exception = await HandleRoutingFailureAsync(routed);

        Assert.Equal(PostCommitFailureKind.Permanent, exception.Kind);
        Assert.Same(routed, exception.InnerException);
    }

    private static async Task<RuntimePostCommitDeliveryException> HandleRoutingFailureAsync(Exception failure)
    {
        var buffer = new PublishStimulusStagingBuffer(new FakeTimeProvider(Now));
        buffer.StagePublishStimulus(new PublishStimulusRequest(WorkflowExecutionId, ActivityExecutionId, "e"));
        var intent = Assert.Single(buffer.TakePublishStimuli(WorkflowExecutionId, ActivityExecutionId));

        return await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(async () =>
            await ((IRuntimePostCommitDurableSourceIntentHandler)new PublishStimulusExecutor(new ThrowingStimulusRouter(failure))).HandleAsync(ConstructedSource(intent)));
    }

    private static PostCommitDeliveryContext ConstructedSource(RuntimePostCommitIntent intent)
    {
        var item = new RuntimePostCommitOutboxItem("commit:" + intent.IntentId, intent, RuntimePostCommitOutboxStatus.Delivering,
            Now, Now, RuntimePostCommitRetryPolicy.None, 0, "owner", Now, null, null, null, 1, Now.AddMinutes(1), 1, new("tenant"), null);
        return new(new(item, "owner", 1, Now, Now.AddMinutes(1)), new("tenant"));
    }

    private static async ValueTask<IActivityCompletionTransition> ExecuteAsync(PublishEvent activity)
    {
        var context = new SimpleActivityExecutionContext(
            activity,
            CancellationToken.None,
            workflowExecutionId: WorkflowExecutionId,
            invocationId: ActivityExecutionId,
            executableNodeId: "node-publish");
        var transition = await ((IActivity)activity).ExecuteAsync(context.ToActivityExecutionContext());
        return Assert.IsAssignableFrom<IActivityCompletionTransition>(transition);
    }

    private static string ReadEventName(RuntimePostCommitIntent intent) => ReadPayload(intent).EventName;

    private static PublishStimulusIntentPayload ReadPayload(RuntimePostCommitIntent intent) =>
        intent.Payload!.Value.Deserialize<PublishStimulusIntentPayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private sealed class RecordingPublishStimulusStager : IPublishStimulusStager
    {
        public List<PublishStimulusRequest> Requests { get; } = [];
        public void StagePublishStimulus(PublishStimulusRequest request) => Requests.Add(request);
    }

    private sealed class SavedResultRouter(params StimulusStartOutcome[] starts) : IStimulusRouter
    {
        public List<PostCommitDeliveryContext> Sources { get; } = [];
        public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Old routing must never handle a saved send.");
        public ValueTask<StimulusRoutingResult> RouteSourceAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
        { Sources.Add(source); return ValueTask.FromResult(new StimulusRoutingResult(starts, [])); }
    }

    private sealed class ThrowingStimulusRouter(Exception failure) : IStimulusRouter
    {
        public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Old routing must never handle a saved send.");
        public ValueTask<StimulusRoutingResult> RouteSourceAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default) => throw failure;
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
