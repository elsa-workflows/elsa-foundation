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
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Durable-first coverage for the <see cref="PublishEvent"/> send surface (spec 135): the activity stages a publish
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
    public async Task Handler_RoutesStartAndResume_WithCarriedValues()
    {
        var router = new RecordingStimulusRouter();
        var buffer = new PublishStimulusStagingBuffer(new FakeTimeProvider(Now));
        var payload = JsonSerializer.SerializeToElement(new { orderId = 7 });
        buffer.StagePublishStimulus(new PublishStimulusRequest(WorkflowExecutionId, ActivityExecutionId, "order.placed", "corr-1", payload));
        var intent = Assert.Single(buffer.TakePublishStimuli(WorkflowExecutionId, ActivityExecutionId));

        await new PublishStimulusExecutor(router).HandleAsync(intent);

        var dispatch = Assert.Single(router.Requests);
        Assert.Equal(StimulusRoutingMode.StartAndResume, dispatch.Mode);
        Assert.Equal(EventStimulus.StimulusType, dispatch.StimulusType);
        Assert.Equal(EventStimulus.Hash("order.placed"), dispatch.StimulusHash);
        Assert.Equal("corr-1", dispatch.CorrelationId);
        Assert.Equal(payload.GetRawText(), dispatch.Input!.Value.GetRawText());
        Assert.Equal(intent.IdempotencyKey, dispatch.IdempotencyKey);
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

    /// <summary>
    /// A local publish must not wake sibling instances or fire a published message start (#1117): the handler
    /// routes ResumeOnly and pins the target to the publishing execution — taken from the intent (the runtime's
    /// own record of who committed the send), never from the activity-authored payload.
    /// </summary>
    [Fact]
    public async Task Handler_RoutesLocalEvent_ResumeOnly_TargetedAtThePublishingExecution()
    {
        var router = new RecordingStimulusRouter();
        var buffer = new PublishStimulusStagingBuffer(new FakeTimeProvider(Now));
        buffer.StagePublishStimulus(new PublishStimulusRequest(WorkflowExecutionId, ActivityExecutionId, "order.placed", isLocalEvent: true));
        var intent = Assert.Single(buffer.TakePublishStimuli(WorkflowExecutionId, ActivityExecutionId));

        await new PublishStimulusExecutor(router).HandleAsync(intent);

        var dispatch = Assert.Single(router.Requests);
        Assert.Equal(StimulusRoutingMode.ResumeOnly, dispatch.Mode);
        Assert.Equal(WorkflowExecutionId, dispatch.TargetWorkflowExecutionId);
    }

    [Fact]
    public async Task Handler_LeavesBroadcastPublishUntargeted()
    {
        var router = new RecordingStimulusRouter();
        var buffer = new PublishStimulusStagingBuffer(new FakeTimeProvider(Now));
        buffer.StagePublishStimulus(new PublishStimulusRequest(WorkflowExecutionId, ActivityExecutionId, "order.placed"));
        var intent = Assert.Single(buffer.TakePublishStimuli(WorkflowExecutionId, ActivityExecutionId));

        await new PublishStimulusExecutor(router).HandleAsync(intent);

        var dispatch = Assert.Single(router.Requests);
        Assert.Equal(StimulusRoutingMode.StartAndResume, dispatch.Mode);
        Assert.Null(dispatch.TargetWorkflowExecutionId);
    }

    [Fact]
    public async Task Handler_WrapsRouterFailure_AsTransientDeliveryFailure()
    {
        var buffer = new PublishStimulusStagingBuffer(new FakeTimeProvider(Now));
        buffer.StagePublishStimulus(new PublishStimulusRequest(WorkflowExecutionId, ActivityExecutionId, "e"));
        var intent = Assert.Single(buffer.TakePublishStimuli(WorkflowExecutionId, ActivityExecutionId));
        var router = new ThrowingStimulusRouter();

        var exception = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(async () => await new PublishStimulusExecutor(router).HandleAsync(intent));

        Assert.Equal(PostCommitFailureKind.Transient, exception.Kind);
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

    private sealed class ThrowingStimulusRouter : IStimulusRouter
    {
        public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
