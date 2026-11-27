using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Kernel.WorkflowActor;
using static Kernel.Native.Wf;
using Contract = Kernel.Foundation.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

/// <summary>
/// Timer and Cron on the schedule actor (K6; GA test drive card 3 B), through the public actor contract on the in-memory
/// foundation with its clock under the check's control: each occurrence starts one run, also when its turn's commit is
/// lost or its owner is replaced, a late owner fires the due occurrence once without the backlog, a changed schedule
/// replaces the timer, and a stopped one fires no more.
/// </summary>
internal static class ScheduleChecks
{
    private const string EveryHalfMinute = "*/30 * * * * *";

    /// <summary>A stand-in for the host's cron reading: one expression, every 30 seconds on the clock.</summary>
    private sealed class Calendar : ICronCalendar
    {
        public long? Next(string expression, long after) =>
            expression == EveryHalfMinute ? after - after % 30_000 + 30_000 : throw new FormatException($"no expression '{expression}'");
    }

    private static string? CronError(string expression) => expression == EveryHalfMinute ? null : "not an expression this host reads";

    private static WorkflowRegistry Registry() => new WorkflowRegistry()
        .Add(Workflow("tick", Sequence("root", Timer("t", "PT1M"), Set("o", "output", "ticked"))))
        .Add(Workflow("cron-tick", Sequence("root", Cron("c", EveryHalfMinute), Set("o", "output", "cron"))))
        // A Flowchart begins at its Timer, not at the other node no connection leads to, which would wait for ever.
        .Add(Workflow("flow-tick", Flowchart("flow",
            [Event("first", "never"), Timer("t", "00:01:00"), Set("after", "output", "timer")],
            [new("t", "after")])))
        // The start path is followed through containers, through a Flowchart by its start node.
        .Add(Workflow("nested", Sequence("root", Sequence("inner", Timer("t", "PT1H"), Set("o", "output", "nested")))))
        .Add(Workflow("deep-start", Flowchart("flow", [Sequence("s", Timer("t", "PT1M"), Set("o", "output", "deep"))], [])));

