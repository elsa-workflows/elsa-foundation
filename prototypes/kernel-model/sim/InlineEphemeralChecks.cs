using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class InlineEphemeralChecks
{
    public static async Task Run(Action<bool, string> check, Flaw flaws)
    {
        string[] shapes = flaws.HasFlag(Flaw.ForgetShortWaits) ? ["promote"]
            : ["finish", "timer", "job", "timed", "promote", "handover", "child", "hold", "refused", "lost-before", "lost-after"];
        foreach (var shape in shapes)
            await new Schedule(shape, check, flaws).Run();
    }

    private sealed class Schedule(string shape, Action<bool, string> check, Flaw flaws)
    {
        private const long Start = 1_703_500_000_000;
        private readonly Contract.BoundScope _scope = new("inline");
        private long _now = Start;

        internal async Task Run()
        {
            var store = new InMemoryStore(1, () => _now);
            var requests = new Requests("inline-owner");
            var actor = new Probe();
            var options = new WorkerOptions
            {
                ShortWait = TimeSpan.FromMilliseconds(100), Monotonic = () => _now - Start,
                Transport = new Transport(requests), Flaws = flaws
            };
            var loss = shape is "lost-before" or "lost-after";
            var commits = 0;
            var lost = false;
            var wire = new CountingStore(store, () => commits++, (batch, landed) =>
            {
                if (!loss || lost || (shape == "lost-after") != landed || !batch.Turns.Any(turn => turn.Sent.Any(msg => msg.BindFoundationChild))) return;
                lost = true;
                throw new IOException("the first durable inline answer was lost");
            });
            if (shape == "refused") store.Refuses = row => row.Status == InstanceStatus.Running;
            Worker MakeOwner(string node) => new(node, 0, wire, new ActorAdapter(store, actor, (requests, node)), options);
            var worker = MakeOwner("inline-owner");
            await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
            Contract.IIngress ingress = new InMemoryIngress(store, requests, "inline-owner", inlineOwner: worker);
            Contract.IActorReads reads = new ActorList(store, 5);

            var recipe = shape is "refused" or "lost-before" or "lost-after" ? "child" : shape == "handover" ? "promote" : shape;
            var target = new Contract.Target.Start(new Contract.StartSpec(actor.Type, recipe, null, Ephemeral: true));
            var deadline = _now + (shape == "promote" ? 60 : 400);
            var reply = ingress.RequestAsync(_scope, target, shape, Contract.Kinds.Start, null, null,
                new("caller", deadline, Inline: true)).AsTask();
            var duplicate = await ingress.SendAsync(_scope, target, shape, Contract.Kinds.Start);
            check(store.Messages.Count == 0 && duplicate.Outcome == Contract.AdmissionOutcome.Duplicate,
                $"inline {shape} shared its provisional receipt and omitted the creator write");
            await Pass(owner);
            check(actor.Reference is not null && duplicate.Actor == actor.Reference, $"inline {shape} ran the same provisional life on the local owner");
            var reference = actor.Reference.GetValueOrDefault();
            if (shape is "timer" or "timed" or "promote" or "handover")
            {
                check(await reads.GetAsync(_scope, reference) is null && store.Messages.Count == 0 && store.Journal.Count == 0,
                    $"inline {shape} waits without a durable birth");
                check((await ingress.SendAsync(_scope, new Contract.Target.Ref(reference), "uncommitted", "resume")).Outcome ==
                    Contract.AdmissionOutcome.Refused, $"inline {shape} is not externally addressable before promotion");
                if (shape == "handover") await owner.DisposeAsync();
                else
                {
                    _now += shape is "timer" or "timed" ? 20 : 60;
                    if (shape == "promote") await worker.FlushAsync();
                    else await Pass(owner);
                }
            }

            if (shape is "promote" or "handover")
            {
                check(await reads.GetAsync(_scope, reference) is
                    { Header: { TypeData: "updated", Labels: var labels } } && store.Instances.Values.Single().State == "checkpoint" &&
                    labels.GetValueOrDefault("phase") == "waiting" && store.Messages.Count == 1 && store.Journal.Count == 1,
                    "inline promotion writes the same public life, state, header, journal and live timer together");
                if (shape == "handover")
                {
                    _now += 60;
                    await using var successor = (await new DurableHost([MakeOwner("successor")]).ActivateAsync(0))!;
                    await Pass(successor);
                }
                else await Pass(owner);
                check(await reads.GetAsync(_scope, reference) is { Header.Status: Contract.ActorStatus.Finished },
                    $"the {shape} timer continues the same actor");
                return;
            }

            if (shape == "hold")
            {
                check(await reads.GetAsync(_scope, reference) is { Header.Status: Contract.ActorStatus.Running } &&
                    (await reads.InboxAsync(_scope, reference)).Items.Count == 1, "an inline held Start remains available after the actor is stored");
                await ingress.SendAsync(_scope, new Contract.Target.Ref(reference), "stimulus", "resume");
                await Pass(owner);
            }

            var answer = await reply.WaitAsync(TimeSpan.FromSeconds(2));
            if (shape is "finish" or "timer" or "job")
            {
                check(answer is { Channel: "response", Item: string value } && value == shape && commits == 0 &&
                    await reads.GetAsync(_scope, reference) is null && store.Messages.Count == 0 && store.Instances.Count == 0 &&
                    store.Journal.Count == 0, $"inline {shape} answered and ended without writes");
            }
            else if (shape == "timed")
            {
                check(answer is { Channel: "response", Item: "timeout" } && store.Messages.Count == 0,
                    "the short timed job times out once and settles its volatile ticket");
            }
            else if (shape == "refused")
            {
                check(answer is { Status: Contract.ActorStatus.Faulted, Item: null } &&
                    await reads.GetAsync(_scope, reference) is { Header.Status: Contract.ActorStatus.Faulted } &&
                    store.Messages.Count == 0 && store.Instances.Count == 1,
                    "refusal publishes neither the proposed child nor the staged response");
            }
            else if (loss)
            {
                var landed = shape == "lost-after";
                check(lost && answer.Error == "lost" && (await reads.GetAsync(_scope, reference) is not null) == landed &&
                    store.Messages.Count == (landed ? 1 : 0),
                    $"an unknown first commit preserves exactly the effects that landed ({shape})");
                _now += (long)options.Lease.TotalMilliseconds + 1;
                await using var successor = (await new DurableHost([MakeOwner("successor")]).ActivateAsync(0))!;
                await Pass(successor);
                check(store.Instances.Count == (landed ? 2 : 0) && store.Messages.Count == 0,
                    $"takeover consumes only the child of a landed inline commit ({shape})");
            }
            else if (shape == "child")
            {
                check(answer is { Channel: "response", Item: Contract.ActorRef child } &&
                    await reads.GetAsync(_scope, reference) is { Header.Status: Contract.ActorStatus.Running } &&
                    await reads.GetAsync(_scope, child) is { Header.Status: Contract.ActorStatus.Finished },
                    "the first durable outcome binds the inline creator and its detached child together");
            }
            else
            {
                check(answer is { Channel: "response", Item: "hold" } &&
                    (await reads.InboxAsync(_scope, reference)).Items.Count == 0,
                    "the published actor resumes and consumes its formerly volatile held Start once");
            }
        }
    }

    private static async Task Pass(Kernel.Runtime.IScopeActivation owner)
    {
        for (var i = 0; i < 10; i++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "inline-probe";
        public Contract.ActorRef? Reference { get; private set; }
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string? state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string? state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string? state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
            => job.Key == "timed"
                ? new(new TaskCompletionSource<object?>().Task.WaitAsync(cancellationToken))
                : ValueTask.FromResult<object?>(job.Actor == Reference ? "job" : "wrong actor");

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            Reference ??= turn.Header.Ref;
            var recovered = turn.Inbox.Any(message => message.Kind == "resume");
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start && turn.Header.TypeData != "finish" && !recovered)
                {
                    switch (turn.Header.TypeData)
                    {
                        case "timer": turn.Send(turn.Header.Ref, "timer", dueAt: turn.Now + 20); break;
                        case "promote":
                            turn.Send(turn.Header.Ref, "timer", dueAt: turn.Now + 60);
                            turn.State = "checkpoint";
                            turn.SetHeader("updated", new Dictionary<string, string> { ["phase"] = "waiting" });
                            turn.Record("waiting");
                            break;
                        case "job": turn.StartJob("job"); break;
                        case "timed": turn.StartJob("timed", timeout: TimeSpan.FromMilliseconds(20)); break;
                        case "child": turn.Emit("response", turn.Start(new(Type, "finish", null), detached: true)); break;
                        case "hold": turn.Hold(message); break;
                    }
                    continue;
                }

                if (message.Kind == "resume") continue;
                var observation = message.Kind == Contract.Kinds.Start && !recovered
                    ? turn.Ambient as string == "caller" ? "finish" : "wrong ambient"
                    : recovered ? "hold" : message.Kind == Contract.Kinds.JobFinished
                        ? Json.Read<Contract.JobFinished>(message.Payload!) is { Result: { } result } ? Json.Read<string>(result) : JobError(message)
                        : message.Kind;
                turn.Emit("response", observation);
                turn.End(Contract.ActorStatus.Finished);
            }
            return default;

            static string? JobError(Contract.Message message) => Json.Read<Contract.JobFinished>(message.Payload!).Error;
        }
    }

    private sealed class Transport(Requests requests) : IReplyTransport
    {
        public void Send(string node, Reply reply) => requests.Receive(reply);
    }
}
