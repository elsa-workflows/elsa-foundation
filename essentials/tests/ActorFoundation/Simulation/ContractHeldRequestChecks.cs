using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ContractHeldRequestChecks
{
    public static async Task Run(Action<bool, string> check, Flaw flaws)
    {
        await Run(check, flaws, reset: false);
        await Run(check, flaws, reset: true);
    }

    private static async Task Run(Action<bool, string> check, Flaw flaws, bool reset)
    {
        const long initial = 1_702_100_000_000;
        const int lease = 100;
        var now = initial;
        var scope = ContractScopes.Bind("north");
        var store = new InMemoryStore(1, () => now);
        var requests = new Requests("request-node");
        var transport = new Replies(requests);
        Contract.IIngress ingress = new InMemoryIngress(store, requests, "request-node");
        Contract.IActorReads reads = new ActorList(store, 16);
        var model = new Probe();
        var loseReplyCommit = false;
        var triedReplyCommit = 0;
        var authoredReplyBeforeCommit = false;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed || !batch.Turns.Any(turn => turn.Row?.State == "answered")) return;
            triedReplyCommit++;
            authoredReplyBeforeCommit |= transport.Authored > 0;
            if (!loseReplyCommit) return;
            loseReplyCommit = false;
            throw new StoreException(Failure.Unknown, "the first reply-bearing commit never reached the store");
        });

        var opened = await ingress.SendAsync(scope, new Contract.Target.Start(new(model.Type, "request")),
            "start", Contract.Kinds.Start);
        var reference = opened.Actor!.Value;
        var target = new Contract.Target.Ref(reference);
        await using var first = await Activate("request-node");
        await Pump(first);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var response = ingress.RequestAsync(scope, target, "held-request", "ask", "answer",
            null, new Contract.RequestOptions("request ambient", now + 10_000), timeout.Token).AsTask();
        await Pump(first);
        var held = await reads.InboxAsync(scope, reference);
        check(!response.IsCompleted && transport.Sent == 0 && held.Items is
            [{ Id: "held-request", State: Contract.DeliveryState.Held }],
            "a live public request remains unanswered while its input is durably held");

        if (!reset) now += lease + 1;
        await using var successor = reset ? null : await Activate("successor-node");
        var owner = successor ?? first;
        if (successor is not null)
        {
            await Pump(successor);
            check(held.Items is [{ Handle: var originalHandle }] &&
                (await reads.InboxAsync(scope, reference)).Items is
                [{ Id: "held-request", State: Contract.DeliveryState.Held, Handle: var handle }] &&
                handle == originalHandle && transport.Sent == 0,
                "a successor sees the same opaque held request and cannot release it before new input");
        }

        loseReplyCommit = reset;
        await ingress.SendAsync(scope, target, "release", "release");
        await Pump(owner);
        Contract.Reply? reply = null;
        try { reply = await response; }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }

        if (reset)
        {
            check(triedReplyCommit == 1 && !loseReplyCommit && !authoredReplyBeforeCommit &&
                reply is { Error: "lost", Channel: null } && transport.Authored == 0,
                "the original receiving owner reports its reset without releasing the authored response");
            await owner.TakeAsync();
            await Pump(owner);
        }
        else
        {
            check(triedReplyCommit == 1 && !authoredReplyBeforeCommit &&
                reply is { Channel: "answered", Item: "answer:none", Error: null } && transport.Sent == 1,
                "a live reply on the successor releases once after commit with no original-node ambient");
        }

        var journal = await reads.TailAsync(scope, reference);
        check(triedReplyCommit == (reset ? 2 : 1) &&
            journal.Items.Count(entry => entry.Entry == Json.Write("applied:held-request")) == 1 &&
            (await reads.InboxAsync(scope, reference)).Items.Count == 0,
            "the committed turn applies the held request exactly once and consumes its inbox row");

        async Task<Worker> Activate(string node)
        {
            var adapter = new ActorAdapter(store, model, (requests, node));
            var worker = new Worker(node, 0, wire, adapter, new WorkerOptions
            {
                Lease = TimeSpan.FromMilliseconds(lease), Monotonic = () => now - initial, Transport = transport, Flaws = flaws
            });
            if (await new DurableHost([worker]).ActivateAsync(0) is null)
                throw new InvalidOperationException("The live-request witness could not acquire the actor's owner.");
            return worker;
        }

        static async Task Pump(IScopeActivation owner)
        {
            for (var pass = 0; pass < 20; pass++)
            {
                await owner.WokenAsync(null);
                await owner.RunAsync();
                await Task.Yield();
            }
        }
    }

    private sealed class Replies(Requests requests) : IReplyTransport
    {
        public int Sent { get; private set; }
        public int Authored { get; private set; }

        public void Send(string node, Reply reply)
        {
            Sent++;
            if (reply.Channel == "answered") Authored++;
            requests.Receive(reply);
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "held-request";
        public bool Withholds(Contract.ActorHeader header) => false;
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox.FirstOrDefault(message => message.Kind == "ask") is not { } request)
                return default;
            if (!turn.Inbox.Any(message => message.Kind == "release"))
            {
                turn.Hold(request);
                return default;
            }

            turn.State = "answered";
            turn.Record($"applied:{request.Id}");
            turn.Emit("answered", $"{request.Payload}:{turn.Ambient ?? "none"}");
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
