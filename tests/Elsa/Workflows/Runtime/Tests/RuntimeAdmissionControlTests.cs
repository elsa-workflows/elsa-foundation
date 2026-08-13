using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
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
    private readonly RecordingAlterationCommandExecutor _alterationExecutor = new();

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
    public void TryAdmit_ReportsThePreReservationLoadOnTheExemptPath()
    {
        // Uses the REAL load signal because the pin is an evaluation-order fact the stub cannot see: with a parent's
        // ambient charge holding one unit, the exempt path must report ObservedLoad = 1 (the load before its own
        // charge opened), matching what the gated path's compare-and-reserve reports. Reading the load after opening
        // — the one-liner shape `Admit(OpenCharge(), InFlightDispatches, ...)` evaluates left to right — reports 2
        // and lets a nested command's own seeded unit tip its completion sample into the saturated band.
        var signal = new DispatchRuntimeAdmissionLoadSignal();
        using var parentCharge = signal.OpenCharge();
        var controller = new RuntimeAdmissionController(signal);

        using var decision = controller.TryAdmit();

        Assert.True(decision.IsAdmitted);
        Assert.Equal(1, decision.ObservedLoad);
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
        var controller = NewController(AdaptiveOptions());
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
        var controller = NewController(AdaptiveOptions());
        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(10));
        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(15));

        Assert.Equal(107, controller.Limit);
        Assert.Equal(1, _diagnostics.LimitIncreases);
    }

    [Fact]
    public void AdaptiveLimit_IgnoresACompletionThatDispatchedNothing()
    {
        // A command that opened a charge and never reached a scheduler dispatch never touched the writer this limit
        // protects, so its duration measures something else. AlterWorkflow (#1325) is the live case: it is gated, but
        // the alteration executor reaches no RecordDispatch call site at all, and its sub-millisecond exits — the
        // duplicate-return after two store lookups, the claim-fence throw — are durations no real drain produces.
        // Learning from one is worse than learning from an unsaturated sample, because the baseline update takes a
        // faster sample OUTRIGHT: the collapsed baseline then makes the next genuine drain look like a latency
        // blow-out and cuts the limit, on a host whose alteration pump re-pins it every sweep.
        var controller = NewController(AdaptiveOptions());
        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(500));                 // calibrates at 500 ms

        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(1), dispatches: 0);    // must teach nothing
        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(900));                 // within 2x of 500 ms

        // The 900 ms sample DID dispatch, so it still moves the limit — the guard skips uninformative samples, it
        // does not switch adaptation off. Had the 1 ms sample been learned from, the limit would have stepped up on it
        // and then been halved when 900 ms blew past a baseline collapsed to 1 ms.
        Assert.Equal(107, controller.Limit);
        Assert.Equal(1, _diagnostics.LimitIncreases);
        Assert.Equal(0, _diagnostics.LimitDecreases);
    }

    [Fact]
    public void AdaptiveLimit_DoesNotCalibrateTheBaselineOnACompletionThatDispatchedNothing()
    {
        // The placement half: the guard sits ABOVE the first-sample calibration. A zero-dispatch sample arriving
        // first is the worst version of the same defect — it would become the baseline every later sample is judged
        // against, with no decay path back, rather than one bad reading among many.
        var controller = NewController(AdaptiveOptions());

        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(1), dispatches: 0);
        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(500));                 // the FIRST real sample
        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(900));

        Assert.Equal(107, controller.Limit);
        Assert.Equal(0, _diagnostics.LimitDecreases);
    }

    [Fact]
    public void AdaptiveLimit_IgnoresUnsaturatedCompletions()
    {
        // A slow completion taken while the host was near-idle says the work was slow, not that the host was full.
        // Acting on it would let one quiet host ratchet itself down to the floor and start shedding for no reason.
        var controller = NewController(AdaptiveOptions());

        CompleteSaturatedSample(controller, TimeSpan.FromMilliseconds(10));

        CompleteSample(controller, TimeSpan.FromSeconds(30), load: 4);

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
    public void TryAdmit_AdmitsExactlyTheLimitUnderASimultaneousBurst()
    {
        // The invariant a check-then-reserve gate cannot hold. The load signal here is instrumented to FORCE the worst
        // interleaving rather than hope for it: any caller that reads the load separately parks until every caller has
        // read, so they would all observe the same below-limit value and all reserve. An atomic gate never takes that
        // read path at all, which is what makes this assertion deterministic instead of a race the test might lose.
        const int limit = 8;
        const int callers = 64;
        var signal = new ReadRendezvousLoadSignal(callers);
        var controller = new RuntimeAdmissionController(
            signal,
            new RuntimeAdmissionOptions { StaticLimit = limit },
            _diagnostics,
            _clock);

        var decisions = new RuntimeAdmissionDecision[callers];
        var start = new ManualResetEventSlim();
        // Dedicated threads, not the thread pool: the pool injects workers slowly, so a pool-scheduled burst would
        // arrive staggered and the interleaving under test would never occur.
        var threads = Enumerable.Range(0, callers)
            .Select(index => new Thread(() =>
            {
                start.Wait();
                decisions[index] = controller.TryAdmit();
            }) { IsBackground = true })
            .ToArray();

        foreach (var thread in threads)
            thread.Start();
        start.Set();
        foreach (var thread in threads)
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)));

        Assert.Equal(limit, decisions.Count(decision => decision.IsAdmitted));
        Assert.Equal(callers - limit, decisions.Count(decision => !decision.IsAdmitted));
        Assert.Equal(limit, _diagnostics.Admitted);
        Assert.Equal(callers - limit, _diagnostics.Shed);

        foreach (var decision in decisions)
            decision.Dispose();
    }

    [Fact]
    public void TryAdmit_DecidesThroughTheAtomicReservationNotASeparateRead()
    {
        // The structural half of the same guarantee, and the one that fails outright on a check-then-reserve gate:
        // reaching a limit decision by reading the load first is the bug, so the signal refuses to serve that read.
        var controller = new RuntimeAdmissionController(
            new ReservationOnlyLoadSignal(),
            new RuntimeAdmissionOptions { StaticLimit = 4 },
            _diagnostics,
            _clock);

        using var decision = controller.TryAdmit();

        Assert.True(decision.IsAdmitted);
    }

    [Fact]
    public void DispatchLoadSignal_RefusesAReservationAtTheLimit()
    {
        var signal = new DispatchRuntimeAdmissionLoadSignal();

        var first = signal.TryOpenCharge(limit: 1, out var loadBefore);
        var second = signal.TryOpenCharge(limit: 1, out var loadAfter);

        Assert.NotNull(first);
        Assert.Equal(0, loadBefore);
        Assert.Null(second);
        Assert.Equal(1, loadAfter);
        first!.Dispose();
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
        Assert.False(result.ShedWorkQueued);
        Assert.False(result.DrainPerformed);
        Assert.False(result.IsFaulted);
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Items);
    }

    [Fact]
    public async Task Router_ShedsAResumeButParksTheWorkItem()
    {
        // A command naming a live execution is deferred, not dropped, so the work item stays queued for the
        // resumption sweep to re-drive. What is pinned here is the parking, not the delivery: the sweep is composed
        // only by WorkflowsRuntimeResumptionFeature and AddWorkflowRuntime does not register it, so on a host without
        // one the parked item has no owner — the still-open gap tracked by #1320.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var router = NewRouter(queue, AlwaysShed);

        var result = await router.ProcessAsync(NewEnvelope(WorkflowExecutionCommandKind.ResumeBookmark));

        Assert.True(result.Shed);
        Assert.True(result.ShedWorkQueued);
        Assert.False(result.DrainPerformed);
        Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Items);
    }

    // The single source of truth for which kinds bypass the admission gate, shared by both theories below so the
    // gated list and the exempt list cannot drift apart.
    private static readonly WorkflowExecutionCommandKind[] AdmissionExemptKinds =
    [
        WorkflowExecutionCommandKind.RunSchedulerWork,
        WorkflowExecutionCommandKind.Cancel,
        WorkflowExecutionCommandKind.PauseWorkflowExecution,
        WorkflowExecutionCommandKind.UnpauseWorkflowExecution
    ];

    public static TheoryData<WorkflowExecutionCommandKind> ExemptCommandKinds()
    {
        var data = new TheoryData<WorkflowExecutionCommandKind>();
        foreach (var kind in AdmissionExemptKinds)
            data.Add(kind);
        return data;
    }

    public static TheoryData<WorkflowExecutionCommandKind> GatedCommandKinds()
    {
        // Computed from the enum rather than listed, so a kind added later defaults into the gated assertion — the
        // safe direction for a deny-list exemption — and widening IsSubjectToAdmission's exemption set by even one
        // kind goes red here until this test names it deliberately.
        var data = new TheoryData<WorkflowExecutionCommandKind>();
        foreach (var kind in Enum.GetValues<WorkflowExecutionCommandKind>().Except(AdmissionExemptKinds))
            data.Add(kind);
        return data;
    }

    // The gated kinds that are refused OUTRIGHT rather than parked for a later drain, and why each one is on the list.
    // Start writes nothing durable, so a caller told "not taken, retry" cannot end up having the work done twice.
    // AlterWorkflow must not park either, for a different reason: NoopWorkflowSchedulerWorkHandler.CanHandle matches
    // every kind except InvokeActivity/GeneratedEvent/ResumeBookmark and its HandleAsync does nothing, so a parked
    // alteration item would be silently swallowed on the next drain even where the resumption sweep IS composed. That
    // handler-less property is NOT unique to it — ContinueVolatileWait and DeliverSignal share it and would be
    // dropped the same way, latent only because nothing in Elsa constructs them, so whoever wires one up owns adding
    // it here. AlterWorkflow is on the list because it is the reachable one AND because the outright refusal is safe
    // for it: both listed kinds have a re-driver that needs no parked item, the caller retrying a start and the
    // alteration pump re-claiming the job once its lease lapses (a full JobLeaseDuration, one minute by default).
    private static bool ExpectsQueueingOnShed(WorkflowExecutionCommandKind kind) => kind is not (
        WorkflowExecutionCommandKind.Start or
        WorkflowExecutionCommandKind.AlterWorkflow);

    [Theory]
    [MemberData(nameof(GatedCommandKinds))]
    public async Task Router_ShedsEveryGatedKindAtCapacity(WorkflowExecutionCommandKind kind)
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var router = NewRouter(queue, AlwaysShed);

        var result = await router.ProcessAsync(NewEnvelope(kind));

        Assert.True(result.Shed);
        Assert.False(result.DrainPerformed);
        var expectQueued = ExpectsQueueingOnShed(kind);
        Assert.Equal(expectQueued, result.ShedWorkQueued);
        Assert.Equal(expectQueued ? 1 : 0, (await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Items.Count);
    }

    [Theory]
    [MemberData(nameof(ExemptCommandKinds))]
    public async Task Router_NeverShedsRecoveryOrControlPlaneCommands(WorkflowExecutionCommandKind kind)
    {
        // RunSchedulerWork is the resumption sweep re-driving a backlog that is ALREADY queued, so shedding it would
        // add one more trigger per sweep pass and grow the backlog the sweep exists to drain. Cancel/pause/unpause
        // reduce load, so refusing them at capacity would remove the tool for ending the overload.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainOrchestrator = new RecordingDrainOrchestrator();
        var router = NewRouter(queue, AlwaysShed, drainOrchestrator);

        var result = await router.ProcessAsync(NewEnvelope(kind));

        Assert.False(result.Shed);
        Assert.True(result.DrainPerformed);
        Assert.Equal(1, drainOrchestrator.DrainCount);
    }

    [Fact]
    public async Task Router_ChargesAnAdmittedAlterationForTheDurationOfTheExecutor()
    {
        // The charge has to stay OPEN across the hand-off rather than be acquired and discarded above it, because
        // RecordDispatch is Ambient.Value?.Add() and TryAdmit's nested-command exemption keys off the same ambient
        // value: a released charge would make a command dispatched from inside an alteration look like new work
        // arriving at the door and let it be shed behind its own caller. Uses the REAL load signal, which the stub
        // cannot model, and the executor reads the load AFTER an await, so this also pins that the AsyncLocal charge
        // survives a genuine async continuation inside the callee and is released on the way out.
        //
        // ONE unit is the whole weight, and the assertion says so deliberately rather than manufacturing a bigger
        // number. The product has exactly one RecordDispatch call site — WorkflowSchedulerDrainer, reached only
        // through WorkflowDrainOrchestrator, which only the router's non-alteration branch drives — so an admitted
        // alteration weighs the seed unit and nothing more, flat, whatever its plan size. The work it commits is
        // drained later under the admission-exempt RunSchedulerWork and stays uncounted.
        var signal = new DispatchRuntimeAdmissionLoadSignal();
        var executor = new RecordingAlterationCommandExecutor(signal);
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainOrchestrator = new RecordingDrainOrchestrator();
        var router = NewRouter(
            queue,
            new RuntimeAdmissionController(signal, new RuntimeAdmissionOptions { InitialLimit = 100 }, _diagnostics, _clock),
            drainOrchestrator,
            executor);

        var result = await router.ProcessAsync(NewEnvelope(WorkflowExecutionCommandKind.AlterWorkflow));

        Assert.False(result.Shed);
        Assert.Equal(1, _diagnostics.Admitted);
        Assert.Equal(1, executor.ExecuteCount);
        Assert.Equal(1, executor.LoadWhileExecuting);
        Assert.Equal(0, signal.InFlightDispatches);

        // The hand-off is TERMINAL. An admitted alteration must neither park a work item — only the Noop fallback
        // handler would match it, and it swallows silently — nor drain, and the alteration executor is the only thing
        // that ever runs the command. Without these three, deleting the branch's `return` re-introduces exactly the
        // silently-swallowed parked item this unit exists to avoid, and every other assertion above still holds.
        Assert.False(result.DrainPerformed);
        Assert.Equal(0, drainOrchestrator.DrainCount);
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Items);
    }

    [Fact]
    public async Task Router_RefusesAnAlterationAboveTheHandOff()
    {
        // The refusal SHAPE for AlterWorkflow (shed, nothing queued, no drain) is pinned by
        // Router_ShedsEveryGatedKindAtCapacity and is not repeated here. What only this case can pin is WHERE the
        // refusal happens: above the hand-off, so the executor never runs and the job stays claimable for the
        // alteration pump to re-claim once its lease lapses. Kept as its own Fact rather than folded into the theory,
        // where an ExecuteCount assertion would be vacuously true for the other thirteen gated kinds.
        var router = NewRouter(new InMemoryWorkflowSchedulerWorkQueue(), AlwaysShed);

        var result = await router.ProcessAsync(NewEnvelope(WorkflowExecutionCommandKind.AlterWorkflow));

        Assert.True(result.Shed);
        Assert.Equal(0, _alterationExecutor.ExecuteCount);
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
    public async Task Actor_KeepsAShedStartRetryableOnTheSameIdempotencyKey()
    {
        // A shed start wrote nothing, so the key is not consumed: answering Duplicate on the retry would strand a
        // workflow that never ran.
        var executor = new ShedThenAcceptCommandExecutor(workQueued: false);
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

    [Fact]
    public async Task Actor_ConsumesTheIdempotencyKeyWhenAShedCommandWasQueued()
    {
        // The work item is durably queued and waiting for the resumption sweep. An at-least-once redelivery of the
        // same key — which the distributed placement pump performs by design on every Deferred result — would queue
        // the same work twice.
        var executor = new ShedThenAcceptCommandExecutor(workQueued: true);
        using var provider = new InProcessWorkflowExecutionActorProvider(executor);
        var actor = await provider.GetAgentAsync(NewActivationRequest());
        var envelope = NewEnvelope(WorkflowExecutionCommandKind.ResumeBookmark);

        var shed = await actor.EnqueueAsync(envelope);
        var retried = await actor.EnqueueAsync(envelope);

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, shed.Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, retried.Status);
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

    // The adaptive shape the limit-movement tests share: room to move in both directions, a 2x tolerance, and a
    // halving decrease so one cut is unmistakable against one +7 step.
    private static RuntimeAdmissionOptions AdaptiveOptions() => new()
    {
        InitialLimit = 100,
        MinLimit = 10,
        MaxLimit = 200,
        LatencyTolerance = 2,
        DecreaseFactor = 0.5,
        IncreaseStep = 7
    };

    // Admits at the lowest load the controller still calls saturated, whatever the limit has moved to by now.
    // Rounded UP, not truncated: the controller's test is `ObservedLoad >= Limit / 2` against the double limit, so at
    // an odd limit — 107 after one +7 step — truncation lands one unit BELOW the boundary and the sample is silently
    // unsaturated. That is not a cosmetic difference: it made a mutation of the ChargedUnits guard survive here,
    // because the sample meant to detect the collapsed baseline was never evaluated against it at all.
    private void CompleteSaturatedSample(IRuntimeAdmissionController controller, TimeSpan duration, int dispatches = 1) =>
        CompleteSample(controller, duration, dispatches, load: (long)Math.Ceiling(controller.Limit / 2));

    // Defaults to one dispatch because that is the shape of every command the limiter is meant to learn from: a drain
    // records at least one. dispatches: 0 is the zero-dispatch sample — an alteration, or anything else that opens a
    // charge and never reaches the drainer — which must not be learned from at all.
    private void CompleteSample(IRuntimeAdmissionController controller, TimeSpan duration, int dispatches = 1, long? load = null)
    {
        if (load is { } value)
            _signal.InFlightDispatches = value;

        var decision = controller.TryAdmit();
        Assert.True(decision.IsAdmitted);
        for (var dispatch = 0; dispatch < dispatches; dispatch++)
            _signal.RecordDispatch();
        _clock.Advance(duration);
        decision.Dispose();
    }

    // The alteration executor is composed by default, the way a real runtime host composes it, so an AlterWorkflow
    // case reaches the routing under test instead of the "not composed" throw.
    private WorkflowSchedulerCommandRouter NewRouter(
        IWorkflowSchedulerWorkQueue queue,
        IRuntimeAdmissionController admissionController,
        IWorkflowDrainOrchestrator? drainOrchestrator = null,
        IWorkflowAlterationActorCommandExecutor? alterationExecutor = null) =>
        new(
            queue,
            new ImmediateWorkflowSchedulerDrainPolicy(),
            drainOrchestrator ?? new RecordingDrainOrchestrator(),
            _clock,
            alterationActorCommandExecutor: alterationExecutor ?? _alterationExecutor,
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

    // Drives the in-flight reading from the test rather than from real work, but models the ONE thing the controller
    // reads back off a completion faithfully: a charge starts at the seed unit and grows one per dispatch. A stub
    // whose Units were pinned at 1 would report every sample as zero-dispatch and hide the ChargedUnits rule entirely.
    private sealed class StubAdmissionLoadSignal : IRuntimeAdmissionLoadSignal
    {
        private StubCharge? _current;

        public long InFlightDispatches { get; set; }

        public bool HasAmbientCharge { get; set; }

        public IRuntimeAdmissionCharge? TryOpenCharge(double limit, out long observedLoad)
        {
            observedLoad = InFlightDispatches;
            return observedLoad > 0 && observedLoad >= limit ? null : OpenCharge();
        }

        public IRuntimeAdmissionCharge OpenCharge() => _current = new StubCharge();

        public void RecordDispatch() => _current?.Add();

        private sealed class StubCharge : IRuntimeAdmissionCharge
        {
            public long Units { get; private set; } = 1;

            public void Add() => Units++;

            public void Dispose()
            {
            }
        }
    }

    // Pass-through over the real signal that makes every separate load READ rendezvous with the others, forcing the
    // interleaving a check-then-reserve gate loses on. The atomic gate never reads, so nothing ever waits.
    private sealed class ReadRendezvousLoadSignal(int callers) : IRuntimeAdmissionLoadSignal
    {
        private readonly DispatchRuntimeAdmissionLoadSignal _inner = new();
        private readonly CountdownEvent _reads = new(callers);

        public long InFlightDispatches
        {
            get
            {
                var value = _inner.InFlightDispatches;
                _reads.Signal();
                _reads.Wait(TimeSpan.FromSeconds(10));
                return value;
            }
        }

        public bool HasAmbientCharge => _inner.HasAmbientCharge;

        public IRuntimeAdmissionCharge? TryOpenCharge(double limit, out long observedLoad) =>
            _inner.TryOpenCharge(limit, out observedLoad);

        public IRuntimeAdmissionCharge OpenCharge() => _inner.OpenCharge();

        public void RecordDispatch() => _inner.RecordDispatch();
    }

    private sealed class ReservationOnlyLoadSignal : IRuntimeAdmissionLoadSignal
    {
        private readonly DispatchRuntimeAdmissionLoadSignal _inner = new();

        public long InFlightDispatches =>
            throw new InvalidOperationException("A gated admission decision must come from TryOpenCharge, not from a separate load read.");

        public bool HasAmbientCharge => _inner.HasAmbientCharge;

        public IRuntimeAdmissionCharge? TryOpenCharge(double limit, out long observedLoad) =>
            _inner.TryOpenCharge(limit, out observedLoad);

        public IRuntimeAdmissionCharge OpenCharge() => _inner.OpenCharge();

        public void RecordDispatch() => _inner.RecordDispatch();
    }

    // Stands in for the alteration actor executor. It reads the load AFTER an await and never records a dispatch of
    // its own — matching the real executor, which reaches no RecordDispatch call site — so what it reports back is
    // the reading a genuinely resumed continuation inside the callee sees, not one manufactured by the fixture.
    private sealed class RecordingAlterationCommandExecutor(IRuntimeAdmissionLoadSignal? loadSignal = null)
        : IWorkflowAlterationActorCommandExecutor
    {
        public int ExecuteCount { get; private set; }

        public long LoadWhileExecuting { get; private set; }

        public async ValueTask ExecuteAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            await Task.Yield();

            LoadWhileExecuting = loadSignal?.InFlightDispatches ?? 0;
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

    private sealed class ShedThenAcceptCommandExecutor(bool workQueued) : IWorkflowExecutionCommandExecutor
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
                WorkflowExecutionCommandProcessResult.FromShed("at capacity", TimeSpan.FromSeconds(3), workQueued));
        }
    }
}
