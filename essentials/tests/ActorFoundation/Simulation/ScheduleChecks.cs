using System.Text.Json.Nodes;
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
            var runs = (await List(PublicWorkflow.ActorType)).Where(v => JsonNode.Parse(v.Header.TypeData)?["Definition"]?.GetValue<string>() == workflow).ToList();
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
