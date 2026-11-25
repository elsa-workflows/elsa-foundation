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
        // A Timer that is not where a run begins is no schedule.
        .Add(Workflow("mid", Sequence("root", Set("a", "x", 1), Timer("t", "PT1M"))));

    public static async Task Run(Action<bool, string> check)
    {
        var registry = Registry();
        var table = new NativeSchedules(registry, CronError);
        check(table.All.Select(s => $"{s.Workflow}/{s.Node}/{s.Kind}").SequenceEqual(["cron-tick/c/cron", "flow-tick/t/interval", "tick/t/interval"]),
            $"a Timer or Cron where a run begins is a schedule, and one in the middle of a workflow is none ({string.Join(", ", table.All.Select(s => s.Key))})");
        check(Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("zero", Timer("t", "PT0S"))))) &&
              Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("bad", Timer("t", "soon"))))) &&
              Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("computed", Node<Kernel.Native.Timer>("t", ("Interval", Binding.Expr("'PT1M'")))))) ) &&
              Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("cron", Cron("c", "* * *"))), CronError)) &&
              Refused(() => new NativeSchedules(new WorkflowRegistry().Add(Workflow("no-calendar", Cron("c", EveryHalfMinute))))),
            "an interval that is not positive or does not read, a computed one, and a cron expression the host does not read are refused");

        var model = new NativeExecutionModel(registry);
        await using var driver = new ContractWorkload.InMemoryDriver(4, Flaw.None, new PublicWorkflow([model]), alsoTypes: [new ScheduleActor(new Calendar())]);
        var scope = ContractScopes.Bind("schedules");
        await driver.TakeAll();

        async Task<Contract.AdmissionOutcome[]> SetAll() =>
            [.. await Task.WhenAll(table.All.Select(s =>
                ScheduleActor.SetAsync(driver.Ingress, scope, s.Key, "v1", new RecurringSchedule(s.Kind, s.Value), s.Start()).AsTask())).ContinueWith(t => t.Result.Select(a => a.Outcome))];
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
        check(created.All(o => o == Contract.AdmissionOutcome.Accepted) && (await List(ScheduleActor.ActorType)).Count == 3 && (await Runs("tick")).Finished == 0,
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
        check((await Runs("tick")).Finished == 1 && flow is { Finished: 1, Running: 0 },
            $"at its interval the Timer starts one run, and a Flowchart run begins at its Timer and finishes (tick {(await Runs("tick")).Finished}, flow {flow})");

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

        // A changed schedule replaces the timer; a stopped one fires no more.
        var tick = table.All.Single(s => s.Workflow == "tick");
        var changed = await ScheduleActor.SetAsync(driver.Ingress, scope, tick.Key, "v2", new RecurringSchedule(RecurringSchedule.Interval, "PT2M"), tick.Start());
        await driver.PassAll(4);
        await Step(61_000);
        var halfway = (await Runs("tick")).Finished;
        await Step(61_000);
        check(changed.Outcome == Contract.AdmissionOutcome.Accepted && halfway == 6 && (await Runs("tick")).Finished == 7,
            $"a changed interval replaces the timer: nothing at the old interval, one run at the new ({halfway}, {(await Runs("tick")).Finished})");
        var back = await ScheduleActor.SetAsync(driver.Ingress, scope, tick.Key, "v3", new RecurringSchedule(RecurringSchedule.Interval, "PT1M"), tick.Start());
        await driver.PassAll(4);
        await Step(61_000);
        var restored = (await Runs("tick")).Finished;
        var changedAgain = await ScheduleActor.SetAsync(driver.Ingress, scope, tick.Key, "v2", new RecurringSchedule(RecurringSchedule.Interval, "PT2M"), tick.Start());
        check(back.Outcome == Contract.AdmissionOutcome.Accepted && restored == 8 && changedAgain.Outcome == Contract.AdmissionOutcome.Duplicate,
            $"a change back to an earlier schedule applies, and a repeat of an earlier operation is a duplicate ({back.Outcome}, {restored}, {changedAgain.Outcome})");
        await Step(61_000);
        var stopped = await ScheduleActor.StopAsync(driver.Ingress, scope, tick.Key, "stop-tick");
        await driver.PassAll(4);
        await Step(10 * 60_000);
        var schedule = (await List(ScheduleActor.ActorType)).Single(v => v.Header.TypeData.Contains("\"PT1M\"", StringComparison.Ordinal));
        check(stopped.Outcome == Contract.AdmissionOutcome.Accepted && (await Runs("tick")).Finished == 9 && schedule.Header.Status == Contract.ActorStatus.Finished,
            $"a stopped schedule ends and starts no more runs ({(await Runs("tick")).Finished}, {schedule.Header.Status})");
        check((await Runs("tick")).Running == 0 && (await Runs("flow-tick")).Running == 0 && (await Runs("cron-tick")).Running == 0, "every scheduled run finished");
    }

    private static bool Refused(Action build)
    {
        try { build(); return false; }
        catch (NativeScheduleException) { return true; }
    }
}
