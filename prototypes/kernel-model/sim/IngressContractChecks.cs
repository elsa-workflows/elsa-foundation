using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class IngressContractChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long start = 1_700_900_000_000;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        var requests = new Requests("contract-ingress");
        Contract.IIngress ingress = new InMemoryIngress(store, requests, "request-node");
        var scope = new Contract.BoundScope("acme");
        var foreign = new Contract.BoundScope("other");
        var transport = new Transport(store, requests);
        var adapter = new ActorAdapter(store, new Probe(), (requests, "request-node"));
        var worker = new Worker("request-node", 0, store, adapter,
            new WorkerOptions { Monotonic = () => now - start, Transport = transport });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;

        var spec = new Contract.StartSpec("ingress-probe", "request", "creator");
        var target = new Contract.Target.Start(spec);
        var root = ingress.RequestAsync(scope, target, "root", Contract.Kinds.Start, null, null, new("root-ambient", now + 100)).AsTask();
        await Pump(owner);
        var rootReply = await root.WaitAsync(TimeSpan.FromSeconds(2));
        check(rootReply is { Channel: "answer", Item: Observation { Input: "creator", Ambient: "root-ambient", Applied: 1 } } &&
            transport.ReadBeforeAnswer, "the public Start request answers from its first landed actor turn with local Ambient");
        var reference = ((Observation)rootReply.Item!).Actor;
        var exact = new Contract.Target.Ref(reference);

        var held = ingress.RequestAsync(scope, exact, "read-at-deadline", "hold", null, null, new(null, now + 10)).AsTask();
        await Pump(owner);
        now += 10;
        check(await held.WaitAsync(TimeSpan.FromSeconds(2)) is { Error: "timeout", Status: Contract.ActorStatus.Running } &&
            requests.All.Count == 0, "an unanswered exact request reports the committed actor status at the deadline");

        using var exactCaller = new CancellationTokenSource();
        var next = ingress.RequestAsync(scope, exact, "exact", "ping", "next", null, new("exact-ambient", now + 100),
            exactCaller.Token).AsTask();
        await Pump(owner);
        var delivered = next.IsCompletedSuccessfully || requests.All.Any(request => request.Id == "exact" && request.Done);
        if (!delivered)
        {
            exactCaller.Cancel();
            try { await next; }
            catch (OperationCanceledException) when (exactCaller.IsCancellationRequested) { }
        }
        check(delivered && await next.WaitAsync(TimeSpan.FromSeconds(2)) is { Channel: "answer", Item: Observation { Actor: var actor, Input: "next", Ambient: "exact-ambient" } } &&
            actor == reference && transport.ReadBeforeAnswer,
            "the public exact request carries the reply address in the admitted delivery");

        var key = new Contract.Target.Key("Invoice", spec);
        var keyedStart = ingress.RequestAsync(scope, key, "start-key", Contract.Kinds.Start, null, null, new("key-start", now + 100)).AsTask();
        await Pump(owner);
        var started = await keyedStart.WaitAsync(TimeSpan.FromSeconds(2));
        var keyedSend = ingress.RequestAsync(scope, new Contract.Target.Key("iNVOICE"), "key-call", "ping", "key payload", null,
            new("key-ambient", now + 100)).AsTask();
        await Pump(owner);
        var keyedReply = await keyedSend.WaitAsync(TimeSpan.FromSeconds(2));
        check(started.Item is Observation first && keyedReply is
            { Channel: "answer", Item: Observation { Actor: var member, Input: "key payload", Ambient: "key-ambient" } } &&
            member == first.Actor, "the public keyed request uses the same exact life across spellings");

        var miss = await ingress.RequestAsync(foreign, exact, "foreign", "ping", null, null, new(null, now + 100));
        var emptyEvent = new Contract.Target.Event("no-start");
        var eventMiss = await ingress.RequestAsync(scope, emptyEvent, "event", "ignored", null, null, new(null, now + 100));
        check(miss is { Error: "missing", Item: null } && eventMiss is { Error: "no waiter", Item: null } &&
            requests.All.Count == 0 && store.FoundationEvents.Count == 0,
            "the public port hides foreign actors and an unmatched event request publishes nothing");

        var end = ingress.RequestAsync(scope, exact, "end", "end", null, null, new(null, now + 100)).AsTask();
        await Pump(owner);
        check(await end.WaitAsync(TimeSpan.FromSeconds(2)) is { Channel: null, Status: Contract.ActorStatus.Finished },
            "an exact request without output receives the actor's landed end status");
    }

    private sealed record Observation(Contract.ActorRef Actor, string? Input, string? Ambient, int Applied);

    private sealed class Transport(InMemoryStore store, Requests requests) : IReplyTransport
    {
        public bool ReadBeforeAnswer { get; private set; }

        public void Send(string node, Reply reply)
        {
            if (reply.Item is Observation observation)
            {
                var actor = store.ReadFoundationActor("acme", observation.Actor.Id).Actor;
                ReadBeforeAnswer = actor is not null && Json.Read<int>(actor.Row.State) == observation.Applied;
            }
            requests.Receive(reply);
        }
    }

    private static async Task Pump(IScopeActivation owner)
    {
        for (var step = 0; step < 20; step++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "ingress-probe";
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var applied = turn.State is null ? 0 : Json.Read<int>(turn.State);
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case "hold":
                        turn.Hold(message);
                        break;
                    case "end":
                        turn.End(Contract.ActorStatus.Finished);
                        break;
                    default:
                        turn.Reply(message, "answer", new Observation(turn.Header.Ref, message.Payload, turn.Ambient as string, ++applied));
                        break;
                }

            turn.State = Json.Write(applied);
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
