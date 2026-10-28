using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

/// <summary>Actual in-process caller/inbox controls; fresh EF recovery and BPMN interpretation remain later gates.</summary>
public sealed class WorkflowControlInboxTests
{
    private const string NotificationKind = "Elsa.Activities.DispatchWorkflow.NotifyControl";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Nonterminal_delivery_commits_acceptance_with_deferred_consumer_and_retry_cannot_repeat_it()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(authoredNotificationChild: true);
        var run = await fixture.StartParentAsync("running-inbox", "running-inbox-parent", "correlation",
            waitForCompletion: true, controlConsumerVersion: 1);
        await fixture.SweepAsync();
        var intent = Assert.Single(CheckpointCommitIntents.IntentsOf(fixture.Checkpoints.Commits), item => item.Kind == NotificationKind);
        await using var scope = fixture.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
            .ProcessAsync(new RuntimePostCommitOutboxProcessRequest(1, intentKind: NotificationKind));
        Assert.Equal(1, result.DeliveredCount);
        var control = intent.Payload!.Value.Deserialize<RuntimeChildControlTransport>(Json)!;
        var child = await CallAsync(fixture, run.Dispatch);
        Assert.Equal(ActivityExecutionStatus.Suspended, child.Status);
        Assert.Equal(1, child.ChildControlInbox!.NextOrdinal);
        Assert.False(child.ChildControlInbox.IsTerminalConsumed);
        var parent = await ConsumerAsync(fixture, control);
        Assert.Equal(ActivityExecutionStatus.Running, parent.Status);
        Assert.Equal(1, Count(parent));
        AssertAtomicDecision(fixture, child, parent);
        var stateBefore = JsonSerializer.Serialize(parent, Json);
        var childBefore = JsonSerializer.Serialize(child, Json);
        await Handler(scope.ServiceProvider, NotificationKind).HandleAsync(intent);
        Assert.Equal(stateBefore, JsonSerializer.Serialize(await ConsumerAsync(fixture, control), Json));
        Assert.Equal(childBefore, JsonSerializer.Serialize(await CallAsync(fixture, run.Dispatch), Json));
        Assert.Single(fixture.Actors.Envelopes, envelope => envelope.Command.Kind == WorkflowExecutionCommandKind.DeliverChildControl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Terminal_barrier_delivered_first_buffers_without_callback_then_releases_one_ordered_prefix(bool ordinaryFault)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(authoredNotificationChild: true,
            terminalAfterNotification: true, ordinaryFaultAfterNotification: ordinaryFault, configureServices: EnableCoherentDeletion);
        var run = await fixture.StartParentAsync("terminal-gap-" + ordinaryFault, "terminal-gap-parent-" + ordinaryFault, "correlation",
            waitForCompletion: true, controlConsumerVersion: 1, controlResponse: "defer");
        await fixture.SweepAsync();
        var notification = Assert.Single(CheckpointCommitIntents.IntentsOf(fixture.Checkpoints.Commits), item => item.Kind == NotificationKind);
        var terminal = Assert.Single(CheckpointCommitIntents.IntentsOf(fixture.Checkpoints.Commits), item => item.Kind == DispatchWorkflowConstants.ResumeParentIntentKind);
        var payload = terminal.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(Json)!;
        var barrier = payload.Result.Control!;
        Assert.Equal(1, barrier.Batch.Ordinal);
        Assert.True(barrier.Batch.IsTerminal);
        Assert.Empty(barrier.Batch.Notifications);
        Assert.Null(barrier.Batch.AuthoredFailureCode);
        Assert.Equal(ordinaryFault ? WorkflowDispatchStatus.Faulted : WorkflowDispatchStatus.Completed, payload.Result.Status);
        await using var scope = fixture.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        var first = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(1, intentKind: DispatchWorkflowConstants.ResumeParentIntentKind));
        Assert.Equal(1, first.DeliveredCount);
        var buffered = await CallAsync(fixture, run.Dispatch);
        var waiting = await ConsumerAsync(fixture, barrier);
        Assert.Equal(ActivityExecutionStatus.Completed, buffered.Status);
        Assert.Equal(0, buffered.ChildControlInbox!.NextOrdinal);
        Assert.Equal(1, Assert.Single(buffered.ChildControlInbox.Pending).Batch.Ordinal);
        Assert.False(buffered.ChildControlInbox.IsTerminalConsumed);
        Assert.Equal(ActivityExecutionStatus.Running, waiting.Status);
        Assert.Null(waiting.PrivateState);
        Assert.True(buffered.Metadata.ContainsKey(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId));
        AssertAtomicDecision(fixture, buffered, waiting);
        var deletion = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionDeletionStore>();
        Assert.True(deletion.IsSupported);
        var candidate = (await deletion.ReadAsync(run.Dispatch.ChildWorkflowExecutionId))!;
        Assert.Equal(WorkflowExecutionDeletionResult.DependencyRetained, await deletion.TryDeleteTerminalAsync(candidate));
        Assert.NotNull(await fixture.FindWorkflowAsync(run.Dispatch.ChildWorkflowExecutionId));
        var roundTrip = JsonSerializer.Deserialize<ActivityExecutionState>(JsonSerializer.Serialize(buffered, Json), Json)!;
        Assert.Equal(0, roundTrip.ChildControlInbox!.NextOrdinal);
        Assert.Equal(1, Assert.Single(roundTrip.ChildControlInbox.Pending).Batch.Ordinal);

