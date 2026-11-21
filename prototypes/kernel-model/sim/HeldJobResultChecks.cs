using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class HeldJobResultChecks
{
    private const string JobKey = "run";

    public static async Task Run(Action<bool, string> check, Flaw flaws)
    {
        await Schedule(false, flaws, (ok, name) => check(ok, $"durable: {name}"));
        await Schedule(true, flaws, (ok, name) => check(ok, $"inline first write: {name}"));
    }

    private static async Task Schedule(bool inline, Flaw flaws, Action<bool, string> check)
    {
        const long initial = 1_701_300_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var actor = new Probe();
        var scope = new Contract.BoundScope("held-result");
        var requests = new Requests("first-owner");
        Worker WorkerFor(string node) => new(node, 0, store, new ActorAdapter(store, actor),
            new WorkerOptions { Monotonic = () => now - initial, Flaws = flaws });
        var worker = WorkerFor("first-owner");
        Contract.IIngress edge = new InMemoryIngress(store, requests, "first-owner", inlineOwner: worker);
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
            await Pump(owner);

            var row = store.Instances.Values.Single();
            var ticket = InboxKey.Ticket(row.Id, JobKey, row.Creation);
            var result = InboxKey.JobResult(row.Id, JobKey, row.Creation);
            check(row.State == "held" && row.Jobs is not null && actor.Handle is not null && actor.Applied == 0 &&
                !store.Messages.ContainsKey(ticket) && store.Messages.GetValueOrDefault(result) is { Held: true, Message.FoundationJobResult: true },
                "the landed Hold keeps the original completion and removes its runnable ticket");

            // Stand-down changes the epoch without relying on real time or a test delay.
            await owner.DisposeAsync();
            await using var successor = (await new DurableHost([WorkerFor("successor")]).ActivateAsync(0))!;
            await edge.SendAsync(scope, target, "release", "release");
            await Pump(successor);
            check(store.Instances.Values.Single() is { State: "original", Jobs: null } &&
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

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "hold-job-result";
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Entries { get; private set; }
        public int Applied { get; private set; }
        public string? Handle { get; private set; }
        public string? AppliedHandle { get; private set; }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case Contract.Kinds.Start:
                        turn.StartJob(JobKey);
                        break;
                    case Contract.Kinds.JobFinished:
                        if (!turn.Inbox.Any(input => input.Kind == "release"))
                        {
                            Handle ??= message.Id;
                            turn.State = "held";
                            turn.Hold(message);
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
            return default;
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
