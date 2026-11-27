using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Native;
using Kernel.Sim.Native;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

/// <summary>Replays the simulation's original Native definitions through the public actor and workflow contracts.</summary>
internal static class ContractNativeWorkload
{
    private sealed record Started(string Workflow, string Key, int Signals, Contract.ActorRef Ref);
    private static readonly Dictionary<string, (string Node, int Count)[]> Completions = new(StringComparer.Ordinal)
    {
        ["n-seq"] = [("http", 1), ("mail", 1), ("then", 1), ("else", 0)],
        ["n-join"] = [("e1", 1), ("e2", 1), ("e3", 1), ("par", 1)],
        ["n-join2"] = [("par", 1)],
        ["n-flow"] = [("f2", 1), ("f3", 1), ("f4", 1)],
        ["n-dispatch"] = [("c1", 1), ("c2", 1), ("c3", 1)],
        ["n-loop"] = [("d", 3), ("add", 3), ("inc", 2), ("svc", 1)],
        ["n-pub"] = [("p1", 1), ("p2", 1)],
        ["n-graph"] = [("a", 3), ("b", 3), ("join", 3), ("yes", 1), ("no", 0), ("end", 1)],
        ["n-out"] = [("h1", 2), ("m1", 1), ("h2", 2), ("hj", 2)]
    };