        var second = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(1, intentKind: NotificationKind));
        Assert.Equal(1, second.DeliveredCount);
        var consumed = await CallAsync(fixture, run.Dispatch);
        var finished = await ConsumerAsync(fixture, barrier);
        Assert.True(consumed.ChildControlInbox!.IsTerminalConsumed);
        Assert.Equal(2, consumed.ChildControlInbox.NextOrdinal);
        Assert.Empty(consumed.ChildControlInbox.Pending);
        Assert.Equal(ActivityExecutionStatus.Running, finished.Status);
        Assert.Equal(1, Count(finished));
        var data = finished.PrivateState!.Value.InlineValue!.Value;
        Assert.Equal([0L, 1L], data.GetProperty("ordinals").EnumerateArray().Select(item => item.GetInt64()));
        Assert.Equal(["first", "second"], data.GetProperty("codes").EnumerateArray().Select(item => item.GetString()));
        AssertAtomicDecision(fixture, consumed, finished);
        Assert.Equal(ordinaryFault ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Completed,
            (await fixture.FindWorkflowAsync(run.Dispatch.ChildWorkflowExecutionId))!.Status);
        if (ordinaryFault)
            Assert.True(Assert.Single(await fixture.Services.GetRequiredService<IIncidentStateStore>().ListAsync(run.Dispatch.ChildWorkflowExecutionId)).IsBlocking);
        var prior = JsonSerializer.Serialize(finished, Json);
        await Handler(scope.ServiceProvider, NotificationKind).HandleAsync(notification);
        await Handler(scope.ServiceProvider, DispatchWorkflowConstants.ResumeParentIntentKind).HandleAsync(terminal);
        Assert.Equal(prior, JsonSerializer.Serialize(await ConsumerAsync(fixture, barrier), Json));
    }

    [Theory]
    [InlineData("defer", ActivityExecutionStatus.Running)]
    [InlineData("fault", ActivityExecutionStatus.Faulted)]
    [InlineData("cancel", ActivityExecutionStatus.Cancelled)]
    public async Task Returned_consumer_decision_commits_the_cursor_private_state_and_owed_work_together(string response, ActivityExecutionStatus status)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync("decision-" + response, "decision-parent-" + response, "correlation",
            waitForCompletion: true, controlConsumerVersion: 1, controlResponse: response);
        await fixture.SweepAsync();
        await fixture.SweepAsync();
        var child = await CallAsync(fixture, run.Dispatch);
        Assert.True(child.ChildControlInbox!.IsTerminalConsumed);
        Assert.Equal(1, child.ChildControlInbox.NextOrdinal);
        Assert.Equal(ActivityExecutionStatus.Completed, child.Status);
        var root = Assert.Single(await fixture.ListActivitiesAsync(run.Dispatch.ParentWorkflowExecutionId), state => state.ParentActivityExecutionId is null);
        Assert.Equal(status, root.Status);
        Assert.Equal(1, Count(root));
        AssertAtomicDecision(fixture, child, root);
    }

    [Fact]
    public async Task Consuming_a_nonterminal_batch_can_cancel_the_same_call_without_overwriting_its_cancellation()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(authoredNotificationChild: true);
        var run = await fixture.StartParentAsync("cancel-call-inbox", "cancel-call-inbox-parent", "correlation",
            waitForCompletion: true, controlConsumerVersion: 1, controlResponse: "cancel-call");
        await fixture.SweepAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        var delivered = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(1, intentKind: NotificationKind));
        var diagnosis = await fixture.ListActivitiesAsync(run.Dispatch.ParentWorkflowExecutionId);
        var incidents = await fixture.Services.GetRequiredService<IIncidentStateStore>().ListAsync(run.Dispatch.ParentWorkflowExecutionId);
        Assert.True(delivered.DeliveredCount == 1, JsonSerializer.Serialize(new { delivered,
            states = diagnosis.Select(state => new { state.InvocationId, state.Status, state.ExecutionScopeId, inbox = state.ChildControlInbox?.NextOrdinal }),
            incidents = incidents.Select(incident => new { incident.FailureType, incident.Message }) }, Json));
        var child = await CallAsync(fixture, run.Dispatch);
        Assert.Equal(ActivityExecutionStatus.Cancelled, child.Status);
        Assert.Equal(1, child.ChildControlInbox!.NextOrdinal);
        Assert.False(child.ChildControlInbox.IsTerminalConsumed);
        var parent = (await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().FindAsync(run.Dispatch.ParentWorkflowExecutionId, child.ParentActivityExecutionId!))!;
        Assert.Equal(1, Count(parent));
        AssertAtomicDecision(fixture, child, parent);
        Assert.Contains(fixture.Checkpoints.Commits, commit =>
            commit.StateChanges.ActivityExecutions.Any(change => change.State.InvocationId == child.InvocationId && change.State.Status == ActivityExecutionStatus.Cancelled && change.State.ChildControlInbox?.NextOrdinal == 1) &&
            CheckpointCommitIntents.IntentsOf(commit).Any(intent => intent.Kind == DispatchWorkflowConstants.CancelChildIntentKind));
        var cancelled = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(1, intentKind: DispatchWorkflowConstants.CancelChildIntentKind));
        Assert.Equal(1, cancelled.DeliveredCount);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await fixture.FindWorkflowAsync(run.Dispatch.ChildWorkflowExecutionId))!.Status);
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.FindWorkflowAsync(run.Dispatch.ParentWorkflowExecutionId))!.Status);
    }

    [Fact]
    public async Task Retired_caller_tombstone_settles_late_source_retry_without_recreating_work()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(authoredNotificationChild: true,
            configureServices: EnableCoherentDeletion);
        var run = await fixture.StartParentAsync("retired-source", "retired-source-parent", "correlation",
            waitForCompletion: true, controlConsumerVersion: 1, controlResponse: "cancel");
        await fixture.SweepAsync();
        var intent = Assert.Single(CheckpointCommitIntents.IntentsOf(fixture.Checkpoints.Commits), item => item.Kind == NotificationKind);
        await using var scope = fixture.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(1, intentKind: NotificationKind));
        for (var attempt = 0; attempt < 4; attempt++) await fixture.SweepAsync();
        var workflow = (await fixture.FindWorkflowAsync(run.Dispatch.ParentWorkflowExecutionId))!;
        Assert.Equal(WorkflowExecutionStatus.Cancelled, workflow.Status);
        Assert.Equal(WorkflowExecutionStatus.Cancelled,
            (await fixture.FindWorkflowAsync(run.Dispatch.ChildWorkflowExecutionId))!.Status);
        var selectedSource = intent.Payload!.Value.Deserialize<RuntimeChildControlTransport>(Json)!.SourceOutboxItemId;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered,
            (await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>().FindAsync(selectedSource))!.Status);
        var deletion = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionDeletionStore>();
        Assert.True(deletion.IsSupported);
        var candidate = (await deletion.ReadAsync(workflow.WorkflowExecutionId))!;
        Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await deletion.TryDeleteTerminalAsync(candidate));
        var actors = fixture.Actors.Envelopes.Count;
        await scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>().DispatchAsync(intent);
        Assert.Equal(actors, fixture.Actors.Envelopes.Count);
        Assert.Null(await fixture.FindWorkflowAsync(workflow.WorkflowExecutionId));
        Assert.Empty(await fixture.ListActivitiesAsync(workflow.WorkflowExecutionId));
        Assert.True(await fixture.Services.GetRequiredService<IWorkflowExecutionStateStore>().IsDeletionTombstonedAsync(workflow.WorkflowExecutionId));
    }

    private static void EnableCoherentDeletion(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Scoped<IWorkflowExecutionDeletionStore>(provider =>
            // Keep checkpoint recording for the witness, but qualify deletion against the same raw provider
            // aggregate used by that recorder and the coalescing overlays, as production composition does.
            new InMemoryWorkflowExecutionDeletionStore(
                provider.GetRequiredService<InMemoryRuntimeCheckpointStoreState>(),
                provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>(),
                Underlying<IActivityExecutionInspectionStore>(provider),
                provider.GetRequiredService<IActivityExecutionHierarchyStore>(),
                Underlying<IWorkflowExecutionStateStore>(provider),
                provider.GetRequiredService<IExecutionLivenessStateStore>(),
                Underlying<IActivityExecutionStateStore>(provider),
                provider.GetRequiredService<IActivityExecutionInspectionWriter>(),
                provider.GetRequiredService<IActivityExecutionHierarchyWriter>(),
                provider.GetRequiredService<IBookmarkStateStore>(),
                Underlying<IDurableValueStateStore>(provider), Underlying<ISchedulerStateStore>(provider),
                provider.GetRequiredService<IDurableTimerStore>(), Underlying<IWorkflowSchedulerWorkQueue>(provider),
                provider.GetRequiredService<IIncidentStateStore>(), provider.GetRequiredService<IWorkflowSchedulerPoisonStore>(),
                provider.GetRequiredService<IWorkflowHoldStateStore>())));

    private static T Underlying<T>(IServiceProvider provider) where T : class =>
        provider.GetService<CoalescingInner<T>>()?.Value ?? provider.GetRequiredService<T>();

    [Fact]
    public async Task A_valid_terminal_source_read_outage_keeps_the_bookmark_and_barrier_owed_until_retry()
    {
        var outage = new OneShotSourceLookup();
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(configureServices: services =>
            services.Replace(ServiceDescriptor.Scoped<IPostCommitOutboxLookupStore>(provider =>
            {
                outage.Inner = provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
                return outage;
            })));
        var run = await fixture.StartParentAsync("source-outage", "source-outage-parent", "correlation",
            waitForCompletion: true, controlConsumerVersion: 1);
        await fixture.SweepAsync();
        var intent = Assert.Single(CheckpointCommitIntents.IntentsOf(fixture.Checkpoints.Commits), item => item.Kind == DispatchWorkflowConstants.ResumeParentIntentKind);
        var message = intent.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(Json)!;
        var before = await CallAsync(fixture, run.Dispatch);
        outage.Arm(message.Result.Control!.SourceOutboxItemId);
        await using var scope = fixture.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        var first = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(1, intentKind: DispatchWorkflowConstants.ResumeParentIntentKind));
        Assert.Equal(0, first.DeliveredCount);
        var held = await CallAsync(fixture, run.Dispatch);
        Assert.Equal(ActivityExecutionStatus.Suspended, held.Status);
        Assert.Equal(JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(held, Json));
        Assert.NotNull(await fixture.FindBookmarkAsync(message.ParentWorkflowExecutionId, message.BookmarkId));
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.FindWorkflowAsync(message.ParentWorkflowExecutionId))!.Status);
        Assert.Empty(await fixture.Services.GetRequiredService<IIncidentStateStore>().ListAsync(message.ParentWorkflowExecutionId));
        Assert.NotEqual(RuntimePostCommitOutboxStatus.Delivered, (await outage.Inner.FindAsync(message.Result.Control.SourceOutboxItemId))!.Status);
        var clock = DispatchWorkflowRuntimeTestFixture.Now;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var owed = (await outage.Inner.FindAsync(message.Result.Control.SourceOutboxItemId))!;
            if (owed.Status == RuntimePostCommitOutboxStatus.Delivered) break;
            var next = owed.AvailableAt is { } available && available > clock ? available : clock;
            fixture.AdvanceTime(next - clock + TimeSpan.FromSeconds(2));
            clock = next.AddSeconds(2);
            await fixture.SweepAsync();
        }
        var recovered = await CallAsync(fixture, run.Dispatch);
        Assert.Equal(ActivityExecutionStatus.Completed, recovered.Status);
        Assert.Null(recovered.Fault);
        Assert.True(recovered.ChildControlInbox!.IsTerminalConsumed);
        Assert.Equal(1, recovered.ChildControlInbox.NextOrdinal);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await outage.Inner.FindAsync(message.Result.Control.SourceOutboxItemId))!.Status);
        Assert.Empty(await fixture.Services.GetRequiredService<IIncidentStateStore>().ListAsync(message.ParentWorkflowExecutionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lost_reply_after_buffered_or_consumed_acceptance_retries_without_repeating_the_consumer(bool terminalFirst)
    {
        var reply = new LostReplyState(terminalFirst ? WorkflowExecutionCommandKind.ResumeBookmark : WorkflowExecutionCommandKind.DeliverChildControl);
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(authoredNotificationChild: true,
            terminalAfterNotification: terminalFirst, configureServices: services =>
                services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutionActorProvider>(provider =>
                    new LostReplyActorProvider(provider.GetRequiredService<RecordingWorkflowExecutionActorProvider>(), reply))));
        var run = await fixture.StartParentAsync("lost-control-reply-" + terminalFirst, "lost-control-reply-parent-" + terminalFirst, "correlation",
            waitForCompletion: true, controlConsumerVersion: 1, controlResponse: "defer");
        await fixture.SweepAsync();
        var kind = terminalFirst ? DispatchWorkflowConstants.ResumeParentIntentKind : NotificationKind;
        var intent = Assert.Single(CheckpointCommitIntents.IntentsOf(fixture.Checkpoints.Commits), item => item.Kind == kind);
        var control = terminalFirst ? intent.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(Json)!.Result.Control! :
            intent.Payload!.Value.Deserialize<RuntimeChildControlTransport>(Json)!;
        await using var scope = fixture.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        var first = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(1, intentKind: kind));
        Assert.Equal(0, first.DeliveredCount);
        Assert.True(reply.Fired);
        var accepted = await CallAsync(fixture, run.Dispatch);
        Assert.True(accepted.ChildControlInbox!.HasAccepted(control));
        var before = await ConsumerAsync(fixture, control);
        if (terminalFirst) Assert.Null(before.PrivateState);
        else Assert.Equal(1, Count(before));
        var source = (await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>().FindAsync(control.SourceOutboxItemId))!;
        Assert.NotEqual(RuntimePostCommitOutboxStatus.Delivered, source.Status);
        var advance = source.AvailableAt is { } at && at > DispatchWorkflowRuntimeTestFixture.Now ? at - DispatchWorkflowRuntimeTestFixture.Now : TimeSpan.Zero;
        fixture.AdvanceTime(advance + TimeSpan.FromSeconds(2));
        var retry = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(1, intentKind: kind));
        Assert.Equal(1, retry.DeliveredCount);
        Assert.Equal(JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(await ConsumerAsync(fixture, control), Json));
        Assert.Equal(JsonSerializer.Serialize(accepted.ChildControlInbox, Json), JsonSerializer.Serialize((await CallAsync(fixture, run.Dispatch)).ChildControlInbox, Json));
        if (terminalFirst)
        {
            Assert.Equal(1, (await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(1, intentKind: NotificationKind))).DeliveredCount);
            var complete = await CallAsync(fixture, run.Dispatch);
            Assert.True(complete.ChildControlInbox!.IsTerminalConsumed);
            Assert.Equal(2, complete.ChildControlInbox.NextOrdinal);
            Assert.Equal(1, Count(await ConsumerAsync(fixture, control)));
        }
    }

    private sealed class LostReplyState(WorkflowExecutionCommandKind kind)
    {
        private int _fired;
        internal bool Fired => Volatile.Read(ref _fired) != 0;
        internal bool ShouldLose(WorkflowExecutionCommandEnvelope envelope) => envelope.Command.Kind == kind && Interlocked.CompareExchange(ref _fired, 1, 0) == 0;
    }

    private sealed class LostReplyActorProvider(IWorkflowExecutionActorProvider inner, LostReplyState reply) : IWorkflowExecutionActorProvider
    {
        public WorkflowExecutionActorCapabilities Capabilities => inner.Capabilities;
        public async ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default) =>
            new LostReplyActor(await inner.GetAgentAsync(request, cancellationToken), reply);
        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) =>
            inner.PassivateAsync(request, cancellationToken);
    }

    private sealed class LostReplyActor(IWorkflowExecutionActor inner, LostReplyState reply) : IWorkflowExecutionActor
    {
        public WorkflowExecutionActorDescriptor Descriptor => inner.Descriptor;
        public async ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            var result = await inner.EnqueueAsync(envelope, cancellationToken);
            if (reply.ShouldLose(envelope)) throw new IOException("Injected reply loss after actual actor acceptance.");
            return result;
        }
        public async ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope,
            WorkflowExecutionCommandDispatchOptions options, CancellationToken cancellationToken = default)
        {
            var result = await inner.EnqueueAsync(envelope, options, cancellationToken);
            if (reply.ShouldLose(envelope)) throw new IOException("Injected reply loss after actual actor acceptance.");
            return result;
        }
    }

    private sealed class OneShotSourceLookup : IPostCommitOutboxLookupStore
    {
        internal IPostCommitOutboxLookupStore Inner { get; set; } = null!;
        private string? _source;
        private int _remaining;
        internal void Arm(string source) { _source = source; _remaining = 1; }
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string outboxItemId, CancellationToken cancellationToken = default)
        {
            if (outboxItemId == _source && Interlocked.CompareExchange(ref _remaining, 0, 1) == 1)
                return ValueTask.FromException<RuntimePostCommitOutboxItem?>(new IOException("Injected one-shot source read failure."));
            return Inner.FindAsync(outboxItemId, cancellationToken);
        }
    }

    private static IRuntimePostCommitIntentHandler Handler(IServiceProvider services, string kind) =>
        (IRuntimePostCommitIntentHandler)services.GetRequiredService(services.GetServices<RuntimePostCommitIntentHandlerContribution>()
            .Single(contribution => contribution.IntentKind == kind).HandlerType);
    private static async ValueTask<ActivityExecutionState> CallAsync(DispatchWorkflowRuntimeTestFixture fixture, WorkflowDispatchRecord dispatch) =>
        (await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().FindAsync(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId))!;
    private static async ValueTask<ActivityExecutionState> ConsumerAsync(DispatchWorkflowRuntimeTestFixture fixture, RuntimeChildControlTransport control) =>
        (await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().FindAsync(control.ParentWorkflowExecutionId, control.ConsumerActivityExecutionId))!;
    private static int Count(ActivityExecutionState parent) => parent.PrivateState!.Value.InlineValue!.Value.GetProperty("count").GetInt32();
    private static void AssertAtomicDecision(DispatchWorkflowRuntimeTestFixture fixture, ActivityExecutionState child, ActivityExecutionState parent) =>
        Assert.Contains(fixture.Checkpoints.Commits, commit =>
            commit.StateChanges.ActivityExecutions.Any(change => change.State.InvocationId == child.InvocationId &&
                JsonSerializer.Serialize(change.State.ChildControlInbox, Json) == JsonSerializer.Serialize(child.ChildControlInbox, Json)) &&
            commit.StateChanges.ActivityExecutions.Any(change => change.State.InvocationId == child.ParentActivityExecutionId && change.State.Status == parent.Status &&
                JsonSerializer.Serialize(change.State.PrivateState, Json) == JsonSerializer.Serialize(parent.PrivateState, Json)));
}
