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
        // resumption sweep to re-drive. The parking is conditional on that sweep existing, which is what the router
        // reads off the resumption durability evidence composed here (#1320); the no-re-driver half is the next case.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var router = NewRouter(queue, AlwaysShed);

        var result = await router.ProcessAsync(NewEnvelope(WorkflowExecutionCommandKind.ResumeBookmark));

        Assert.True(result.Shed);
        Assert.True(result.ShedWorkQueued);
        Assert.False(result.DrainPerformed);
        Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Items);
    }

    [Fact]
    public async Task Router_ShedsAResumeWithoutQueueingWhenNoResumptionRedriverIsComposed()
    {
        // #1320, resolution 1. Parking is a promise that someone re-drives the item, and only
        // WorkflowsRuntimeResumptionFeature makes that true — AddWorkflowRuntime composes the gate but not the sweep.
        // The reachable host is therefore a hand-composed or in-memory one, which is catalog-legal and is the shape
        // most fixtures use; NOT a Groundwork-backed one, since all nine Groundwork persistence shell features declare
        // DependsOn "WorkflowsRuntimeResumption" and so auto-enable the sweep. On a host without it the parked item
        // would have no owner at all, so the refusal degrades to the start shape: nothing durable written, nothing
        // drained, and the caller's retry left as the only re-driver. Refusing loudly is the deliberate direction over
        // a Deferred that reads like a delivery and silently is not one.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainOrchestrator = new RecordingDrainOrchestrator();
        var router = NewRouter(queue, AlwaysShed, drainOrchestrator, hasRedriver: false);

        var result = await router.ProcessAsync(NewEnvelope(WorkflowExecutionCommandKind.ResumeBookmark));

        Assert.True(result.Shed);
        Assert.False(result.ShedWorkQueued);
        Assert.False(result.DrainPerformed);
        Assert.Equal(0, drainOrchestrator.DrainCount);
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Items);
    }

    [Fact]
    public async Task Router_ShedsWithoutQueueingWhenNoDurabilityEvidenceIsInjectedAtAll()
    {
        // The absent-enumerable default, which the container never produces — IEnumerable<T> always resolves, empty at
        // worst — but a hand-composed router does. It has to read as "no re-driver", so that forgetting to wire the
        // evidence costs a promise nobody could have kept rather than silently parking work with no owner.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var router = new WorkflowSchedulerCommandRouter(
            queue,
            new ImmediateWorkflowSchedulerDrainPolicy(),
            new RecordingDrainOrchestrator(),
            _clock,
            admissionController: AlwaysShed);

        var result = await router.ProcessAsync(NewEnvelope(WorkflowExecutionCommandKind.ResumeBookmark));

        Assert.True(result.Shed);
        Assert.False(result.ShedWorkQueued);
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Items);
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

    public static TheoryData<WorkflowExecutionCommandKind, bool> GatedCommandKinds()
    {
        // Computed from the enum rather than listed, so a kind added later defaults into the gated assertion — the
        // safe direction for a deny-list exemption — and widening IsSubjectToAdmission's exemption set by even one
        // kind goes red here until this test names it deliberately. Crossed with both compositions, because the
        // refusal shape now depends on a second thing (#1320): before PR #1291 only two gated kinds were asserted at
        // all and a widened exemption passed the whole suite, so a dimension left unenumerated is exactly how this
        // predicate has already been wrong once. Every kind under both answers closes that hole on the new dimension.
        var data = new TheoryData<WorkflowExecutionCommandKind, bool>();
        foreach (var kind in Enum.GetValues<WorkflowExecutionCommandKind>().Except(AdmissionExemptKinds))
        foreach (var hasRedriver in new[] { true, false })
            data.Add(kind, hasRedriver);
        return data;
    }

    // Deliberately duplicated from WorkflowSchedulerCommandRouter.QueuesOnShed, so a change to the refusal shape has
    // to be made twice; the reasoning for each half lives on that method's doc. Both halves are needed to park: a kind
    // whose parked item a re-driver would run, and a re-driver composed to run it.
    private static bool ExpectsQueueingOnShed(WorkflowExecutionCommandKind kind, bool hasRedriver) =>
        kind is not (
            WorkflowExecutionCommandKind.Start or
            WorkflowExecutionCommandKind.AlterWorkflow) &&
        hasRedriver;

    [Theory]
    [MemberData(nameof(GatedCommandKinds))]
    public async Task Router_ShedsEveryGatedKindAtCapacity(WorkflowExecutionCommandKind kind, bool hasRedriver)
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var router = NewRouter(queue, AlwaysShed, hasRedriver: hasRedriver);

        var result = await router.ProcessAsync(NewEnvelope(kind));

        Assert.True(result.Shed);
        Assert.False(result.DrainPerformed);
        var expectQueued = ExpectsQueueingOnShed(kind, hasRedriver);
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
        // Three separate pins, one per assertion, all against the REAL load signal:
        //   AmbientChargeWhileExecuting — the charge is still the flow's AMBIENT value after an await inside the
        //     callee, which is what TryAdmit's nested-command exemption and RecordDispatch (Ambient.Value?.Add()) both
        //     key off. Reading the signal's global counter cannot show this; only the ambient slot can. Swapping
        //     DispatchRuntimeAdmissionLoadSignal's AsyncLocal<Charge?> for a ThreadLocal<Charge?> — the mutation that
        //     drops ExecutionContext flow while leaving every counter intact — goes red on THIS assertion and nowhere
        //     else in the class, whenever the Task.Yield continuation resumes on another thread. That is the usual
        //     case rather than a guarantee, since the same worker may pick the continuation back up. (A plain static
        //     field is not that mutation: it makes the charge process-global, so this assertion still passes and the
        //     failures land on the burst and ambient-tracking cases instead.)
        //   LoadWhileExecuting — the charge is still OPEN (not acquired and discarded above the hand-off), and its
        //     weight is ONE. One unit is the whole weight and the assertion says so rather than manufacturing a bigger
        //     number: the product has exactly one RecordDispatch call site, WorkflowSchedulerDrainer, reached only
        //     through WorkflowDrainOrchestrator, which only the router's non-alteration branch drives. The work an
        //     alteration commits is charged to whoever drains that execution next — uncounted under the exempt
        //     RunSchedulerWork, charged to a subsequently admitted live command otherwise — never to the alteration.
        //   InFlightDispatches — the charge is released on the way out.
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
        Assert.True(executor.AmbientChargeWhileExecuting);
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

    [Theory]
    [InlineData(true, WorkflowExecutionCommandDispatchStatus.Duplicate)]
    [InlineData(false, WorkflowExecutionCommandDispatchStatus.Accepted)]
    public async Task Actor_ConsumesTheKeyOfAShedResumeOnlyWhenItWasActuallyQueued(
        bool hasRedriver,
        WorkflowExecutionCommandDispatchStatus expectedRetryStatus)
    {
        // The two halves of #1320 composed, which is where the defect lived: each half was correct on its own. The
        // router decides the refusal shape and the agent reads the key rule off ShedWorkQueued, so with a re-driver a
        // redelivery of the same deterministic key must answer Duplicate (the work is queued; running it twice is the
        // hazard), and without one it must answer Accepted (nothing was written; Duplicate would strand the command
        // with no queue anywhere holding it — a refusal that looks exactly like a delivery). Both directions are
        // asserted because the silent failure is the one that reports success.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var router = NewRouter(queue, AlwaysShed, hasRedriver: hasRedriver);
        using var provider = new InProcessWorkflowExecutionActorProvider(router);
        var actor = await provider.GetAgentAsync(NewActivationRequest());
        var envelope = NewEnvelope(WorkflowExecutionCommandKind.ResumeBookmark);

        var shed = await actor.EnqueueAsync(envelope);
        var queuedByTheShed = (await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Items.Count;
        _signal.InFlightDispatches = 0; // the overload passes, so only the key rule can decide the retry
        var retried = await actor.EnqueueAsync(envelope);

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, shed.Status);
        Assert.Equal("true", shed.Metadata[RuntimeMetadataKeys.DispatchShed]);
        Assert.Equal(expectedRetryStatus, retried.Status);
        Assert.Equal(hasRedriver ? 1 : 0, queuedByTheShed);
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
    // case reaches the routing under test instead of the "not composed" throw. The resumption re-driver is composed by
    // default too — as the real WorkflowsRuntimeResumptionFeature composes it, by contributing durability evidence for
    // the resumption component — so a case that says nothing about #1320 keeps the parking behavior it was written for.
    private WorkflowSchedulerCommandRouter NewRouter(
        IWorkflowSchedulerWorkQueue queue,
        IRuntimeAdmissionController admissionController,
        IWorkflowDrainOrchestrator? drainOrchestrator = null,
        IWorkflowAlterationActorCommandExecutor? alterationExecutor = null,
        bool hasRedriver = true) =>
        new(
            queue,
            new ImmediateWorkflowSchedulerDrainPolicy(),
            drainOrchestrator ?? new RecordingDrainOrchestrator(),
            _clock,
            alterationActorCommandExecutor: alterationExecutor ?? _alterationExecutor,
            admissionController: admissionController,
            durabilityEvidence: DurabilityEvidence(hasRedriver));

    // Both compositions carry the evidence a durable persistence provider (Groundwork) contributes on any host; only
    // the resumption entry comes from WorkflowsRuntimeResumptionFeature. The no-re-driver side is therefore NOT an
    // empty collection, so a router that asked "is there any evidence at all" rather than "is the resumption component
    // named" would promise a delivery nobody owns and fail here rather than pass.
    private static IEnumerable<IWorkflowDispatchDurabilityEvidence> DurabilityEvidence(bool hasRedriver)
    {
        string[] durableProviderComponents =
        [
            WorkflowDispatchDurabilityComponents.Checkpoint,
            WorkflowDispatchDurabilityComponents.DispatchStore,
            WorkflowDispatchDurabilityComponents.Outbox,
            WorkflowDispatchDurabilityComponents.Scheduler
        ];
        return durableProviderComponents
            .Concat(hasRedriver ? [WorkflowDispatchDurabilityComponents.Resumption] : [])
            .Select(component => new WorkflowDispatchDurabilityEvidence(component, WorkflowDispatchDurabilityLevel.Durable))
            .ToArray();
    }

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

    // Stands in for the alteration actor executor. It records no dispatch of its own — matching the real executor,
    // which reaches no RecordDispatch call site — and takes both of its readings AFTER an await, so they are what a
    // genuinely resumed continuation inside the callee sees rather than something the fixture manufactured.
    private sealed class RecordingAlterationCommandExecutor(IRuntimeAdmissionLoadSignal? loadSignal = null)
        : IWorkflowAlterationActorCommandExecutor
    {
        public int ExecuteCount { get; private set; }

        public long LoadWhileExecuting { get; private set; }

        public bool AmbientChargeWhileExecuting { get; private set; }

        public async ValueTask ExecuteAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            await Task.Yield();

            LoadWhileExecuting = loadSignal?.InFlightDispatches ?? 0;
            AmbientChargeWhileExecuting = loadSignal?.HasAmbientCharge ?? false;
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