    public static async Task<int> Run(int from, int seeds, int partitions, Flaw flaws, NativeFlaw nativeFlaws)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seeds);
        for (var seed = from; seed < checked(from + seeds); seed++)
        {
            var trace = new List<string>();
            try
            {
                await Replay(seed, partitions, flaws, nativeFlaws, trace);
            }
            catch (InvalidOperationException error)
            {
                Console.Error.WriteLine($"Public Native workload seed {seed} failed at {partitions} virtual partitions ({flaws}, {nativeFlaws}). {error.Message}");
                Console.Error.WriteLine(string.Join(Environment.NewLine, trace));
                return 1;
            }
        }

        Console.WriteLine($"Public Native workload passed {seeds} seeds from {from} at {partitions} virtual partitions. Each seed ran {NativeWorkflows.Mix.Length} original Native workflows.");
        return 0;
    }

    private static async Task Replay(int seed, int partitions, Flaw flaws, NativeFlaw nativeFlaws, List<string> trace)
    {
        var random = new Random(seed);
        var sends = new List<NativeOutbound>();
        var model = new NativeExecutionModel(NativeWorkflows.Registry, new() { Flaws = nativeFlaws }, sends.Add);
        await using ContractWorkload.IDriver driver = new ContractWorkload.InMemoryDriver(partitions, flaws, new PublicWorkflow([model]));
        var scope = ContractScopes.Bind("native");
        var runtime = new PublicRuntime(driver.Ingress, driver.Reads);
        var started = new List<Started>(NativeWorkflows.Mix.Length);

        foreach (var (workflow, _) in NativeWorkflows.Mix)
            if (workflow is not ("n-seq" or "n-pub")) await Start(workflow, Mode());

        await driver.Start(started.Select(run => (scope, run.Ref)).ToArray());
        await driver.Pass(24);
        var loop = started.Single(run => run.Workflow == "n-loop");
        var before = await PendingTimer(loop);
        trace.Add($"take over from the first owner at {driver.Now}, with n-loop due at {before.Due}");
        await driver.TakeOver();
        await driver.Pass(12);
        var after = await PendingTimer(loop);
        Require(before.Handle == after.Handle && before.Due == after.Due && driver.Now < after.Due &&
            !(await ReadJournal(driver.Reads, scope, loop.Ref)).Any(entry => entry.EndsWith(":d", StringComparison.Ordinal)),
            "takeover changed or released the original Native timer before its due time");

        var effects = await Start("n-seq", Modes.Balanced);
        await driver.Start([(scope, effects.Ref)]);
        trace.Add("lose a Balanced outbound turn before its commit executes");
        await driver.LoseTurnCommit(landed: false);
        await driver.Pass(16);
        var replayed = sends.Where(send => send.Actor == effects.Ref).ToList();
        Require(replayed.Count(send => send.Node == "http") >= 2 && replayed.Count(send => send.Node == "mail") >= 2,
            "the lost write did not replay both real Native outbound effects");
        CheckEffects(effects, replayed, [("http", 1), ("mail", 1)]);

        // n-flow has committed its waits. Only n-pub's own events can move it past them.
        var flow = started.Single(run => run.Workflow == "n-flow");
        var publisher = await Start("n-pub", Modes.Balanced, flow.Key);
        await driver.Start([(scope, publisher.Ref)]);
        trace.Add("lose the reply to a landed publisher turn");
        await driver.LoseTurnCommit(landed: true);
        await driver.Pass(16);

        for (var round = 0; round < 64; round++)
        {
            var views = await Views();
            if (views.All(view => view?.Header.Status == Contract.ActorStatus.Finished)) break;
            var signals = started.Zip(views)
                .Where(pair => pair.First.Workflow != "n-flow" && pair.Second?.Header.Status == Contract.ActorStatus.Running)
                .SelectMany(pair => Enumerable.Range(1, pair.First.Signals).Select(slot => $"{pair.First.Key}-{slot}")).ToArray();
            random.Shuffle(signals);
            for (var i = 0; i < signals.Length; i++)
            {
                var signal = signals[i];
                var target = new Contract.Target.Event($"event:{signal}");
                var result = await driver.Ingress.SendAsync(scope, target, $"signal-{round}-{i}", Contract.Kinds.Event);
                Require(result.Outcome == Contract.AdmissionOutcome.Accepted, $"outside publication of {signal} was refused");
            }

            trace.Add($"round {round} at {driver.Now} signals [{string.Join(", ", signals)}]");
            if (random.Next(5) == 0) await driver.TakeOver();
            else driver.Advance(random.Next(20, 41));
            await driver.Pass(random.Next(8, 15));
        }

        await driver.Pass(24);
        foreach (var run in started)
        {
            var entries = await ReadJournal(driver.Reads, scope, run.Ref);
            var view = await driver.Reads.GetAsync(scope, run.Ref);
            trace.Add($"check {run.Workflow} at {view?.Header.Status} ({view?.Header.Fault})");
            CheckJournal(run.Workflow, entries);
            Require(view is { Header.Status: Contract.ActorStatus.Finished, Park: null },
                $"{run.Workflow} status was {view?.Header.Status} ({view?.Header.Fault})");
            if (run.Workflow == "n-dispatch") await CheckChildren(driver, scope, run.Ref, entries);
        }
        CheckEffects(started.Single(run => run.Workflow == "n-out"), sends, [("h1", 2), ("m1", 1), ("h2", 2), ("hj", 2)]);

        string Mode() => random.Next(2) == 0 ? Modes.Balanced : Modes.Durable;

        async Task<Started> Start(string workflow, string mode, string? publishTo = null)
        {
            var key = $"run-{workflow}";
            var input = new JsonObject { ["key"] = key, ["target"] = publishTo };
            var plan = NativeExecutionModel.Start(workflow, mode, input);
            var receipt = await runtime.StartAsync(scope, "start", plan, callerKey: key);
            Require(receipt is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, $"{workflow} start was refused");
            var result = new Started(workflow, key, NativeWorkflows.Mix.Single(item => item.Workflow == workflow).Events, receipt.Actor!.Value);
            started.Add(result);
            trace.Add($"start {workflow} in {mode} at {driver.Now}");
            return result;
        }

        async Task<Contract.DeliveryView> PendingTimer(Started actor)
        {
            var inbox = await runtime.InboxAsync(scope, actor.Ref);
            var timers = inbox.Items.Where(item => item.Kind == NativeKinds.Timer && item.State == Contract.DeliveryState.Pending).ToArray();
            Require(timers is [{ Due: not null }], $"{actor.Workflow} must have exactly one pending Native timer");
            return timers[0];
        }

        async Task<Contract.ActorView?[]> Views()
        {
            var views = new Contract.ActorView?[started.Count];
            for (var i = 0; i < started.Count; i++) views[i] = await driver.Reads.GetAsync(scope, started[i].Ref);
            return views;
        }
    }

    private static void CheckJournal(string workflow, List<string> entries)
    {
        var completed = entries.Where(entry => entry.StartsWith("c:", StringComparison.Ordinal)).Select(entry => entry.Split(':')).ToArray();
        Require(completed.Select(entry => entry[1]).Distinct(StringComparer.Ordinal).Count() == completed.Length,
            $"{workflow} committed a frame completion twice");
        Require(entries.Count(entry => entry == "start") == 1 && entries.Count(entry => entry == "end") == 1 &&
            completed.Count(entry => entry[2] == "root") == 1 && completed.Count(entry => entry[2] == "out") == 1,
            $"{workflow} journal did not show one start, end and final activity");
        foreach (var (node, count) in Completions[workflow])
            Require(completed.Count(entry => entry[2] == node) == count, $"{workflow} journal did not complete {node} {count} time(s)");
        if (workflow == "n-join2")
            Require(completed.Count(entry => entry[2] is "n1" or "n2" or "n3") == 2,
                "n-join2 journal did not show exactly two winning branch increments");
        if (workflow == "n-graph")
        {
            Require(completed.Count(entry => entry[2] is "r1" or "r2") == 1,
                "n-graph journal did not show exactly one race winner");
            var finalJoin = entries.Select(entry => entry.Split('|')).Where(fields => fields is ["g", "f", _, "graph", "end", ..]).ToArray();
            Require(finalJoin is [[_, _, _, _, _, _, "yes", "no"]],
                "n-graph final join did not consume the live yes arrival and the dead no arrival");
        }
    }

    private static async Task CheckChildren(ContractWorkload.IDriver driver, Contract.BoundScope scope, Contract.ActorRef parent, List<string> entries)
    {
        var attached = entries.Where(entry => entry.StartsWith("s:", StringComparison.Ordinal)).Select(entry => entry.Split(':', 3)[2]).ToArray();
        var detached = entries.Where(entry => entry.StartsWith("d:", StringComparison.Ordinal)).Select(entry => entry.Split(':', 3)[2]).ToArray();
        var resumed = entries.Where(entry => entry.StartsWith("r:", StringComparison.Ordinal)).Select(entry => entry.Split(':', 3)[2]);
        var exactCompletions = attached.Select(id => $"{id}:{id}");
        Require(attached.Length == 2 && detached.Length == 1 && attached.Concat(detached).Distinct(StringComparer.Ordinal).Count() == 3 &&
            resumed.Order(StringComparer.Ordinal).SequenceEqual(exactCompletions.Order(StringComparer.Ordinal)),
            "n-dispatch did not record two exact child completions and one detached start");
        foreach (var id in attached.Concat(detached))
        {
            var child = await driver.Reads.GetAsync(scope, new(id));
            var isAttached = attached.Contains(id);
            // The detached child finishes independently of the parent's final turn.
            for (var wait = 0; !isAttached && (child is null or { Header.Status: Contract.ActorStatus.Running }) && wait < 20; wait++)
            {
                driver.Advance(30);
                await driver.Pass(8);
                child = await driver.Reads.GetAsync(scope, new(id));
            }
            if (child is not { Header.Status: Contract.ActorStatus.Finished } || child.Header.Parent != (isAttached ? parent : null))
            {
                var inbox = await driver.Reads.InboxAsync(scope, new(id));
                var journal = await ReadJournal(driver.Reads, scope, new(id));
                var rows = string.Join(", ", inbox.Items.Select(item => $"{item.Kind}/{item.State}/due {item.Due}/attempts {item.Attempts}"));
                throw new InvalidOperationException(
                    $"Native {(isAttached ? "attached" : "detached")} child had status {child?.Header.Status} at {driver.Now}, expected parent {child?.Header.Parent == parent} ({child?.Header.Fault}). Inbox [{rows}]. Journal [{string.Join(", ", journal)}].");
            }
        }
    }

    private static void CheckEffects(Started run, IReadOnlyList<NativeOutbound> sends, (string Node, int Count)[] expected)
    {
        var effects = sends.Where(send => send.Actor == run.Ref).ToList();
        Require(effects.All(effect => expected.Any(node => node.Node == effect.Node)), $"{run.Workflow} emitted an unexpected effect");
        foreach (var effect in effects)
        {
            var key = Models.EffectKey.For(run.Ref.Id, effect.Node, effect.IterationPath, effect.Attempt);
            var wire = effect.Effect switch
            {
                HttpSendEffect http => http.Request.Headers.GetValueOrDefault(SendHttpRequest.IdempotencyKeyHeader) == key,
                EmailSendEffect mail => mail.Mail.MessageId == SendEmail.MessageId(key),
                _ => false
            };
            Require(wire && effect.EffectKey == key, $"{run.Workflow} {effect.Node} effect key did not match its public actor and activation");
        }
        foreach (var (node, count) in expected)
            Require(effects.Where(effect => effect.Node == node).Select(effect => effect.EffectKey).Distinct(StringComparer.Ordinal).Count() == count,
                $"{run.Workflow} {node} did not use {count} stable effect key(s)");
    }

    private static async Task<List<string>> ReadJournal(Contract.IActorReads reads, Contract.BoundScope scope, Contract.ActorRef actor)
    {
        var entries = new List<string>();
        Contract.PageToken? token = null;
        for (var pageNumber = 0; pageNumber < 1_024; pageNumber++)
        {
            var page = await reads.TailAsync(scope, actor, token);
            if (page.Items.Count == 0) return entries;
            Require(page.Next is not null && page.Next != token, "Native journal returned a stationary cursor");
            token = page.Next;
            foreach (var entry in page.Items)
            {
                using var json = JsonDocument.Parse(entry.Entry);
                if (json.RootElement.ValueKind == JsonValueKind.String) entries.Add(json.RootElement.GetString()!);
            }
        }
        throw new InvalidOperationException("Native journal did not settle");
    }

    private static void Require(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }
}
