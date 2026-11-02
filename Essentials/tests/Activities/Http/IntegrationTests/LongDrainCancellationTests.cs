using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.Http.IntegrationTests;

public sealed class LongDrainCancellationTests
{
    [Theory]
    [InlineData(PersistenceMode.Durable, false, false)]
    [InlineData(PersistenceMode.Durable, false, true)]
    [InlineData(PersistenceMode.Balanced, false, false)]
    [InlineData(PersistenceMode.Balanced, false, true)]
    [InlineData(PersistenceMode.Ephemeral, false, false)]
    [InlineData(PersistenceMode.Ephemeral, false, true)]
    [InlineData(PersistenceMode.Durable, true, false)]
    [InlineData(PersistenceMode.Durable, true, true)]
    [InlineData(PersistenceMode.Balanced, true, false)]
    [InlineData(PersistenceMode.Balanced, true, true)]
    [InlineData(PersistenceMode.Ephemeral, true, false)]
    [InlineData(PersistenceMode.Ephemeral, true, true)]
    public async Task Public_cancel_hands_off_a_progressing_unbounded_drain_at_a_checkpoint(PersistenceMode mode, bool replaySafe, bool duplicateCancel)
    {
        var probe = new LoopProbe();
        var drains = new ObservedDrains();
        await using var fixture = await HttpEndpointHostFixture.StartDurableSqliteAsync(mode, 50, services =>
        {
            ReferenceWorkflowRuns.ComposeHost(services);
            services.AddSingleton(probe);
            services.AddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
            services.AddSingleton<IWorkflowSchedulerDrainObserver>(drains);
        });
        var body = ReferenceWorkflows.Clr("body", replaySafe ? typeof(ReplaySafeBody) : typeof(ProgressingBody), []);
        var loop = ReferenceWorkflows.Clr("loop", typeof(WhileActivity), [("Condition", true)],
            childSlots: [new ExecutableChildSlot(WhileActivity.BodySlotName, [body])],
            structure: new ExecutableActivityStructure(WhileActivity.StructureKind, WhileActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = "body" })));
        var executable = ReferenceWorkflows.Executable("cancel-progress", loop);
        await fixture.PublishAsync(executable);
        const string executionId = "cancel-progress";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var start = fixture.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(executable.Identity.ArtifactId, "test", workflowExecutionId: executionId), cancellationToken: timeout.Token).AsTask();
        try
        {
            await probe.Entered.Task.WaitAsync(timeout.Token);
            var actors = fixture.Services.GetRequiredService<IWorkflowExecutionActorProvider>();
            var actor = await actors.GetAgentAsync(new(executionId, WorkflowExecutionActorActivationReason.SchedulerWork,
                DateTimeOffset.UtcNow, "test", WorkflowExecutionActorCapabilities.InProcessMailbox), timeout.Token);
            var now = DateTimeOffset.UtcNow;
            var envelope = new WorkflowExecutionCommandEnvelope("cancel-envelope", executionId,
                new("cancel", executionId, WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>()),
                "cancel", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now);
            var cancel = actor.EnqueueAsync(envelope, timeout.Token).AsTask();
            var duplicate = duplicateCancel ? actor.EnqueueAsync(envelope, timeout.Token).AsTask() : null;
            // EnqueueAsync publishes the control wait synchronously before waiting on the busy mailbox.
            probe.Release.TrySetResult();
            if (duplicate is null)
                Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await cancel.WaitAsync(timeout.Token)).Status);
            else
            {
                var results = await Task.WhenAll(cancel, duplicate).WaitAsync(timeout.Token);
                Assert.Single(results, result => result.Status == WorkflowExecutionCommandDispatchStatus.Accepted);
                Assert.Single(results, result => result.Status == WorkflowExecutionCommandDispatchStatus.Duplicate);
            }
            await start.WaitAsync(timeout.Token);
            Assert.False(probe.SafetyLimitReached);
            Assert.Equal(1, probe.Calls);
            Assert.Contains(drains.Results, result => result.StopReason == RuntimeSchedulerDrainStopReason.Yielded);
            Assert.Equal(WorkflowExecutionStatus.Cancelled, (await fixture.WorkflowExecutionAsync(executionId)).Status);

            await using var scope = fixture.Services.CreateAsyncScope();
            var states = await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(executionId);
            var completedBody = Assert.Single(states, state => state.Execution.ExecutableNodeId == "body");
            Assert.Equal(ActivityExecutionStatus.Completed, completedBody.Status);
            var before = states.Select(state => (state.Execution.ActivityExecutionId, state.Status)).OrderBy(state => state.ActivityExecutionId).ToArray();
            var resumption = scope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>();
            var sweep = await resumption.SweepAsync(new(maxExecutionsPerSweep: 10), timeout.Token);
            Assert.Equal(0, sweep.OutboxFailedCount);
            Assert.All(sweep.Dispatches, dispatch => Assert.True(dispatch.Outcome is RuntimeResumptionDispatchOutcome.Accepted or RuntimeResumptionDispatchOutcome.Duplicate));
            await ReferenceWorkflowRuns.AssertNothingLeftToDrainAsync(fixture);
            var settled = await resumption.SweepAsync(new(maxExecutionsPerSweep: 10), timeout.Token);
            Assert.False(settled.DidWork);
            Assert.Equal(0, settled.OutboxFailedCount);
            await ReferenceWorkflowRuns.AssertNothingLeftToDrainAsync(fixture);
            var after = await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(executionId);
            Assert.Equal(before, after.Select(state => (state.Execution.ActivityExecutionId, state.Status)).OrderBy(state => state.ActivityExecutionId).ToArray());
            Assert.Equal(WorkflowExecutionStatus.Cancelled, (await fixture.WorkflowExecutionAsync(executionId)).Status);
            Assert.Equal(1, probe.Calls);
        }
        finally
        {
            probe.Release.TrySetResult();
            await timeout.CancelAsync();
            try { await start; } catch (OperationCanceledException) { }
        }
    }

    public sealed class LoopProbe
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public bool SafetyLimitReached;
    }

    [ActivitySideEffectProfile(SideEffectProfile.External)]
    public class ProgressingBody(LoopProbe probe) : Activity<ActivityUnit>
    {
        protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            if (Interlocked.Increment(ref probe.Calls) > 32)
            {
                probe.SafetyLimitReached = true;
                throw new InvalidOperationException("The test's runaway guard was reached before cancellation took effect.");
            }
            probe.Entered.TrySetResult();
            await probe.Release.Task.WaitAsync(context.CancellationToken);
            return ActivityTransition.Complete(ActivityUnit.Value);
        }
    }

    [ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
    public sealed class ReplaySafeBody(LoopProbe probe) : ProgressingBody(probe);

    private sealed class ObservedDrains : IWorkflowSchedulerDrainObserver
    {
        public List<RuntimeSchedulerDrainResult> Results { get; } = [];
        public ValueTask OnDrainedAsync(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerDrainResult result, CancellationToken cancellationToken = default)
        {
            Results.Add(result);
            return ValueTask.CompletedTask;
        }
    }
}
