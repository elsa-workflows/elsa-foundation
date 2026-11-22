using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class HeldJobResultChecks
{
    private const string JobKey = "run";

    public static async Task Run(Action<bool, string> check, Flaw flaws)
    {
        foreach (var inline in new[] { false, true })
            foreach (var shape in new[] { "outside-release", "self-release", "poll-one", "singleton", "singleton-cancel", "send-cap" })
                await Schedule(inline, shape, flaws, (ok, name) => check(ok, $"{(inline ? "inline first write" : "durable")}, {shape}: {name}"));
        foreach (var shape in new[] { "cut-same-owner", "cut-takeover" })
            await Schedule(false, shape, flaws, (ok, name) => check(ok, $"{shape}: {name}"));
    }

    private static async Task Schedule(bool inline, string shape, Flaw flaws, Action<bool, string> check)
    {
        const long initial = 1_701_300_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var actor = new Probe(shape);
        var scope = new Contract.BoundScope("held-result");
        var requests = new Requests("first-owner");
        Worker WorkerFor(string node) => new(node, 0, store, new ActorAdapter(store, actor),
            new WorkerOptions { Monotonic = () => now - initial, PollLimit = shape == "poll-one" ? 1 : 500, Flaws = flaws });
        var worker = WorkerFor("first-owner");
        Contract.IIngress edge = new InMemoryIngress(store, requests, "first-owner", (selected, _) => selected == scope, inlineOwner: worker);
        var start = new Contract.Target.Start(new(actor.Type, "ready", null, Ephemeral: inline));
        using var http = new CancellationTokenSource();
        var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var request = inline ? edge.RequestAsync(scope, start, "start", Contract.Kinds.Start, null, null,
            new(null, now + 10_000, Inline: true), http.Token).AsTask() : null;
        try
        {
            var started = await edge.SendAsync(scope, start, "start", Contract.Kinds.Start);
            await Pump(owner);
            var target = new Contract.Target.Ref(started.Actor!.Value);
            var header = store.FoundationTarget(scope.Tenant, target.Actor.Id);
            check(actor.Entries == 1 && (header is null) == inline,
                "the original attempt enters with the expected checkpoint mode");

            var local = worker.Instances.Single().Row!;
            var driver = worker.JobCompletion(local.Id, JobKey, local.Creation)!;
            actor.Release.SetResult();
            await driver.WaitAsync(TimeSpan.FromSeconds(5));
            worker.RunTurn(local.Id);
            var withCut = shape.StartsWith("cut-", StringComparison.Ordinal);
            if (withCut)
            {
                await actor.EnteredHold.Task.WaitAsync(TimeSpan.FromSeconds(5));
                now++;
                var release = await edge.SendAsync(scope, target, "release", "release");
                var view = await new ActorList(store, 10).GetAsync(scope, target.Actor);
                now++;
                var terminate = await edge.SendAsync(scope, target, "terminate", Contract.Kinds.Terminate,
                    Json.Write(new Contract.OperatorCommand(view!.Selection!)));
                check(release.Outcome == Contract.AdmissionOutcome.Accepted && terminate.Outcome == Contract.AdmissionOutcome.Accepted,
                    "release and terminate are admitted during the paused Hold turn");
                actor.FinishHold.SetResult();
            }
            await worker.FlushAsync();

            var row = store.Instances.Values.Single();
            var ticket = InboxKey.Ticket(row.Id, JobKey, row.Creation);
            var result = InboxKey.JobResult(row.Id, JobKey, row.Creation);
            if (shape == "send-cap")
            {
                check(row.Status == InstanceStatus.Faulted && row.Fault?.Contains("10,000", StringComparison.Ordinal) == true &&
                    !store.Messages.Values.Any(delivery => delivery.Message.Kind == "overflow") && !store.Messages.ContainsKey(result),
                    "the conversion counts toward the turn send cap before any sends land");
                return;
            }
            check(row.State == "held" && row.Jobs is not null && actor.Handle is not null && actor.Applied == 0 &&
                !store.Messages.ContainsKey(ticket) && store.Messages.GetValueOrDefault(result) is { Held: true, Message.FoundationJobResult: true },
                "the landed Hold keeps the original completion and removes its runnable ticket");
            if (shape is "self-release" or "poll-one")
            {
                var reads = new ActorList(store, 1);
                var first = await reads.InboxAsync(scope, target.Actor);
                var second = first.Next is { } next ? await reads.InboxAsync(scope, target.Actor, next) : null;
                check(first.Items is [{ Kind: Contract.Kinds.JobFinished, State: Contract.DeliveryState.Held }] &&
                    second?.Items is [{ Kind: "release" }] && second.Next is null,
                    "paged inbox inspection agrees with delivery order across the held result and stimulus");
            }

            if (shape is "singleton" or "singleton-cancel")
            {
                var child = store.Messages.Values.Single(delivery => delivery.Message.Kind == Kinds.Start).Message.Target.Id;
                if (shape == "singleton-cancel" && !flaws.HasFlag(Flaw.HoldRetainsJobTicket))
                {
                    var checkpoint = new TurnCommit(row.Id, row with { Revision = row.Revision + 1 }, row.Revision,
                        [], [], [], [], [], Json.Write(new[] { "must not land" })) { FoundationReleasedHolds = [result] };
                    var differentTurn = new TurnCommit(child, null, null, [result], [], [], [], [], null);
                    var fence = worker.Fences.Single();
                    var batch = new CommitBatch(fence.Partition, fence.Epoch, fence.Position, TimeSpan.FromSeconds(10),
                        false, [differentTurn, checkpoint], [], []);
                    var refused = false;
                    try { await store.CommitAsync(batch); }
                    catch (StoreException error) when (error.Failure == Failure.Definite) { refused = true; }
                    check(refused && store.Instances[row.Id].Revision == row.Revision && store.Messages.GetValueOrDefault(result) is { Held: true } &&
                        !store.Journal.Values.Any(entry => entry.Revision > row.Revision && entry.Instance == row.Id),
                        "a cross-turn consume and release conflict is refused before any write");
                }
                await worker.PollAsync();
                await worker.LoadAsync();
                worker.RunTurn(child);
                await worker.FlushAsync();
                await worker.PollAsync();
                await worker.LoadAsync();
                worker.RunTurn(row.Id);
                await worker.FlushAsync();
                if (shape == "singleton-cancel")
                {
                    check(worker.Owns && store.Instances[row.Id] is { State: "cancelled job", Jobs: null } &&
                        !store.Messages.ContainsKey(result) && actor.Applied == 0 &&
                        store.Messages.Values.Count(message => message.Message.Kind == "committed-side-effect") == 1 &&
                        store.Journal.Values.Count(entry => entry.Entries == Json.Write(new[] { "cancelled in child notice" })) == 1,
                        "the singleton cancel lands atomically and adds no release for the consumed result");
                    return;
                }
                check(store.Instances[row.Id].State == "released" && store.Messages.GetValueOrDefault(result) is { Held: false },
                    "the singleton child notice durably releases the completion in its own commit");
            }

            if (withCut)
            {
                if (shape == "cut-same-owner") await Pump(owner);
                else
                {
                    await owner.DisposeAsync();
                    await using var takeover = (await new DurableHost([WorkerFor("successor")]).ActivateAsync(0))!;
                    await Pump(takeover);
                }
                check(actor.Offers.TakeLast(2).SequenceEqual(["job-finished,release", "kernel.terminate"]) &&
                    actor.Applied == 1 && actor.Entries == 1 && store.Instances[row.Id] is { State: "original", Status: InstanceStatus.Cancelled },
                    "the completion and its release precede the later terminate");
                return;
            }

            // Stand-down changes the epoch without relying on real time or a test delay.
            await owner.DisposeAsync();
            await using var successor = (await new DurableHost([WorkerFor("successor")]).ActivateAsync(0))!;
            var offered = actor.Offers.Count;
            if (shape == "outside-release")
            {
                await Pump(successor);
                check(actor.Offers.Count == offered, "a cold owner waits for a stimulus before offering the held completion");
                await edge.SendAsync(scope, target, "release", "release");
            }
            await Pump(successor);
            check(actor.Offers.Skip(offered).SequenceEqual([shape == "singleton" ? "job-finished" : "job-finished,release"]),
                "the first successor turn offers the held completion before its release");
            check(store.Instances[row.Id] is { State: "original", Jobs: null } &&
                actor.Entries == 1 && actor.Applied == 1 && actor.Handle == actor.AppliedHandle &&
                !store.Messages.ContainsKey(ticket) && !store.Messages.ContainsKey(result),
                "the successor applies the original payload under the same handle once without running the job");
        }
        finally
        {
            await owner.DisposeAsync();
            if (request is not null)
            {
                await http.CancelAsync();
                try { await request; }
                catch (OperationCanceledException) when (http.IsCancellationRequested) { }
            }
        }
    }

    private static async Task Pump(IScopeActivation owner)
    {
        for (var step = 0; step < 15; step++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class Probe(string shape) : Contract.IActorType
    {
        public string Type => "hold-job-result";
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource EnteredHold { get; } = new();
        public TaskCompletionSource FinishHold { get; } = new();
        public int Entries { get; private set; }
        public int Applied { get; private set; }
        public string? Handle { get; private set; }
        public string? AppliedHandle { get; private set; }
        public List<string> Offers { get; } = [];

        public async ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Header.TypeData == "child")
            {
                turn.End(Contract.ActorStatus.Finished);
                return;
            }
            Offers.Add(string.Join(',', turn.Inbox.Select(item => item.Kind)));
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case Contract.Kinds.Start:
                        turn.StartJob(JobKey);
                        break;
                    case Contract.Kinds.ChildFinished:
                        if (shape == "singleton-cancel")
                        {
                            turn.CancelJob(JobKey);
                            turn.Send(turn.Header.Ref, "committed-side-effect", dueAt: turn.Now + 1000);
                            turn.Record("cancelled in child notice");
                        }
                        turn.State = shape == "singleton-cancel" ? "cancelled job" : "released";
                        break;
                    case Contract.Kinds.Terminate:
                        turn.End(Contract.ActorStatus.Cancelled);
                        break;
                    case Contract.Kinds.JobFinished:
                        if (turn.State != "released" && !turn.Inbox.Any(input => input.Kind == "release"))
                        {
                            if (Handle is null && shape is "self-release" or "poll-one") turn.Send(turn.Header.Ref, "release");
                            if (Handle is null && shape is "singleton" or "singleton-cancel") turn.Start(new(Type, "child", null));
                            if (shape == "send-cap")
                                for (var i = 0; i < 10_000; i++) turn.Send(turn.Header.Ref, "overflow");
                            Handle ??= message.Id;
                            turn.State = "held";
                            turn.Hold(message);
                            if (shape.StartsWith("cut-", StringComparison.Ordinal))
                            {
                                EnteredHold.TrySetResult();
                                await FinishHold.Task;
                            }
                        }
                        else
                        {
                            var result = Json.Read<Contract.JobFinished>(message.Payload!);
                            Applied++;
                            AppliedHandle = message.Id;
                            turn.State = result.Error ?? Json.Read<string>(result.Result!);
                        }
                        break;
                }
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            if (++Entries > 1) return "substituted";
            await Release.Task.WaitAsync(cancellationToken);
            return "original";
        }

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
