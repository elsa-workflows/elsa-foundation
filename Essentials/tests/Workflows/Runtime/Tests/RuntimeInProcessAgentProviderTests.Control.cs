using System.Collections.Concurrent;
using System.Threading.Channels;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeInProcessAgentProviderTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Only_a_live_valid_cancel_waiter_requests_its_own_mailbox_handoff(bool cancelWaiter, bool wrongPartition)
    {
        var controls = new DrainControlContext();
        var processor = new ObservingControlProcessor(controls);
        using var provider = new InProcessWorkflowExecutionActorProvider(processor) { DrainControls = controls };
        var agent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = agent.EnqueueAsync(NewEnvelope(1), timeout.Token).AsTask();
        await processor.Entered.Task.WaitAsync(timeout.Token);
        using var waiterToken = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var now = DateTimeOffset.UtcNow;
        var cancel = new WorkflowExecutionCommandEnvelope("cancel", "wfexec-1",
            new("cancel", "wfexec-1", WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>()),
            "cancel", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now,
            partition: wrongPartition ? new WorkflowExecutionPartition("other") : null);
        var waiting = agent.EnqueueAsync(cancel, waiterToken.Token).AsTask();
        if (cancelWaiter)
        {
            await waiterToken.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }
        processor.Inspect.TrySetResult();
        await running.WaitAsync(timeout.Token);
        if (!cancelWaiter)
            Assert.Equal(wrongPartition ? WorkflowExecutionCommandDispatchStatus.Rejected : WorkflowExecutionCommandDispatchStatus.Accepted,
                (await waiting.WaitAsync(timeout.Token)).Status);
        Assert.Equal(!cancelWaiter && !wrongPartition, processor.OwnYield);
        Assert.False(processor.OtherYield);
        Assert.False(processor.CancelYield);
        Assert.False(controls.ShouldYield("wfexec-1"));
    }

    [Fact]
    public async Task A_drain_that_yielded_to_a_cancelled_waiter_is_redriven_at_once()
    {
        using var drain = await StartYieldingDrainAsync();
        var (processor, agent, timeout) = (drain.Processor, drain.Agent, drain.Timeout);
        using var waiterToken = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var waiting = agent.EnqueueAsync(NewCancel("cancel"), waiterToken.Token).AsTask();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);

        await waiterToken.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        processor.Release.TrySetResult();

        var redrive = await processor.Redriven.Task.WaitAsync(timeout.Token);
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, redrive.Command.Kind);
        Assert.Equal("wfexec-1", redrive.WorkflowExecutionId);
        Assert.Equal(0, processor.CancelsProcessed);
    }

    [Fact]
    public async Task A_drain_that_yielded_to_a_duplicate_cancel_is_redriven_at_once()
    {
        using var drain = await StartYieldingDrainAsync(precedingCancelKey: "cancel");
        var (processor, agent, timeout) = (drain.Processor, drain.Agent, drain.Timeout);
        var duplicate = agent.EnqueueAsync(NewCancel("cancel"), timeout.Token).AsTask();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);
        processor.Release.TrySetResult();

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, (await duplicate.WaitAsync(timeout.Token)).Status);
        var redrive = await processor.Redriven.Task.WaitAsync(timeout.Token);
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, redrive.Command.Kind);
        Assert.Equal(1, processor.CancelsProcessed);
    }

    [Fact]
    public async Task A_drain_that_yielded_to_a_cancel_that_ran_is_not_redriven()
    {
        using var drain = await StartYieldingDrainAsync();
        var (processor, agent, timeout) = (drain.Processor, drain.Agent, drain.Timeout);
        processor.CancelGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancel = agent.EnqueueAsync(NewCancel("cancel"), timeout.Token).AsTask();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);
        using var queuedToken = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var queued = agent.EnqueueAsync(NewEnvelope(2), queuedToken.Token).AsTask();
        processor.Release.TrySetResult();
        await drain.Running.WaitAsync(timeout.Token);
        await processor.CancelEntered.Task.WaitAsync(timeout.Token);

        // A command queued behind the Cancel withdraws while the Cancel is inside its own turn, and checks for a stranded
        // yield on its way out. Only the Cancel's registration, held through its turn, tells it the yield is being
        // consumed; released when the Cancel entered the mailbox, that check would re-drive.
        await queuedToken.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        processor.CancelGate.TrySetResult();

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await cancel.WaitAsync(timeout.Token)).Status);
        Assert.Equal(1, processor.CancelsProcessed);
        await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
        Assert.False(processor.Redriven.Task.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_turn_other_than_a_cancel_keeps_the_yield_for_a_later_withdrawn_cancel(bool firstDrains)
    {
        using var drain = await StartYieldingDrainAsync();
        var (processor, agent, timeout) = (drain.Processor, drain.Agent, drain.Timeout);
        // Mailbox waiters are served in order: two commands, then the Cancel the drain yields to. The first either drains
        // nothing or runs a drain to quiescence. A real drain yields while that Cancel waits; one runs past it only when the
        // Cancel leaves just before, which is what lets a turn take the mailbox before the leaving waiter's own check.
        // That drain did not deliver the yielded segment's flushed continuations, so it must not settle the yield.
        processor.GatedKey = "wfexec-1:command-3";
        if (firstDrains)
            processor.DrainingKey = "wfexec-1:command-2";
        var first = agent.EnqueueAsync(NewEnvelope(2), timeout.Token).AsTask();
        var second = agent.EnqueueAsync(NewEnvelope(3), timeout.Token).AsTask();
        using var waiterToken = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var waiting = agent.EnqueueAsync(NewCancel("cancel"), waiterToken.Token).AsTask();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);
        processor.Release.TrySetResult();

        // The first command has completed; the Cancel withdraws while the second still holds the mailbox.
        await first.WaitAsync(timeout.Token);
        await processor.GatedEntered.Task.WaitAsync(timeout.Token);
        await waiterToken.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        processor.Gate.TrySetResult();
        await second.WaitAsync(timeout.Token);

        var redrive = await processor.Redriven.Task.WaitAsync(timeout.Token);
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, redrive.Command.Kind);
        Assert.Equal(0, processor.CancelsProcessed);
    }

    [Fact]
    public async Task A_disposed_provider_does_not_redrive_a_stranded_yield()
    {
        var logger = new SignallingLogger();
        using var drain = await StartYieldingDrainAsync(logger: logger);
        var (processor, agent, timeout) = (drain.Processor, drain.Agent, drain.Timeout);
        using var waiterToken = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var waiting = agent.EnqueueAsync(NewCancel("cancel"), waiterToken.Token).AsTask();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);
        await waiterToken.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        // The yield is stranded only when the yielded turn ends, after the provider was disposed.
        drain.Provider.Dispose();
        processor.Release.TrySetResult();
        await drain.Running.WaitAsync(timeout.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
        Assert.False(processor.Redriven.Task.IsCompleted);
        Assert.False(logger.Logged.Task.IsCompleted);
    }

    [Fact]
    public async Task Cancellation_after_mailbox_acquisition_does_not_execute_or_consume_the_command()
    {
        var processor = new AdmissionCommandProcessor();
        using var provider = new InProcessWorkflowExecutionActorProvider(processor);
        var agent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = agent.EnqueueAsync(NewEnvelope(1), timeout.Token).AsTask();
        await processor.Entered.Task.WaitAsync(timeout.Token);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using var continuations = new HeldAdmissionContext();
        Task<WorkflowExecutionCommandDispatchResult> waiting;
        var previous = SynchronizationContext.Current;
        try
        {
            // Capture only this caller's await continuation, without changing the actor or semaphore.
            SynchronizationContext.SetSynchronizationContext(continuations);
            waiting = agent.EnqueueAsync(NewEnvelope(2), cancelled.Token).AsTask();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        processor.Release.TrySetResult();
        await running.WaitAsync(timeout.Token);
        // The mailbox has granted entry, but its continuation has not entered the executor yet.
        await continuations.WaitForPostAsync(timeout.Token);
        Assert.Equal(new[] { "envelope-1" }, processor.Executed.ToArray());
        await cancelled.CancelAsync();
        await continuations.ResumeUntilCompletedAsync(waiting, timeout.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(new[] { "envelope-1" }, processor.Executed.ToArray());

        // Cancellation must release the mailbox and leave the unprocessed idempotency key reusable.
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted,
            (await agent.EnqueueAsync(NewEnvelope(2), timeout.Token)).Status);
        Assert.Equal(new[] { "envelope-1", "envelope-2" }, processor.Executed.ToArray());
    }

    [Fact]
    public async Task A_redrive_still_waiting_for_the_mailbox_stops_quietly_when_the_provider_is_disposed()
    {
        var logger = new SignallingLogger();
        using var drain = await StartYieldingDrainAsync(logger: logger);
        var (processor, agent, timeout) = (drain.Processor, drain.Agent, drain.Timeout);
        // A command that drains nothing holds the mailbox after the yielded turn, so the re-drive has to wait behind it.
        processor.GatedKey = "wfexec-1:command-2";
        var gated = agent.EnqueueAsync(NewEnvelope(2), timeout.Token).AsTask();
        using var waiterToken = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var waiting = agent.EnqueueAsync(NewCancel("cancel"), waiterToken.Token).AsTask();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);
        processor.Release.TrySetResult();
        await drain.Running.WaitAsync(timeout.Token);
        await processor.GatedEntered.Task.WaitAsync(timeout.Token);

        // The withdrawn Cancel strands the yield and queues the re-drive; the provider is disposed before it can run.
        await waiterToken.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(processor.Redriven.Task.IsCompleted);
        drain.Provider.Dispose();
        Assert.False(processor.Redriven.Task.IsCompleted);
        processor.Gate.TrySetResult();
        await gated.WaitAsync(timeout.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
        Assert.False(processor.Redriven.Task.IsCompleted);
        Assert.False(logger.Logged.Task.IsCompleted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_redrive_that_fails_or_is_not_accepted_is_logged_and_the_mailbox_keeps_working(bool throws)
    {
        var logger = new SignallingLogger();
        using var drain = await StartYieldingDrainAsync(logger: logger);
        var (processor, agent, timeout) = (drain.Processor, drain.Agent, drain.Timeout);
        var failure = new InvalidOperationException("The re-drive failed.");
        if (throws)
            processor.RedriveFailure = failure;
        else
            processor.RedriveResult = new(true, RuntimeSchedulerDrainStopReason.Faulted, true, false, "The re-drive faulted.");
        using var waiterToken = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var waiting = agent.EnqueueAsync(NewCancel("cancel"), waiterToken.Token).AsTask();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);
        await waiterToken.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        processor.Release.TrySetResult();

        var (level, exception, message) = await logger.Logged.Task.WaitAsync(timeout.Token);
        Assert.Equal(throws ? LogLevel.Warning : LogLevel.Debug, level);
        Assert.Same(throws ? failure : null, exception);
        Assert.Contains("wfexec-1", message);
        if (!throws)
            Assert.Contains(nameof(WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted), message);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted,
            (await agent.EnqueueAsync(NewEnvelope(2), timeout.Token).AsTask().WaitAsync(timeout.Token)).Status);
    }

    [Fact]
    public async Task Passivating_an_actor_whose_drain_keeps_running_makes_the_drain_yield_and_finishes()
    {
        using var drain = await StartYieldingDrainAsync();
        var (processor, agent, timeout) = (drain.Processor, drain.Agent, drain.Timeout);

        // The drain never ends its turn on its own; only a yield request does (#3678).
        var passivation = drain.Provider.PassivateAsync(NewPassivationRequest("wfexec-1"), timeout.Token).AsTask();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);
        processor.Release.TrySetResult();

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await drain.Running.WaitAsync(timeout.Token)).Status);
        await passivation.WaitAsync(timeout.Token);
        Assert.Equal(WorkflowExecutionActorStatus.Passivated, agent.Descriptor.Status);
        Assert.Equal(0, AgentCount(drain.Provider));

        // The yielded work is the next owner's or the resumption sweep's: a passivated mailbox does not re-drive it.
        await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
        Assert.False(processor.Redriven.Task.IsCompleted);
    }

    /// <summary>
    /// Each mailbox has its own yield signal. A second live mailbox, of another execution or of the same execution id in
    /// another partition, runs a drain while the first one passivates, and only the passivating mailbox's drain is asked
    /// to yield.
    /// </summary>
    [Theory]
    [InlineData("wfexec-2", null)]
    [InlineData("wfexec-1", "other")]
    public async Task A_passivation_asks_only_its_own_mailboxs_drain_to_yield(string otherExecutionId, string? otherPartition)
    {
        var controls = new DrainControlContext();
        var processor = new KeyedObservingProcessor(controls, drains: 2);
        using var provider = new InProcessWorkflowExecutionActorProvider(processor) { DrainControls = controls };
        var partition = new WorkflowExecutionPartition(otherPartition ?? WorkflowExecutionPartition.DefaultValue);
        var own = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        var other = await provider.GetAgentAsync(NewActivationRequest(otherExecutionId, partition: partition));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ownRunning = own.EnqueueAsync(NewEnvelope(1), timeout.Token).AsTask();
        var otherRunning = other.EnqueueAsync(NewEnvelope(2, otherExecutionId, partition: partition), timeout.Token).AsTask();
        await processor.Entered.Task.WaitAsync(timeout.Token);

        var passivation = provider.PassivateAsync(NewPassivationRequest("wfexec-1"), timeout.Token).AsTask();
        await WaitUntilStatusAsync(own, WorkflowExecutionActorStatus.Passivating);
        processor.Inspect.TrySetResult();
        await Task.WhenAll(ownRunning, otherRunning).WaitAsync(timeout.Token);
        await passivation.WaitAsync(timeout.Token);

        Assert.True(processor.Yielded[KeyedObservingProcessor.Key("wfexec-1", WorkflowExecutionPartition.DefaultValue)]);
        Assert.False(processor.Yielded[KeyedObservingProcessor.Key(otherExecutionId, partition.Value)]);
        Assert.Equal(WorkflowExecutionActorStatus.Passivated, own.Descriptor.Status);
        Assert.Equal(WorkflowExecutionActorStatus.Active, other.Descriptor.Status);
    }

    [Fact]
    public async Task A_cancel_that_arrives_while_the_actor_passivates_is_deferred_and_the_drain_still_yields()
    {
        using var drain = await StartYieldingDrainAsync();
        var (processor, agent, timeout) = (drain.Processor, drain.Agent, drain.Timeout);
        var passivation = drain.Provider.PassivateAsync(NewPassivationRequest("wfexec-1"), timeout.Token).AsTask();
        await WaitUntilStatusAsync(agent, WorkflowExecutionActorStatus.Passivating);

        // A Cancel on an actor whose passivation completes runs nothing here. Deferred, it stays with its sender, as any
        // deferred command does. Only a sender that resolved the actor before the passivation began gets here: resolving
        // it later waits for the passivation and returns a fresh actor.
        var cancel = agent.EnqueueAsync(NewCancel("cancel"), timeout.Token).AsTask();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);
        processor.Release.TrySetResult();

        await passivation.WaitAsync(timeout.Token);
        var deferred = await cancel.WaitAsync(timeout.Token);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, deferred.Status);
        Assert.Equal(0, processor.CancelsProcessed);
        await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
        Assert.False(processor.Redriven.Task.IsCompleted);
    }

    [Fact]
    public async Task A_drain_that_yielded_to_a_passivation_that_gave_up_is_redriven_and_the_actor_stays_active()
    {
        using var drain = await StartYieldingDrainAsync();
        var (processor, agent, timeout) = (drain.Processor, drain.Agent, drain.Timeout);
        using var passivationToken = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var passivation = drain.Provider.PassivateAsync(NewPassivationRequest("wfexec-1"), passivationToken.Token).AsTask();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);

        await passivationToken.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => passivation);
        processor.Release.TrySetResult();

        var redrive = await processor.Redriven.Task.WaitAsync(timeout.Token);
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, redrive.Command.Kind);
        Assert.Equal(WorkflowExecutionActorStatus.Active, agent.Descriptor.Status);
        Assert.Equal(0, processor.CancelsProcessed);
    }

    [Fact]
    public async Task A_cancel_that_arrived_while_a_passivation_gave_up_makes_the_drain_yield_and_runs()
    {
        // The drain is inside a step that outlasts the passivation, as when the stop path's one-lease bound cancels it.
        var step = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var drain = await StartYieldingDrainAsync(step: step);
        var (processor, agent, timeout) = (drain.Processor, drain.Agent, drain.Timeout);
        using var passivationToken = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var passivation = drain.Provider.PassivateAsync(NewPassivationRequest("wfexec-1"), passivationToken.Token).AsTask();
        await WaitUntilStatusAsync(agent, WorkflowExecutionActorStatus.Passivating);
        var cancel = agent.EnqueueAsync(NewCancel("cancel"), timeout.Token).AsTask();

        await passivationToken.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => passivation);
        Assert.Equal(WorkflowExecutionActorStatus.Active, agent.Descriptor.Status);
        Assert.False(processor.YieldObserved.Task.IsCompleted);

        // The step ends and the drain would go on; the Cancel's own signal makes it yield, and the Cancel runs.
        step.TrySetResult();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);
        processor.Release.TrySetResult();

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await drain.Running.WaitAsync(timeout.Token)).Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await cancel.WaitAsync(timeout.Token)).Status);
        Assert.Equal(1, processor.CancelsProcessed);
        Assert.Equal(WorkflowExecutionActorStatus.Active, agent.Descriptor.Status);

        // The Cancel consumed the yield, so nothing re-drives it.
        await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
        Assert.False(processor.Redriven.Task.IsCompleted);
    }

    private async Task<YieldingDrain> StartYieldingDrainAsync(string? precedingCancelKey = null, ILogger? logger = null, TaskCompletionSource? step = null)
    {
        var controls = new DrainControlContext();
        var processor = new YieldingControlProcessor(controls) { Step = step };
        var provider = new InProcessWorkflowExecutionActorProvider(processor) { DrainControls = controls, Logger = logger };
        var agent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        if (precedingCancelKey is not null)
            Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted,
                (await agent.EnqueueAsync(NewCancel(precedingCancelKey), timeout.Token)).Status);
        var running = agent.EnqueueAsync(NewEnvelope(1), timeout.Token).AsTask();
        await processor.Entered.Task.WaitAsync(timeout.Token);
        return new(controls, processor, provider, agent, timeout, running);
    }

    private sealed record YieldingDrain(DrainControlContext Controls, YieldingControlProcessor Processor,
        InProcessWorkflowExecutionActorProvider Provider, IWorkflowExecutionActor Agent, CancellationTokenSource Timeout,
        Task<WorkflowExecutionCommandDispatchResult> Running) : IDisposable
    {
        public void Dispose()
        {
            Provider.Dispose();
            Timeout.Dispose();
        }
    }

    private static WorkflowExecutionCommandEnvelope NewCancel(string idempotencyKey)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N");
        return new(id, "wfexec-1",
            new(id, "wfexec-1", WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>()),
            idempotencyKey, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now);
    }

    private sealed class AdmissionCommandProcessor : IWorkflowExecutionCommandExecutor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> Executed { get; } = new();

        public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.EnvelopeId == "envelope-1")
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            Executed.Enqueue(envelope.EnvelopeId);
            return WorkflowExecutionCommandProcessResult.NoDrain;
        }
    }

    private sealed class HeldAdmissionContext : SynchronizationContext, IDisposable
    {
        private readonly Channel<(SendOrPostCallback Callback, object? State)> callbacks =
            Channel.CreateUnbounded<(SendOrPostCallback, object?)>();

        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Writer.TryWrite((callback, state));

        public async Task WaitForPostAsync(CancellationToken cancellationToken) =>
            Assert.True(await callbacks.Reader.WaitToReadAsync(cancellationToken));

        public async Task ResumeUntilCompletedAsync(Task task, CancellationToken cancellationToken)
        {
            while (!task.IsCompleted)
            {
                await Task.WhenAny(task, callbacks.Reader.WaitToReadAsync(cancellationToken).AsTask()).WaitAsync(cancellationToken);
                while (callbacks.Reader.TryRead(out var callback))
                    callback.Callback(callback.State);
            }
        }

        public void Dispose() => callbacks.Writer.TryComplete();
    }

    private sealed class SignallingLogger : ILogger
    {
        public TaskCompletionSource<(LogLevel Level, Exception? Exception, string Message)> Logged { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Logged.TrySetResult((logLevel, exception, formatter(state, exception)));
    }

    // The first scheduler turn, after a step the test may hold, yields once a Cancel waits, holding the mailbox until the
    // test releases it. A turn whose source is the yield re-drive is reported, and fails or answers as set; any other
    // later scheduler turn drains nothing, or runs a drain to quiescence when its key is the draining key.
    private sealed class YieldingControlProcessor(DrainControlContext controls) : IWorkflowExecutionCommandExecutor
    {
        private int schedulerTurns;
        private int cancelsProcessed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource YieldObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<WorkflowExecutionCommandEnvelope> Redriven { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancelEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? CancelGate { get; set; }
        public TaskCompletionSource? Step { get; init; }
        public string? GatedKey { get; set; }
        public string? DrainingKey { get; set; }
        public TaskCompletionSource GatedEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? RedriveFailure { get; set; }
        public WorkflowExecutionCommandProcessResult? RedriveResult { get; set; }
        public int CancelsProcessed => Volatile.Read(ref cancelsProcessed);

        public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Command.Kind == WorkflowExecutionCommandKind.Cancel)
            {
                Interlocked.Increment(ref cancelsProcessed);
                CancelEntered.TrySetResult();
                if (CancelGate is { } gate)
                    await gate.Task.WaitAsync(cancellationToken);
                return WorkflowExecutionCommandProcessResult.NoDrain;
            }
            if (envelope.Command.Metadata.TryGetValue("source", out var source) &&
                source == InProcessWorkflowExecutionActorProvider.YieldRedriveSource)
            {
                Redriven.TrySetResult(envelope);
                if (RedriveFailure is { } failure)
                    throw failure;
                return RedriveResult ?? WorkflowExecutionCommandProcessResult.NoDrain;
            }
            if (Interlocked.Increment(ref schedulerTurns) > 1)
            {
                if (envelope.IdempotencyKey == GatedKey)
                {
                    GatedEntered.TrySetResult();
                    await Gate.Task.WaitAsync(cancellationToken);
                }
                return envelope.IdempotencyKey == DrainingKey
                    ? new(true, RuntimeSchedulerDrainStopReason.Quiesced, false, false, null)
                    : WorkflowExecutionCommandProcessResult.NoDrain;
            }
            Entered.TrySetResult();
            if (Step is { } step)
                await step.Task.WaitAsync(cancellationToken);
            while (!controls.ShouldYield(envelope.WorkflowExecutionId))
                await Task.Delay(5, cancellationToken);
            YieldObserved.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(true, RuntimeSchedulerDrainStopReason.Yielded, false, false, null);
        }
    }

    // Each drain waits until the test inspects, then records whether its own frame was asked to yield, by execution and
    // partition.
    private sealed class KeyedObservingProcessor(DrainControlContext controls, int drains) : IWorkflowExecutionCommandExecutor
    {
        private int entered;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Inspect { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentDictionary<string, bool> Yielded { get; } = new(StringComparer.Ordinal);

        public static string Key(string workflowExecutionId, string partition) => $"{partition}/{workflowExecutionId}";

        public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref entered) == drains)
                Entered.TrySetResult();
            await Inspect.Task.WaitAsync(cancellationToken);
            Yielded[Key(envelope.WorkflowExecutionId, envelope.Partition.Value)] = controls.ShouldYield(envelope.WorkflowExecutionId);
            return WorkflowExecutionCommandProcessResult.NoDrain;
        }
    }

    private sealed class ObservingControlProcessor(DrainControlContext controls) : IWorkflowExecutionCommandExecutor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Inspect { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool OwnYield;
        public bool OtherYield;
        public bool CancelYield;
        public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Command.Kind == WorkflowExecutionCommandKind.Cancel)
            {
                CancelYield = controls.ShouldYield(envelope.WorkflowExecutionId);
                return WorkflowExecutionCommandProcessResult.NoDrain;
            }
            Entered.TrySetResult();
            await Inspect.Task.WaitAsync(cancellationToken);
            OwnYield = controls.ShouldYield(envelope.WorkflowExecutionId);
            OtherYield = controls.ShouldYield("wfexec-2");
            return WorkflowExecutionCommandProcessResult.NoDrain;
        }
    }
}