    public static async Task Run(Action<bool, string> check)
    {
        var registry = Registry();
        var table = new NativeSchedules(registry, CronError);
        check(table.All.Select(s => $"{s.Workflow}/{s.Node}/{s.Kind}").SequenceEqual(["cron-tick/c/cron", "deep-start/t/interval", "flow-tick/t/interval", "nested/t/interval", "tick/t/interval"]),
            $"a Timer or Cron where a run begins is a schedule, also down the start path through containers ({string.Join(", ", table.All.Select(s => s.Key))})");
        check(Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("mid", Sequence("root", Set("a", "x", 1), Timer("t", "PT1M")))))) &&
              Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("joined", Flowchart("flow", [Set("a", "x", 1), Timer("t", "PT1M")], [new("a", "t")]))))) &&
              // Deeper than a Flowchart's direct children, a trigger counts only through the start node: here the run begins at 'a'.
              Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("deep", Flowchart("flow",
                  [Event("a", "never"), Sequence("s", Timer("t", "PT1M"), Set("o", "x", 1))], []))))),
            "a Timer away from where a run begins is refused, as the old core refuses to publish it, also one below a Flowchart child that is not its start");
        check(Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("zero", Timer("t", "PT0S"))))) &&
              Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("tiny", Timer("t", "PT0.0001S"))))) &&
              Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("huge", Timer("t", "P99999999Y"))))) &&
              Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("bad", Timer("t", "soon"))))) &&
              Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("computed", Node<Kernel.Native.Timer>("t", ("Interval", Binding.Expr("'PT1M'")))))) ) &&
              Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("cron", Cron("c", "* * *"))), CronError)) &&
              Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("no-calendar", Cron("c", EveryHalfMinute))))),
            "an interval under a second, one that does not read or overflows, a computed one, and a cron expression the host does not read are refused");
        // K10: a schedule is keyed by the definition, so a republished version keeps its schedule actor; its run starts the
        // version under the definition. Two versions of one definition with one schedule would fire it twice.
        static Workflow Orders(string version) => VersionOf("orders", Workflow(version, Sequence("root", Timer("t", "PT1M"), Set("o", "output", version))));
        var versioned = new NativeSchedules(new WorkflowRegistry().Add(Orders("orders@2")));
        check(versioned.All is [{ Key: "schedule:orders:t", StartKey: "schedule-start:orders:t", Workflow: "orders@2" } ordersSchedule] &&
              ordersSchedule.Start() is { Workflow: "orders@2", Definition: "orders" } &&
              Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Orders("orders@1")).Add(Orders("orders@2")))),
            $"a schedule is keyed by its definition and starts the version it was built from, and two versions of one definition with one schedule are refused ({string.Join(", ", versioned.All.Select(s => s.Key))})");
        check(Argument(() => ScheduleActor.SetAsync(null!, null!, "k", 1, new RecurringSchedule(RecurringSchedule.Interval, "soon"), "s")) &&
              Argument(() => ScheduleActor.SetAsync(null!, null!, "k", 1, new RecurringSchedule(RecurringSchedule.Interval, "PT0.0001S"), "s")) &&
              Argument(() => ScheduleActor.SetAsync(null!, null!, "k", 1, new RecurringSchedule("weekly", "x"), "s")) &&
              Argument(() => ScheduleActor.SetAsync(null!, null!, "k", 1, new RecurringSchedule(RecurringSchedule.Cron, "* * *"), "s", new Calendar())),
            "setting a schedule that does not read is refused before anything is sent");

        var model = new NativeExecutionModel(registry);
        await using var driver = new ContractWorkload.InMemoryDriver(4, Flaw.None, new PublicWorkflow([model]),
            authorizeOperator: (_, _) => true, alsoTypes: [new ScheduleActor(new Calendar())]);
        var scope = ContractScopes.Bind("schedules");
        await driver.TakeAll();

        async Task<Contract.AdmissionOutcome[]> SetAll()
        {
            var outcomes = new List<Contract.AdmissionOutcome>();
            foreach (var s in table.All)
            {
                await ScheduleActor.RegisterRunAsync(driver.Ingress, scope, s.StartKey, s.Start());
                outcomes.Add((await ScheduleActor.SetAsync(driver.Ingress, scope, s.Key, 1, new RecurringSchedule(s.Kind, s.Value), s.StartKey)).Outcome);
            }
            return [.. outcomes];
        }
        async Task<List<Contract.ActorView>> List(string? type)
        {
            var all = new List<Contract.ActorView>();
            for (Contract.PageToken? next = null; ;)
            {
                var page = await driver.Reads.ListAsync(scope, new Contract.ActorQuery(Type: type), next);
                all.AddRange(page.Items);
                if ((next = page.Next) is null) return all;
            }
        }
        async Task<(int Finished, int Running)> Runs(string workflow)
        {
            var runs = (await List(PublicWorkflow.ActorType)).Where(v => JsonNode.Parse(v.Header.TypeData)?["Workflow"]?.GetValue<string>() == workflow).ToList();
            return (runs.Count(r => r.Header.Status == Contract.ActorStatus.Finished), runs.Count(r => r.Header.Status == Contract.ActorStatus.Running));
        }
        // The driver's lease is 50 ms, so a jump of the clock is an owner change: a new owner takes every partition and
        // restores each schedule's timer from its committed state, then runs what is due.
        async Task Jump(long milliseconds)
        {
            driver.Advance(milliseconds - 51);
            await driver.TakeOver();     // advances the remaining 51 ms
        }
        async Task Step(long milliseconds)
        {
            await Jump(milliseconds);
            await driver.PassAll(4);
        }

        var created = await SetAll();
        await driver.PassAll(4);
        check(created.All(o => o == Contract.AdmissionOutcome.Accepted) && (await List(ScheduleActor.ActorType)).Count == 5 && (await Runs("tick")).Finished == 0,
            "setting the schedules creates one schedule actor each, and nothing runs before its first occurrence");

        // The clock moves in jumps, each an owner change, so every occurrence below fires after its timer was restored.
        // An interval counts from the turn that fired, so each check keeps a second's margin around a due time. The Cron
        // fires every 30 seconds on the clock; a jump over several of its occurrences fires it once, without the backlog.
        var cron = 0;
        async Task<bool> CronOnce()
        {
            var now = (await Runs("cron-tick")).Finished;
            var once = now == cron + 1;
            cron = now;
            return once;
        }

        await Step(59_000);
        check((await Runs("tick")).Finished == 0 && await CronOnce(),
            "no Timer run before its interval; a Cron whose owner comes late fires the due occurrence once, without the backlog");
        await Step(2_000);     // no Cron occurrence falls in these two seconds
        var flow = await Runs("flow-tick");
        var deep = await Runs("deep-start");
        check((await Runs("tick")).Finished == 1 && flow is { Finished: 1, Running: 0 } && deep is { Finished: 1, Running: 0 },
            $"at its interval the Timer starts one run; a Flowchart run begins at its Timer, or reaches it through its start node, and finishes (tick {(await Runs("tick")).Finished}, flow {flow}, deep {deep})");

        var again = await SetAll();
        await Step(61_000);
        check(again.All(o => o == Contract.AdmissionOutcome.Duplicate) && (await Runs("tick")).Finished == 2 && await CronOnce(),
            $"setting the same schedules again is a duplicate, and the next interval starts one run ({string.Join(",", again)}; {(await Runs("tick")).Finished})");

        // Each occurrence starts one run when a turn's commit is lost before or after it landed.
        await Jump(61_000);
        await driver.LoseTurnCommit(landed: false);
        await driver.PassAll(4);
        check((await Runs("tick")).Finished == 3 && (await Runs("flow-tick")).Finished == 3 && await CronOnce(),
            $"with a turn's commit lost, each due occurrence starts one run when its turn runs again ({(await Runs("tick")).Finished}, {(await Runs("flow-tick")).Finished})");
        await Jump(61_000);
        await driver.LoseTurnCommit(landed: true);
        await driver.PassAll(4);
        check((await Runs("tick")).Finished == 4 && (await Runs("flow-tick")).Finished == 4 && await CronOnce(),
            $"with a turn's commit landed unseen, no occurrence starts a second run ({(await Runs("tick")).Finished}, {(await Runs("flow-tick")).Finished})");

        // An owner that is late by five intervals fires the due occurrence once and counts the next one from then.
        await Step(5 * 60_000);
        var late = (await Runs("tick")).Finished;
        await Step(59_000);
        var quiet = (await Runs("tick")).Finished;
        await Step(2_000);
        check(late == 5 && quiet == 5 && (await Runs("tick")).Finished == 6,
            $"after five missed intervals the due occurrence fires once, and the next comes an interval later ({late}, {quiet}, {(await Runs("tick")).Finished})");

        // A newer revision replaces the schedule, an older one changes nothing, and an unchanged one keeps its timer.
        var tick = table.All.Single(s => s.Workflow == "tick");
        ValueTask<Contract.Admission> SetTick(long revision, string interval) =>
            ScheduleActor.SetAsync(driver.Ingress, scope, tick.Key, revision, new RecurringSchedule(RecurringSchedule.Interval, interval), tick.StartKey);
        var changed = await SetTick(10, "PT2M");
        await driver.PassAll(4);
        await Step(61_000);
        var halfway = (await Runs("tick")).Finished;
        await Step(61_000);
        check(changed.Outcome == Contract.AdmissionOutcome.Accepted && halfway == 6 && (await Runs("tick")).Finished == 7,
            $"a changed interval replaces the timer: nothing at the old interval, one run at the new ({halfway}, {(await Runs("tick")).Finished})");
        var back = await SetTick(20, "PT1M");
        var older = await SetTick(15, "PT5M");
        await driver.PassAll(4);
        await Step(61_000);
        var restored = (await Runs("tick")).Finished;
        var repeat = await SetTick(10, "PT2M");
        check(back.Outcome == Contract.AdmissionOutcome.Accepted && older.Outcome == Contract.AdmissionOutcome.Accepted && restored == 8 &&
              repeat.Outcome == Contract.AdmissionOutcome.Duplicate,
            $"a change back applies, an older revision sent late changes nothing, and a repeat is a duplicate ({restored}, {repeat.Outcome})");
        await Step(30_000);
        await SetTick(30, "PT1M");     // republished with the schedule unchanged, halfway to the next occurrence
        await driver.PassAll(4);
        await Step(31_000);
        check((await Runs("tick")).Finished == 9, $"an unchanged schedule set again keeps its pending occurrence ({(await Runs("tick")).Finished})");

        // Stop disarms the schedule and leaves it to be set again; Terminate ends it for good.
        var stopped = await ScheduleActor.StopAsync(driver.Ingress, scope, tick.Key, 40);
        await driver.PassAll(4);
        await Step(10 * 60_000);
        var schedule = (await List(ScheduleActor.ActorType)).Single(v => v.Header.TypeData.Contains("\"Revision\":30", StringComparison.Ordinal));
        check(stopped.Outcome == Contract.AdmissionOutcome.Accepted && (await Runs("tick")).Finished == 9 && schedule.Header.Status == Contract.ActorStatus.Running,
            $"a stopped schedule starts no more runs and stays, idle ({(await Runs("tick")).Finished}, {schedule.Header.Status})");
        var rearmed = await SetTick(50, "PT1M");
        await driver.PassAll(4);
        await Step(61_000);
        check(rearmed.Outcome == Contract.AdmissionOutcome.Accepted && (await Runs("tick")).Finished == 10,
            $"a stopped schedule set again fires again ({(await Runs("tick")).Finished})");
        var tickActor = (await List(ScheduleActor.ActorType)).Single(v => v.Header.TypeData.Contains("\"Revision\":50", StringComparison.Ordinal)).Header.Ref;
        var terminated = await driver.Ingress.SendAsync(scope, new Contract.Target.Ref(tickActor), "terminate-tick", Contract.Kinds.Terminate);
        await driver.PassAll(4);
        await SetTick(60, "PT1M");
        await driver.PassAll(4);
        await Step(5 * 60_000);
        var ended = (await List(ScheduleActor.ActorType)).FirstOrDefault(v => v.Header.Status != Contract.ActorStatus.Running);
        check(terminated.Outcome == Contract.AdmissionOutcome.Accepted && ended?.Header.Status == Contract.ActorStatus.Cancelled && (await Runs("tick")).Finished == 10,
            $"Terminate ends the schedule, and a Set after it starts nothing ({terminated.Outcome} {terminated.Reason}, {ended?.Header.Status}, {(await Runs("tick")).Finished})");

        // A run registration removed (an unpublished definition) leaves the occurrence nothing to start.
        var flowSchedule = table.All.Single(s => s.Workflow == "flow-tick");
        var flowBefore = (await Runs("flow-tick")).Finished;
        await ScheduleActor.RegisterRunAsync(driver.Ingress, scope, flowSchedule.StartKey, null);
        await Step(2 * 61_000);
        check((await Runs("flow-tick")).Finished == flowBefore, $"without its run registered, an occurrence starts nothing ({flowBefore}, {(await Runs("flow-tick")).Finished})");
        check((await Runs("tick")).Running == 0 && (await Runs("flow-tick")).Running == 0 && (await Runs("cron-tick")).Running == 0 && (await Runs("deep-start")).Running == 0, "every scheduled run finished");
        await ManagedOccurrences(check);
        await RecoverManagedOffer(check);
    }

    private static async Task ManagedOccurrences(Action<bool, string> check)
    {
        var acceptor = new ManagedAcceptor();
        var actor = new ObservedSchedule(new ScheduleActor(new Calendar(), acceptor));
        // Bind a legacy run at the same key so an accidental Publish would create a visible workflow.
        var workflow = new PublicWorkflow([new NativeExecutionModel(Registry())]);
        await using var driver = new ContractWorkload.InMemoryDriver(4, Flaw.None, workflow,
            authorizeOperator: (_, _) => true, alsoTypes: [actor]);
        var scope = ContractScopes.Bind("managed-schedules");
        await driver.TakeAll();
        await ScheduleActor.RegisterRunAsync(driver.Ingress, scope, "legacy-start", new NativeSchedules(Registry(), CronError).All.Single(x => x.Workflow == "tick").Start());
        var first = new ScheduleHeader(new(RecurringSchedule.Interval, "PT1S"), "legacy-start", 10) { Authority = "blue" };
        var born = await driver.Ingress.SendAsync(scope, new Contract.Target.Key("managed", new(ScheduleActor.ActorType,
            JsonSerializer.Serialize(first), History: Contract.History.Minimal)), "managed-start", ScheduleActor.SetKind);
        var reference = born.Actor ?? throw new InvalidOperationException("The managed schedule was not admitted.");
        var target = new Contract.Target.Ref(reference);
        await driver.PassAll(4);

        var timer = actor.Plans.Last().Timers.Single();
        // Ordinary ingress can match the due payload and kind, but cannot present the authenticated timer's identity.
        await driver.Ingress.SendAsync(scope, target, "forged-fire", timer.Kind, timer.Payload);
        await driver.PassAll(4);
        check(acceptor.Offered.IsEmpty && actor.Plans.Last().Timers.Single() == timer,
            "managed schedule ignores a caller-supplied Fire with the exact due payload");

        driver.Advance(timer.Due - driver.Now);
        await driver.TakeOver();
        await driver.PassAll(6);
        var original = acceptor.Offered.Single();
        var recipe = actor.Plans.SelectMany(plan => plan.Jobs).Single();
        check(original is { ArmGeneration: 1, Authority: "blue" } && original.Tenant == scope.Tenant &&
              original.Schedule == reference && original.TimerDeliveryId == timer.Id && original.DueAtUnixMilliseconds == timer.Due &&
              JsonSerializer.Deserialize<ScheduleOccurrence>(recipe.Arguments!) == original &&
              recipe.Deadline == original.OfferUntilUnixMilliseconds &&
              original.OfferUntilUnixMilliseconds > timer.Due && original.OfferUntilUnixMilliseconds - timer.Due <= 60_100 &&
              actor.Plans.All(plan => plan.Jobs.Count + plan.Timers.Count <= 1),
            "authentic Fire binds tenant, actor, arm, timer and deadline in one durable job and in its Park restoration recipe");
        await CheckForeignJob(actor, recipe, original, acceptor, check);

        var retry = actor.Plans.Last().Timers.Single();
        await driver.TakeOver();
        await driver.PassAll(4);
        check(acceptor.Offered.Count == 1 && retry.Due < original.OfferUntilUnixMilliseconds,
            "the replacement owner preserves the retry timer after an unknown acknowledgement");
        driver.Advance(retry.Due - driver.Now);
        await driver.TakeOver();
        await driver.PassAll(6);
        check(acceptor.Offered.SequenceEqual([original, original]) && acceptor.Accepted.Count == 1 &&
              actor.Plans.SelectMany(plan => plan.Jobs).Last() == recipe,
            "replay uses the identical offer and original deadline and observes the single committed acceptance");

        var second = actor.Plans.Last().Timers.Single();
        driver.Advance(second.Due - driver.Now);
        await driver.TakeOver();
        await driver.PassAll(6);
        check(acceptor.Offered.Count == 3 && acceptor.Offered.Last() is { ArmGeneration: 2, Authority: "blue" },
            "a subsequent occurrence owns its own generation");
        var moved = first with { Revision = 11, Authority = "green" };
        await driver.Ingress.SendAsync(scope, target, "managed-move", ScheduleActor.SetKind, JsonSerializer.Serialize(moved));
        await driver.Ingress.SendAsync(scope, target, "unlate-blue", ScheduleActor.StopKind, JsonSerializer.Serialize(new ScheduleStop(10)));
        await driver.PassAll(4);
        check(actor.Plans.Last() is { Timers.Count: 0, Jobs.Count: 1 } &&
              (await driver.Reads.GetAsync(scope, reference))?.Header.TypeData == JsonSerializer.Serialize(moved),
            "a newer Set supersedes the in-flight arm without occupying a second job slot and an older Stop leaves it armed");

        // Losing this callback's acknowledgement would normally retry, but the arm has been superseded.
        acceptor.ReleaseSecond.SetException(new IOException("the old acknowledgement was lost"));
        var waitUntil = Environment.TickCount64 + 5_000;
        while (Environment.TickCount64 < waitUntil)
        {
            await driver.PassAll(1);
            if (actor.Plans.Last().Timers.Count != 0) break;
            await Task.Delay(1);
        }
        var newTimer = actor.Plans.Last().Timers.Single();
        driver.Advance(newTimer.Due - driver.Now);
        await driver.TakeOver();
        await driver.PassAll(6);
        check(acceptor.Offered.Count == 4 && acceptor.Offered.Last() is { ArmGeneration: 3, Authority: "green" } green &&
              green.TimerDeliveryId == newTimer.Id && green.TimerDeliveryId != second.Id &&
              acceptor.Accepted.Count == 1 && actor.Plans.Last().Timers.Single().Kind == ScheduleActor.FireKind,
            "the displaced callback does not retry, and a definitive refusal of the new arm continues the schedule");

        await driver.Ingress.SendAsync(scope, target, "managed-stop", ScheduleActor.StopKind, JsonSerializer.Serialize(new ScheduleStop(11)));
        await driver.Ingress.SendAsync(scope, target, "late-green", ScheduleActor.SetKind, JsonSerializer.Serialize(moved));
        await driver.PassAll(4);
        driver.Advance(80_000);
        await driver.TakeOver();
        await driver.PassAll(6);
        var workflows = await driver.Reads.ListAsync(scope, new Contract.ActorQuery(Type: PublicWorkflow.ActorType));
        check(acceptor.Offered.Count == 4 && actor.Plans.Last() is { Timers.Count: 0, Jobs.Count: 0 } && workflows.Items.Count == 0,
            "an equal-revision Set cannot undo Stop, and neither acceptance nor refusal calls the legacy publication path");

        await driver.Ingress.SendAsync(scope, target, "managed-after-stop", ScheduleActor.SetKind,
            JsonSerializer.Serialize(moved with { Revision = 12, Authority = "expiry" }));
        await driver.PassAll(4);
        var afterStop = actor.Plans.Last().Timers.Single();
        driver.Advance(afterStop.Due - driver.Now);
        await driver.TakeOver();
        await driver.PassAll(6);
        var expiring = acceptor.Offered.Last();
        check(expiring is { ArmGeneration: 5, Authority: "expiry" } &&
              actor.Plans.Last().Jobs.Single().Deadline == expiring.OfferUntilUnixMilliseconds,
            "a higher revision re-arms a stopped schedule and offers one unsettled timed job");
        driver.Advance(expiring.OfferUntilUnixMilliseconds - driver.Now);
        await driver.TakeOver();
        await driver.PassAll(6);
        var recovery = actor.Plans.Last().Timers.Single();
        check(recovery.Kind != ScheduleActor.FireKind && recovery.Due > expiring.OfferUntilUnixMilliseconds,
            "an expired acceptance releases its job slot and keeps the original due occurrence queued for retry");
        driver.Advance(recovery.Due - driver.Now);
        await driver.TakeOver();
        await driver.PassAll(6);
        var renewed = actor.Plans.SelectMany(plan => plan.Jobs).Last();
        check(acceptor.Offered.TakeLast(2).SequenceEqual([expiring, expiring]) &&
              acceptor.Accepted.Keys.Count(offer => offer == expiring) == 1 && acceptor.Offered.Count == 6 &&
              renewed.Deadline - recovery.Due is >= 60_000 and <= 60_100 &&
              actor.Plans.Last().Timers.Single() is { Kind: ScheduleActor.FireKind } next && next.Due > recovery.Due,
            "the new owner accepts the exact due occurrence once in a fresh bounded attempt, then skips intervening due times");

        await driver.Ingress.SendAsync(scope, target, "managed-terminate", Contract.Kinds.Terminate);
        await driver.PassAll(4);
        driver.Advance(80_000);
        await driver.TakeOver();
        await driver.PassAll(6);
        check(acceptor.Offered.Count == 6 && (await driver.Reads.GetAsync(scope, reference))?.Header.Status == Contract.ActorStatus.Cancelled,
            "Terminate ends a managed actor with a future arm and the timer cannot offer afterward");
    }

    private static async Task CheckForeignJob(ObservedSchedule actor, Contract.JobSpec recipe, ScheduleOccurrence offer, ManagedAcceptor acceptor, Action<bool, string> check)
    {
        try
        {
            await actor.ExecuteJobAsync(new TestedJob(new("wrong-schedule"), recipe.Key, recipe.Arguments, recipe.Deadline), CancellationToken.None);
            check(false, "a callback carrying another actor's offer is refused");
        }
        catch (InvalidOperationException)
        {
            check(acceptor.Offered.SequenceEqual([offer]), "a callback carrying another actor's offer is refused before consulting the host");
        }
    }

    private static async Task RecoverManagedOffer(Action<bool, string> check)
    {
        const long initial = 1_703_010_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = ContractScopes.Bind("parked-schedule");
        var acceptor = new ParkedAcceptor();
        var actor = new ObservedSchedule(new ScheduleActor(null, acceptor))
        {
            ObserveJob = job => acceptor.Deadlines.Enqueue(job.Deadline)
        };
        var edge = new InMemoryIngress(store, new Requests("schedule-park"), "schedule-park", (_, kind) => kind == Contract.Kinds.Recover);
        var reads = new ActorList(store, 7);
        Worker NewWorker(string node) => new(node, 0, store, new ActorAdapter(store, actor),
            new WorkerOptions { Monotonic = () => now - initial, MaxAttempts = 2 });
        var first = NewWorker("before-schedule-Park");
        await using var original = (await new DurableHost([first]).ActivateAsync(0))!;
        var header = new ScheduleHeader(new(RecurringSchedule.Interval, "PT1S"), "unused", 1) { Authority = "pinned" };
        var started = await edge.SendAsync(scope, new Contract.Target.Start(new(actor.Type, JsonSerializer.Serialize(header))),
            "schedule-birth", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(started.Actor!.Value);
        await Pump(original);
        now = actor.Plans.Last().Timers.Single().Due;
        await Pump(original);
        var offer = acceptor.Offered.Single();
        var job = actor.Plans.Last().Jobs.Single();

        await edge.SendAsync(scope, target, "schedule-fault", ObservedSchedule.ParkKind);
        await Pump(original);
        var parked = await reads.GetAsync(scope, target.Actor);
        check(parked is { Park: not null, Selection: not null } && acceptor.Offered.Count == 1,
            "the schedule Parks after committing its offer while the callback is in flight");
        if (parked is not { Park: not null, Selection: { } selected }) return;
        await original.DisposeAsync();

        var next = NewWorker("after-schedule-Park");
        await using var replacement = (await new DurableHost([next]).ActivateAsync(0))!;
        await Pump(replacement);
        var admitted = await edge.SendAsync(scope, target, "recover-schedule", Contract.Kinds.Recover,
            JsonSerializer.Serialize(new Contract.OperatorCommand(selected)));
        await Pump(replacement);
        check(admitted.Outcome == Contract.AdmissionOutcome.Accepted &&
              (await reads.GetAsync(scope, target.Actor)) is { Header.Status: Contract.ActorStatus.Running, Park: null } &&
              acceptor.Offered.SequenceEqual([offer, offer]) && acceptor.Deadlines.SequenceEqual([job.Deadline, job.Deadline]),
            "Recover restarts the exact parked offer with the original timed-job deadline on the replacement owner");

        static async Task Pump(IScopeActivation owner)
        {
            for (var i = 0; i < 12; i++)
            {
                await owner.WokenAsync(null);
                await owner.RunAsync();
                await Task.Yield();
            }
        }
    }

    private sealed record TestedJob(Contract.ActorRef Actor, string Key, string? Arguments, long? Deadline) : Contract.IJob
    {
        public string AttemptId => "fixture-attempt";
        public int AttemptNumber => 1;
    }

    private sealed class ManagedAcceptor : IScheduleOccurrenceAcceptor
    {
        private readonly TaskCompletionSource<bool> _untilDeadline = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<ScheduleOccurrence> Offered { get; } = new();
        public ConcurrentDictionary<ScheduleOccurrence, byte> Accepted { get; } = new();
        public TaskCompletionSource<bool> ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<bool> TryAcceptAsync(ScheduleOccurrence occurrence, CancellationToken cancellationToken) =>
            TryAcceptAttemptAsync(occurrence, occurrence.OfferUntilUnixMilliseconds, cancellationToken);

        public ValueTask<bool> TryAcceptAttemptAsync(ScheduleOccurrence occurrence, long attemptDeadline, CancellationToken cancellationToken)
        {
            Offered.Enqueue(occurrence);
            if (occurrence is { ArmGeneration: 1, Authority: "blue" })
            {
                if (Accepted.TryAdd(occurrence, 0)) throw new IOException("the committed acceptance acknowledgement was lost");
                return ValueTask.FromResult(true);
            }
            if (occurrence is { ArmGeneration: 2, Authority: "blue" }) return new(ReleaseSecond.Task.WaitAsync(cancellationToken));
            if (occurrence.Authority == "expiry")
            {
                if (attemptDeadline == occurrence.OfferUntilUnixMilliseconds) return new(_untilDeadline.Task.WaitAsync(cancellationToken));
                Accepted.TryAdd(occurrence, 0);
                return ValueTask.FromResult(true);
            }
            return ValueTask.FromResult(false);
        }
    }

    private sealed class ParkedAcceptor : IScheduleOccurrenceAcceptor
    {
        private readonly TaskCompletionSource<bool> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<ScheduleOccurrence> Offered { get; } = new();
        public ConcurrentQueue<long?> Deadlines { get; } = new();

        public ValueTask<bool> TryAcceptAsync(ScheduleOccurrence occurrence, CancellationToken cancellationToken)
        {
            Offered.Enqueue(occurrence);
            return new(_pending.Task.WaitAsync(cancellationToken));
        }
    }

    private sealed class ObservedSchedule(ScheduleActor actor) : Contract.IActorType
    {
        internal const string ParkKind = "fixture.schedule.park";
        public List<Contract.RestorePlan> Plans { get; } = [];
        public Action<Contract.IJob>? ObserveJob { get; init; }
        public string Type => actor.Type;
        public bool Withholds(Contract.ActorHeader header) => actor.Withholds(header);

        public async ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox.Any(message => message.Kind == ParkKind)) throw new InvalidOperationException("park the managed schedule");
            await actor.TurnAsync(turn, cancellationToken);
            if (turn.State is { } state) Plans.Add(actor.RestoreWaits(turn.Header, state));
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            ObserveJob?.Invoke(job);
            return actor.ExecuteJobAsync(job, cancellationToken);
        }
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => actor.RestoreWaits(header, state);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => actor.TerminalGrowth(header, state);
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => actor.ProtectsDeletion(header, state, now);
    }

    private static bool Argument(Func<ValueTask<Contract.Admission>> set)
    {
        try { set().AsTask().GetAwaiter().GetResult(); return false; }
        catch (ArgumentException) { return true; }
    }

    private static bool Refused(Action build)
    {
        try { build(); return false; }
        catch (NativeScheduleException) { return true; }
    }
}
