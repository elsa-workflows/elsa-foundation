using Kernel.Durable;
using Kernel.Foundation.InMemory;
using Kernel.Runtime;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class InlineTrialChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long start = 1_714_000_000_000;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        var requests = new Requests("inline-trial");
        var scope = new Contract.BoundScope("inline-trial");
        var actor = new Model();
        var opts = new WorkerOptions
        {
            ShortWait = TimeSpan.FromMilliseconds(100), Lease = TimeSpan.FromMilliseconds(50),
            Monotonic = () => now - start, Transport = new Replies(requests)
        };
        Worker NewOwner(string name) => new(name, 0, store, new ActorAdapter(store, actor, (requests, name)), opts);
        Contract.IIngress Edge(Worker worker) => new InMemoryIngress(store, requests, worker.Node,
            (bound, _) => bound == scope, worker);
        var reads = new ActorList(store, 10);
        var first = NewOwner("first");
        await using var firstScope = (await new DurableHost([first]).ActivateAsync(0))!;
        using var caller = new CancellationTokenSource();
        var initial = Edge(first).RequestAsync(scope, new Contract.Target.Start(new(actor.Type, "timed", Ephemeral: true)),
            "start-scheduled", Contract.Kinds.Start, null, null, new(null, now + 1_000, Inline: true), caller.Token).AsTask();
        await Pass(firstScope);
        check(actor.Work is { } && actor.Attempts.Count == 1 && store.Instances.Count == 0 && store.Messages.Count == 0,
            "the original timed job runs from an unwritten inline life");
        var work = actor.Work!.Value;
        now += 10;
        await Pass(firstScope);
        check(await reads.GetAsync(scope, work) is { Header.Status: Contract.ActorStatus.Running } && store.Messages.Count == 2,
            "the same inline life publishes the pending job and deadline with its first subscription");
        caller.Cancel();
        try { await initial; }
        catch (OperationCanceledException) when (caller.IsCancellationRequested) { }

        now = start + 51;
        var next = NewOwner("next");
        await using var nextScope = (await new DurableHost([next]).ActivateAsync(0))!;
        await Pass(nextScope);
        check(actor.Attempts.Count == 2 && next.Epoch > first.Epoch,
            "the successor physically enters the recovered timed job trial");
        actor.Attempts[0].Result.SetResult("old");
        var edge = Edge(next);
        var quick = edge.RequestAsync(scope, new Contract.Target.Start(new(actor.Type, "quick", Ephemeral: true)), "quick",
            Contract.Kinds.Start, null, null, new(null, now + 1_000, Inline: true)).AsTask();
        var selection = (await reads.GetAsync(scope, work))!.Selection!;
        var accepted = await edge.SendAsync(scope, new Contract.Target.Ref(work), "terminate", Contract.Kinds.Terminate,
            Json.Write(new Contract.OperatorCommand(selection)), new Contract.AddOptions(DueAt: now));
        check(accepted.Outcome == Contract.AdmissionOutcome.Accepted, "the actor's authorized termination is queued first");
        now = start + 80;
        await Pass(nextScope);
        check(actor.Attempts[1].Token.IsCancellationRequested && !actor.Attempts[1].Result.Task.IsCompleted &&
            actor.Quick == 0 && !quick.IsCompleted && await reads.GetAsync(scope, work) is { Header.Status: Contract.ActorStatus.Running },
            "the cutoff and mature terminate cannot release the physical trial or enter an unrelated inline turn");
        actor.Attempts[1].Result.SetResult("stopped");
        await Pass(nextScope);
        check(await reads.GetAsync(scope, work) is { Header.Status: Contract.ActorStatus.Cancelled } &&
            (await reads.InboxAsync(scope, work)).Items.Count == 0 &&
            !(await reads.TailAsync(scope, work)).Items.Any(entry => entry.Entry == Json.Write("result applied")),
            "after physical exit the earlier terminate wins and no timeout result applies");
        check(actor.Quick == 1 && await quick.WaitAsync(TimeSpan.FromSeconds(2)) is { Item: "quick" } && store.Instances.Count == 1,
            "the unrelated local request runs exactly once after isolation and leaves no durable row");
    }

    private static async Task Pass(IScopeActivation owner)
    {
        for (var i = 0; i < 32; i++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class Replies(Requests requests) : IReplyTransport
    {
        public void Send(string node, Reply reply) => requests.Receive(reply);
    }

    private sealed class Model : Contract.IActorType
    {
        public string Type => "inline-trial";
        public Contract.ActorRef? Work { get; private set; }
        public int Quick { get; private set; }
        public List<(CancellationToken Token, TaskCompletionSource<object?> Result)> Attempts { get; } = [];
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string? state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string? state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string? state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            var result = new TaskCompletionSource<object?>();
            Attempts.Add((cancellationToken, result));
            return new(result.Task);
        }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
            {
                switch (message.Kind)
                {
                    case Contract.Kinds.Start when turn.Header.TypeData == "quick":
                        Quick++;
                        turn.Emit("response", "quick");
                        turn.End(Contract.ActorStatus.Finished);
                        break;
                    case Contract.Kinds.Start:
                        Work = turn.Header.Ref;
                        turn.StartJob("timed", timeout: TimeSpan.FromMilliseconds(80));
                        turn.Send(turn.Header.Ref, "promote", dueAt: turn.Now + 10);
                        break;
                    case "promote":
                        turn.Subscribe("signal", "wait");
                        break;
                    case Contract.Kinds.Terminate:
                        turn.End(Contract.ActorStatus.Cancelled);
                        break;
                    case Contract.Kinds.JobFinished:
                        turn.Record("result applied");
                        break;
                }
            }
            return ValueTask.CompletedTask;
        }
    }
}
