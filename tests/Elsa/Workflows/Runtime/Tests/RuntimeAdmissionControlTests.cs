using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Live dispatch admission control (RB1, #1235). Every assertion here is driven through the injected load signal and
/// a fake clock rather than through N concurrent workflows: an adaptive limiter demonstrated only under real load
/// cannot be falsified, and "the depth crossed the limit so the next command was shed" has to be a deterministic
/// statement before it is worth making.
/// </summary>
public sealed class RuntimeAdmissionControlTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly StubAdmissionLoadSignal _signal = new();
    private readonly RuntimeAdmissionDiagnostics _diagnostics = new();

    [Fact]
    public void TryAdmit_AdmitsWhileLoadIsBelowTheLimit()
    {
        var controller = NewController(new RuntimeAdmissionOptions { InitialLimit = 100, MinLimit = 10, MaxLimit = 200 });
        _signal.InFlightDispatches = 99;

        using var decision = controller.TryAdmit();

        Assert.True(decision.IsAdmitted);
        Assert.Null(decision.Reason);
        Assert.Null(decision.RetryAfter);
        Assert.Equal(1, _diagnostics.Admitted);
        Assert.Equal(0, _diagnostics.Shed);
    }

    [Fact]
    public void TryAdmit_ShedsOnceLoadReachesTheLimit()
    {
        var controller = NewController(new RuntimeAdmissionOptions
        {
            InitialLimit = 100,
            MinLimit = 10,
            MaxLimit = 200,
            RetryAfter = TimeSpan.FromSeconds(5)
        });
        _signal.InFlightDispatches = 100;

        using var decision = controller.TryAdmit();

        Assert.False(decision.IsAdmitted);
        Assert.Equal(TimeSpan.FromSeconds(5), decision.RetryAfter);
        Assert.Contains("at capacity", decision.Reason);
        Assert.Equal(100, decision.ObservedLoad);
        Assert.Equal(0, _diagnostics.Admitted);
        Assert.Equal(1, _diagnostics.Shed);
    }

    [Fact]
    public void TryAdmit_NeverShedsWhenNothingIsInFlight()
    {
        // A host with nothing running has no contention to protect against, so refusing would mean a host that can
        // serve one request at a time serves none. The floor holds even with the limit pinned as low as it goes.
        var controller = NewController(new RuntimeAdmissionOptions { StaticLimit = 1 });
        _signal.InFlightDispatches = 0;

        using var decision = controller.TryAdmit();

        Assert.True(decision.IsAdmitted);
    }

    [Fact]
    public void TryAdmit_AdmitsAtCapacityWhenSheddingIsDisabled()
    {
        // The kill switch stops the refusal, not the evaluation: the decision is still counted, so an operator who
        // turned shedding off can still see how often the host would have shed.
        var controller = NewController(new RuntimeAdmissionOptions { Enabled = false, InitialLimit = 10 });
        _signal.InFlightDispatches = 1000;

        using var decision = controller.TryAdmit();

        Assert.True(decision.IsAdmitted);
        Assert.Equal(1, _diagnostics.Admitted);
        Assert.Equal(0, _diagnostics.Shed);
    }

    [Fact]
    public void AdaptiveLimit_FallsWhenSaturatedCompletionsRunSlow()
    {
        var controller = NewController(new RuntimeAdmissionOptions
        {
            InitialLimit = 100,
            MinLimit = 10,
            MaxLimit = 200,
            LatencyTolerance = 2,
            DecreaseFactor = 0.5
        });
        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(10));  // calibrates the baseline
        Assert.Equal(100, controller.Limit);

        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(500)); // 50x the baseline

        Assert.Equal(50, controller.Limit);
        Assert.Equal(1, _diagnostics.LimitDecreases);
        Assert.Equal(0, _diagnostics.LimitIncreases);
    }

    [Fact]
    public void AdaptiveLimit_RisesWhenSaturatedCompletionsStayFast()
    {
        var controller = NewController(new RuntimeAdmissionOptions
        {
            InitialLimit = 100,
            MinLimit = 10,
            MaxLimit = 200,
            LatencyTolerance = 2,
            IncreaseStep = 7
        });
        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(10));
        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(15));

        Assert.Equal(107, controller.Limit);
        Assert.Equal(1, _diagnostics.LimitIncreases);
    }

    [Fact]
    public void AdaptiveLimit_IgnoresUnsaturatedCompletions()
    {
        // A slow completion taken while the host was near-idle says the work was slow, not that the host was full.
        // Acting on it would let one quiet host ratchet itself down to the floor and start shedding for no reason.
        var controller = NewController(new RuntimeAdmissionOptions
        {
            InitialLimit = 100,
            MinLimit = 10,
            MaxLimit = 200,
            LatencyTolerance = 2,
            DecreaseFactor = 0.5
        });

        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(10));

        _signal.InFlightDispatches = 4;
        CompleteSample(controller, TimeSpan.FromSeconds(30));

        Assert.Equal(100, controller.Limit);
        Assert.Equal(0, _diagnostics.LimitDecreases);
    }

    [Fact]
    public void AdaptiveLimit_StopsAtTheFloor()
    {
        var controller = NewController(new RuntimeAdmissionOptions
        {
            InitialLimit = 100,
            MinLimit = 64,
            MaxLimit = 200,
            LatencyTolerance = 2,
            DecreaseFactor = 0.5
        });
        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(1));
        for (var sample = 0; sample < 10; sample++)
            CompleteSaturatedSample(controller, TimeSpan.FromSeconds(10));

        Assert.Equal(64, controller.Limit);
    }

    [Fact]
    public void StaticLimit_PinsTheSameControllerRatherThanSelectingAnother()
    {
        // The override is the adaptive controller with its range collapsed onto one value, so the operator running a
        // static ceiling is running the code these tests cover. Adaptation still runs and still reports.
        var controller = NewController(new RuntimeAdmissionOptions
        {
            StaticLimit = 32,
            InitialLimit = 100,
            MinLimit = 10,
            MaxLimit = 200,
            LatencyTolerance = 2,
            DecreaseFactor = 0.5
        });
        Assert.Equal(32, controller.Limit);

        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(1));
        CompleteSaturatedSample(controller, TimeSpan.FromSeconds(10));

        Assert.Equal(32, controller.Limit);
        Assert.Equal(1, _diagnostics.LimitDecreases);

        _signal.InFlightDispatches = 31;
        using var admitted = controller.TryAdmit();
        Assert.True(admitted.IsAdmitted);

        _signal.InFlightDispatches = 32;
        using var shed = controller.TryAdmit();
        Assert.False(shed.IsAdmitted);
    }

    [Fact]
    public void TryAdmit_NeverShedsACommandDispatchedFromInsideAnAdmittedOne()
    {
        // A child workflow started during its parent's drain is not new work arriving at the host. The parent still
        // holds its charge, so refusing the child frees nothing and could only bounce until the parent finishes.
        var controller = NewController(new RuntimeAdmissionOptions { InitialLimit = 10, MinLimit = 10, MaxLimit = 10 });
        _signal.InFlightDispatches = 1000;
        _signal.HasAmbientCharge = true;

        using var decision = controller.TryAdmit();

        Assert.True(decision.IsAdmitted);
    }

    [Fact]
    public void DispatchLoadSignal_TracksTheAmbientCharge()
    {
        var signal = new DispatchRuntimeAdmissionLoadSignal();

        Assert.False(signal.HasAmbientCharge);
        using (signal.OpenCharge())
            Assert.True(signal.HasAmbientCharge);

        Assert.False(signal.HasAmbientCharge);
    }

    [Fact]
    public void DispatchLoadSignal_ChargesPerDispatchNotPerCommand()
    {
        // The unit that makes the limit mean the same work for an External-leaf run (~56 dispatches) and a fusable
        // one (~5). A per-command charge would count both as one.
        var signal = new DispatchRuntimeAdmissionLoadSignal();

        using (var charge = signal.OpenCharge())
        {
            Assert.Equal(1, signal.InFlightDispatches);

            for (var dispatch = 0; dispatch < 55; dispatch++)
                signal.RecordDispatch();

            Assert.Equal(56, charge.Units);
            Assert.Equal(56, signal.InFlightDispatches);
        }

        Assert.Equal(0, signal.InFlightDispatches);
    }

    [Fact]
    public void DispatchLoadSignal_IgnoresDispatchesOutsideAnAdmittedCommand()
    {
        // Recovery-sweep and pump dispatches run on flows that were never admitted; charging them would make the
        // reading something other than a reading of live dispatch.
        var signal = new DispatchRuntimeAdmissionLoadSignal();

        signal.RecordDispatch();

        Assert.Equal(0, signal.InFlightDispatches);
    }

    [Fact]
    public async Task Router_ShedsAStartWithoutQueueingAnything()
    {
        // A start refused after its work item was queued would be run later by the resumption sweep, so a caller told
        // "not taken, retry" would get the work done twice. Nothing durable may be written.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var router = NewRouter(queue, AlwaysShed);

        var result = await router.ProcessAsync(NewEnvelope(WorkflowExecutionCommandKind.Start));

        Assert.True(result.Shed);
        Assert.False(result.DrainPerformed);
        Assert.False(result.IsFaulted);
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Items);
    }

    [Fact]
    public async Task Router_ShedsAResumeButParksTheWorkItem()
    {
        // A command naming a live execution is deferred, not dropped, so the work item stays queued for the
        // resumption sweep. That is what makes Deferred a promise rather than a lie.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var router = NewRouter(queue, AlwaysShed);

        var result = await router.ProcessAsync(NewEnvelope(WorkflowExecutionCommandKind.ResumeBookmark));

        Assert.True(result.Shed);
        Assert.False(result.DrainPerformed);
        Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Items);
    }

    [Fact]
    public async Task Router_DrainsWhenAdmitted()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainOrchestrator = new RecordingDrainOrchestrator();
        var router = NewRouter(queue, NewController(new RuntimeAdmissionOptions { InitialLimit = 100 }), drainOrchestrator);
        _signal.InFlightDispatches = 0;

        var result = await router.ProcessAsync(NewEnvelope(WorkflowExecutionCommandKind.Start));

        Assert.False(result.Shed);
        Assert.True(result.DrainPerformed);
        Assert.Equal(1, drainOrchestrator.DrainCount);
    }

    [Fact]
    public async Task Actor_RendersAShedCommandAsDeferredWithoutConsumingTheIdempotencyKey()
    {
        // The command was refused, not processed, so the same key retried later must be evaluated afresh instead of
        // being answered Duplicate.
        var executor = new ShedThenAcceptCommandExecutor();
        using var provider = new InProcessWorkflowExecutionActorProvider(executor);
        var actor = await provider.GetAgentAsync(NewActivationRequest());
        var envelope = NewEnvelope(WorkflowExecutionCommandKind.Start);

        var shed = await actor.EnqueueAsync(envelope);

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, shed.Status);
        Assert.NotNull(shed.Reason);
        Assert.Equal("true", shed.Metadata[RuntimeMetadataKeys.DispatchShed]);
        Assert.Equal("3", shed.Metadata[RuntimeMetadataKeys.DispatchRetryAfterSeconds]);

        var retried = await actor.EnqueueAsync(envelope);

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, retried.Status);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private RuntimeAdmissionController NewController(RuntimeAdmissionOptions options) =>
        new(_signal, options, _diagnostics, _clock);

    private IRuntimeAdmissionController AlwaysShed =>
        NewControllerAtCapacity();

    private IRuntimeAdmissionController NewControllerAtCapacity()
    {
        _signal.InFlightDispatches = 1000;
        return NewController(new RuntimeAdmissionOptions { InitialLimit = 10, MinLimit = 10, MaxLimit = 10, RetryAfter = TimeSpan.FromSeconds(2) });
    }

    // Admits at exactly half the current limit — the saturation boundary — so the sample is both admitted and
    // counted as saturated whatever the limit has moved to by now.
    private void CompleteSaturatedSample(IRuntimeAdmissionController controller, TimeSpan duration)
    {
        _signal.InFlightDispatches = (long)(controller.Limit / 2);
        CompleteSample(controller, duration);
    }

    private void CompleteSample(IRuntimeAdmissionController controller, TimeSpan duration)
    {
        var decision = controller.TryAdmit();
        Assert.True(decision.IsAdmitted);
        _clock.Advance(duration);
        decision.Dispose();
    }

    private WorkflowSchedulerCommandRouter NewRouter(
        IWorkflowSchedulerWorkQueue queue,
        IRuntimeAdmissionController admissionController,
        IWorkflowDrainOrchestrator? drainOrchestrator = null) =>
        new(
            queue,
            new ImmediateWorkflowSchedulerDrainPolicy(),
            drainOrchestrator ?? new RecordingDrainOrchestrator(),
            _clock,
            admissionController: admissionController);

    private static WorkflowExecutionCommandEnvelope NewEnvelope(WorkflowExecutionCommandKind kind) =>
        new(
            envelopeId: "envelope-1",
            workflowExecutionId: "wfexec-1",
            command: new WorkflowExecutionCommand(
                CommandId: "command-1",
                WorkflowExecutionId: "wfexec-1",
                Kind: kind,
                EnqueuedAt: Now,
                Payload: JsonDocument.Parse("{}").RootElement.Clone(),
                Metadata: new Dictionary<string, string>()),
            idempotencyKey: "wfexec-1:command-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now);

    private static WorkflowExecutionActorActivationRequest NewActivationRequest() =>
        new(
            workflowExecutionId: "wfexec-1",
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: Now,
            requestedBy: "test",
            requiredCapabilities: WorkflowExecutionActorCapabilities.None);

    private sealed class StubAdmissionLoadSignal : IRuntimeAdmissionLoadSignal
    {
        public long InFlightDispatches { get; set; }

        public bool HasAmbientCharge { get; set; }

        public IRuntimeAdmissionCharge OpenCharge() => new NoopCharge();

        public void RecordDispatch()
        {
        }

        private sealed class NoopCharge : IRuntimeAdmissionCharge
        {
            public long Units => 1;

            public void Dispose()
            {
            }
        }
    }

    private sealed class RecordingDrainOrchestrator : IWorkflowDrainOrchestrator
    {
        public int DrainCount { get; private set; }

        public ValueTask<RuntimeSchedulerDrainResult> DrainAsync(
            WorkflowExecutionCommandEnvelope envelope,
            RuntimeSchedulerDrainRequest request,
            CancellationToken cancellationToken = default)
        {
            DrainCount++;
            return ValueTask.FromResult(new RuntimeSchedulerDrainResult(
                workflowExecutionId: request.WorkflowExecutionId,
                startedAt: Now,
                completedAt: Now,
                items: []));
        }
    }

    private sealed class ShedThenAcceptCommandExecutor : IWorkflowExecutionCommandExecutor
    {
        private bool _shed;

        public ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(
            WorkflowExecutionCommandEnvelope envelope,
            CancellationToken cancellationToken = default) =>
            ProcessAsync(envelope, WorkflowExecutionCommandDispatchOptions.Default, cancellationToken);

        public ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(
            WorkflowExecutionCommandEnvelope envelope,
            WorkflowExecutionCommandDispatchOptions options,
            CancellationToken cancellationToken = default)
        {
            if (_shed)
                return ValueTask.FromResult(WorkflowExecutionCommandProcessResult.NoDrain);

            _shed = true;
            return ValueTask.FromResult(
                WorkflowExecutionCommandProcessResult.FromShed("at capacity", TimeSpan.FromSeconds(3)));
        }
    }
}
