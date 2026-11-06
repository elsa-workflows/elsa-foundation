using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.Http.IntegrationTests;

public sealed partial class LongDrainCancellationTests
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

    [Theory]
    [InlineData(PersistenceMode.Durable, false, false)]
    [InlineData(PersistenceMode.Balanced, false, false)]
    [InlineData(PersistenceMode.Ephemeral, false, false)]
    [InlineData(PersistenceMode.Durable, true, false)]
    [InlineData(PersistenceMode.Balanced, true, false)]
    [InlineData(PersistenceMode.Ephemeral, true, false)]
    [InlineData(PersistenceMode.Durable, false, true)]
    [InlineData(PersistenceMode.Balanced, false, true)]
    [InlineData(PersistenceMode.Ephemeral, false, true)]
    [InlineData(PersistenceMode.Durable, true, true)]
    [InlineData(PersistenceMode.Balanced, true, true)]
    [InlineData(PersistenceMode.Ephemeral, true, true)]
    public async Task A_drain_that_yielded_to_a_cancel_that_did_not_run_continues_without_a_sweep(PersistenceMode mode, bool replaySafe, bool duplicate)
    {
        var probe = new LoopProbe { Continued = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var withdraw = new WithdrawCancelOnYield();
        var actorWarnings = new ActorWarnings();
        await using var fixture = await HttpEndpointHostFixture.StartDurableSqliteAsync(mode, 50, services =>
        {
            ReferenceWorkflowRuns.ComposeHost(services);
            services.AddSingleton(probe);
            services.AddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
            services.AddSingleton<IWorkflowSchedulerDrainObserver>(withdraw);
            services.AddSingleton<ILoggerProvider>(actorWarnings);
        });
        var body = ReferenceWorkflows.Clr("body", replaySafe ? typeof(ReplaySafeBody) : typeof(ProgressingBody), []);
        var loop = ReferenceWorkflows.Clr("loop", typeof(WhileActivity), [("Condition", true)],
            childSlots: [new ExecutableChildSlot(WhileActivity.BodySlotName, [body])],
            structure: new ExecutableActivityStructure(WhileActivity.StructureKind, WhileActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = "body" })));
        var executable = ReferenceWorkflows.Executable("unconsumed-yield", loop);
        await fixture.PublishAsync(executable);
        const string executionId = "unconsumed-yield";
        const string startKey = "unconsumed-yield-start";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var start = fixture.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(executable.Identity.ArtifactId, "test", workflowExecutionId: executionId,
                idempotencyKey: duplicate ? startKey : null), cancellationToken: timeout.Token).AsTask();
        try
        {
            await probe.Entered.Task.WaitAsync(timeout.Token);
            var actor = await fixture.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(
                new(executionId, WorkflowExecutionActorActivationReason.SchedulerWork, DateTimeOffset.UtcNow, "test",
                    WorkflowExecutionActorCapabilities.InProcessMailbox), timeout.Token);

            if (duplicate)
            {
                // The drain yields to this Cancel. It reuses the start's idempotency key, which the start's turn records
                // as it ends, so the Cancel answers Duplicate without running.
                var duplicated = actor.EnqueueAsync(NewCancel(executionId, startKey), timeout.Token).AsTask();
                probe.Release.TrySetResult();
                Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, (await duplicated.WaitAsync(timeout.Token)).Status);
            }
            else
            {
                // The drain yields to this Cancel, and the observer withdraws it before it reaches the mailbox.
                using var waiter = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                withdraw.Waiter = waiter;
                var withdrawn = actor.EnqueueAsync(NewCancel(executionId, "withdrawn"), waiter.Token).AsTask();
                probe.Release.TrySetResult();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => withdrawn);
            }
            await start.WaitAsync(timeout.Token);

            // No sweep runs: the loop body is entered again only because the yielded drain was re-driven.
            await probe.Continued.Task.WaitAsync(TimeSpan.FromSeconds(5), timeout.Token);
            Assert.Contains(withdraw.Results, result => result.StopReason == RuntimeSchedulerDrainStopReason.Yielded);

            var cancel = actor.EnqueueAsync(NewCancel(executionId, "cancel"), timeout.Token).AsTask();
            probe.Stop.TrySetResult();
            Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await cancel.WaitAsync(timeout.Token)).Status);
            Assert.Equal(WorkflowExecutionStatus.Cancelled, (await fixture.WorkflowExecutionAsync(executionId)).Status);
            Assert.False(probe.SafetyLimitReached);
            Assert.Equal(2, probe.Calls);

            await using var scope = fixture.Services.CreateAsyncScope();
            var resumption = scope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>();
            var sweep = await resumption.SweepAsync(new(maxExecutionsPerSweep: 10), timeout.Token);
            Assert.Equal(0, sweep.OutboxFailedCount);
            Assert.All(sweep.Dispatches, dispatch => Assert.True(dispatch.Outcome is RuntimeResumptionDispatchOutcome.Accepted or RuntimeResumptionDispatchOutcome.Duplicate));
            await ReferenceWorkflowRuns.AssertNothingLeftToDrainAsync(fixture);
            Assert.Equal(WorkflowExecutionStatus.Cancelled, (await fixture.WorkflowExecutionAsync(executionId)).Status);
            Assert.Equal(2, probe.Calls);
            // The re-drive's turn ended cleanly when its drain yielded to the real Cancel: a re-drive that fails is logged.
            Assert.Empty(actorWarnings.Messages);
        }
        finally
        {
            probe.Release.TrySetResult();
            probe.Stop.TrySetResult();
            await timeout.CancelAsync();
            try { await start; } catch (OperationCanceledException) { }
        }
    }

    private static WorkflowExecutionCommandEnvelope NewCancel(string executionId, string key)
    {
        var now = DateTimeOffset.UtcNow;
        return new($"{key}-envelope", executionId,
            new(key, executionId, WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>()),
            key, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now);
    }

    public sealed class LoopProbe
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // When set, the second body call reports itself and holds until Stop.
        public TaskCompletionSource? Continued { get; init; }
        public TaskCompletionSource Stop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public bool SafetyLimitReached;
    }

    [ActivitySideEffectProfile(SideEffectProfile.External)]
    public class ProgressingBody(LoopProbe probe) : Activity<ActivityUnit>
    {
        protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            var call = Interlocked.Increment(ref probe.Calls);
            if (call > 32)
            {
                probe.SafetyLimitReached = true;
                throw new InvalidOperationException("The test's runaway guard was reached before cancellation took effect.");
            }
            if (call > 1 && probe.Continued is { } continued)
            {
                continued.TrySetResult();
                await probe.Stop.Task.WaitAsync(context.CancellationToken);
                return ActivityTransition.Complete(ActivityUnit.Value);
            }
            probe.Entered.TrySetResult();
            await probe.Release.Task.WaitAsync(context.CancellationToken);
            return ActivityTransition.Complete(ActivityUnit.Value);
        }
    }

    [ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
    public sealed class ReplaySafeBody(LoopProbe probe) : ProgressingBody(probe);

    // Cancels the waiting Cancel's token from inside the yielded turn, before the mailbox is released to it. It does so
    // once: the test disposes that token afterwards, and the re-drive's drain later yields to the real Cancel.
    private sealed class WithdrawCancelOnYield : IWorkflowSchedulerDrainObserver
    {
        private CancellationTokenSource? waiter;
        public CancellationTokenSource Waiter { set => waiter = value; }
        public List<RuntimeSchedulerDrainResult> Results { get; } = [];
        public async ValueTask OnDrainedAsync(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerDrainResult result, CancellationToken cancellationToken = default)
        {
            lock (Results)
                Results.Add(result);
            if (result.StopReason == RuntimeSchedulerDrainStopReason.Yielded && Interlocked.Exchange(ref waiter, null) is { } withdrawn)
                await withdrawn.CancelAsync();
        }
    }

    // Records what the in-process actor logs at Warning or above, such as a re-drive that failed.
    private sealed class ActorWarnings : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) =>
            categoryName == typeof(InProcessWorkflowExecutionActorProvider).FullName ? new Logger(Messages) : NullLogger.Instance;
        public void Dispose() { }

        private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    messages.Enqueue($"{formatter(state, exception)} {exception}");
            }
        }
    }

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
