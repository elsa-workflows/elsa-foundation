using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class InlineEphemeralChecks
{
    public static async Task Run(Action<bool, string> check, Flaw flaws)
    {
        string[] shapes = flaws.HasFlag(Flaw.ForgetShortWaits) ? ["promote"]
            : flaws.HasFlag(Flaw.IgnoreLeaseDeadline) ? ["stale"]
            : ["finish", "timer", "job", "timed", "timed-fast", "promote", "handover", "overlong", "child", "hold", "refused", "refused-later", "refused-both", "lost-before", "lost-after", "fallback", "stale", "park-cutoff", "subscribe", "subscribe-later"];
        foreach (var shape in shapes)
            await new Schedule(shape, check, flaws).Run();
    }

    private sealed class Schedule(string shape, Action<bool, string> check, Flaw flaws)
    {
        private const long Start = 1_703_500_000_000;
        private readonly Contract.BoundScope _scope = ContractScopes.Bind("inline");
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
            if (shape is "refused" or "refused-later" or "refused-both")
                store.Refuses = row => row.State == "discard this state" || shape == "refused-both" && row.State == "checkpoint";
            Worker MakeOwner(string node)
            {
                var adapter = new ActorAdapter(store, actor, (requests, node));
                IActorType router = new ActorTypeRouter(new Kernel.Workflows.WorkflowInstance([]),
                    new Dictionary<string, IActorType> { [actor.Type] = adapter });
                return new(node, 0, wire, router, options);
            }
            var worker = MakeOwner("inline-owner");
            await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
            Contract.IIngress ingress = new InMemoryIngress(store, requests, "inline-owner", inlineOwner: worker);
            Contract.IActorReads reads = new ActorList(store, 5);

            var recipe = shape is "lost-before" or "lost-after" ? "child" :
                shape == "handover" ? "promote" :
                shape == "refused-both" ? "refused-later" : shape;
            var target = new Contract.Target.Start(new Contract.StartSpec(shape == "fallback" ? "unregistered" : actor.Type, recipe, null, Ephemeral: true));
            var deadline = _now + (shape == "promote" ? 60 : 400);
            if (shape == "stale") deadline += (long)options.Lease.TotalMilliseconds;
            var reply = ingress.RequestAsync(_scope, target, shape, Contract.Kinds.Start, null, null,
                new("caller", deadline, Inline: true)).AsTask();
            var duplicate = await ingress.SendAsync(_scope, target, shape, Contract.Kinds.Start);
            if (shape == "fallback")
            {
                check(store.Messages.Count == 1 && duplicate is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: not null },
                    "an unregistered type falls back to the durable creator at the same receipt");
                await Pass(owner);
                check((await reply.WaitAsync(TimeSpan.FromSeconds(2))).Status == Contract.ActorStatus.Faulted &&
                    await reads.GetAsync(_scope, duplicate.Actor!.Value) is { Header.Status: Contract.ActorStatus.Faulted },
                    "the mixed router fallback parks a life visible through its issued public reference");
                return;
            }
            check(store.Messages.Count == 0 && duplicate.Outcome == Contract.AdmissionOutcome.Duplicate,
                $"inline {shape} shared its provisional receipt and omitted the creator write");
            await Pass(owner);
            check(actor.Reference is not null && duplicate.Actor == actor.Reference, $"inline {shape} ran the same provisional life on the local owner");
            var reference = actor.Reference.GetValueOrDefault();
            if (shape is "timer" or "timed" or "promote" or "handover" or "refused-later" or "refused-both")
            {
                check(await reads.GetAsync(_scope, reference) is null && store.Messages.Count == 0 && store.Journal.Count == 0,
                    $"inline {shape} waits without a durable birth");
                check((await ingress.SendAsync(_scope, new Contract.Target.Ref(reference), "uncommitted", "resume")).Outcome ==
                    Contract.AdmissionOutcome.Refused, $"inline {shape} is not externally addressable before promotion");
                if (shape == "handover") await owner.DisposeAsync();
                else
                {
                    _now += shape is "timer" or "timed" or "refused-later" or "refused-both" ? 20 : 60;
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

            if (shape is "subscribe" or "subscribe-later")
            {
                if (shape == "subscribe-later")
                {
                    check(await reads.GetAsync(_scope, reference) is null && store.Subscriptions.Count == 0,
                        "the delayed subscription leaves the birth volatile before its timer");
                    _now += 10;
                    await Pass(owner);
                }
                check(await reads.GetAsync(_scope, reference) is { Header.Status: Contract.ActorStatus.Running, Header.Ref: var bound } &&
                    bound == reference && store.Subscriptions.Count == 1,
                    $"the first durable outcome binds the life and its subscription together ({shape})");
                await ingress.SendAsync(ContractScopes.Bind("other"), new Contract.Target.Event("signal"), "foreign", "ignored", "foreign");
                await Pass(owner);
                check(!reply.IsCompleted, $"a foreign publication cannot resolve the provisional life ({shape})");
                await ingress.SendAsync(_scope, new Contract.Target.Event("signal"), "same-scope", "ignored", "same-scope");
                await Pass(owner);
            }

            if (shape == "park-cutoff")
            {
                check(actor.JobsEntered == 1 && store.Messages.Count == 0, "the armed timed job is still local before Park");
                _now += 10;
                await Pass(owner);
            }

            if (shape == "stale")
            {
                check(actor.FirstTurnWaiting && store.Messages.Count == 0,
                    "the original owner has an active write-free start before takeover");
                _now += (long)options.Lease.TotalMilliseconds + 1;
                await using var successor = (await new DurableHost([MakeOwner("successor")]).ActivateAsync(0))!;
                await Pass(successor);
                actor.Resume();
                var stale = await reply.WaitAsync(TimeSpan.FromSeconds(2));
                check(stale is { Error: "lost", Item: null } && store.Instances.Count == 0 && store.Journal.Count == 0,
                    "an expired inline owner cannot return a successful write-free response after takeover");
                return;
            }

            if (shape == "overlong")
            {
                _now += 20;
                await Pass(owner);
                check(actor.TimerCalls == 1 && await reads.GetAsync(_scope, reference) is null,
                    "the inline timer entered one physically active turn");
                _now += 80;
                await Pass(owner);
                check(actor.TimerCalls == 1 && await reads.GetAsync(_scope, reference) is { Header.Status: Contract.ActorStatus.Running },
                    "short-wait expiry writes the prior checkpoint without overlapping authored turns on one owner");
                actor.Resume();
                await Pass(owner);
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
            else if (shape is "timed" or "timed-fast")
            {
                var view = await reads.GetAsync(_scope, reference);
                var history = await reads.TailAsync(_scope, reference);
                var entries = history.Items.Select(item => Json.Read<string>(item.Entry));
                var expected = shape == "timed" ? "timeout" : "timed-fast";
                check(answer is { Channel: "response", Item: string value } && value == expected && store.Messages.Count == 0 &&
                    view is { Header.TypeData: "timed-updated", Header.Labels.Count: 1 } &&
                    store.Instances.Values.Single().State == "checkpoint" &&
                    entries.SequenceEqual(["start", "waiting", expected, "end"]),
                    $"a short timed job promotes its header, state and journal and leaves no deadline ({shape})");
            }
            else if (shape is "subscribe" or "subscribe-later")
            {
                check(answer is { Channel: "response", Item: "same-scope" } && store.Subscriptions.Count == 0 &&
                    await reads.GetAsync(_scope, reference) is { Header.Status: Contract.ActorStatus.Finished },
                    $"the promoted subscription delivers once and End removes it ({shape})");
            }
            else if (shape == "park-cutoff")
            {
                check(answer is { Status: Contract.ActorStatus.Faulted } &&
                    await reads.GetAsync(_scope, reference) is { Header.Status: Contract.ActorStatus.Faulted } &&
                    store.Instances.Values.Single().Jobs is null && store.Messages.Count == 0,
                    "Park of an unwritten actor retires both its timed job and the volatile deadline");
            }
            else if (shape == "overlong")
            {
                var history = await reads.TailAsync(_scope, reference);
                check(answer is { Channel: "response", Item: "slow-timer" } && actor.TimerCalls == 2 &&
                    history.Items.Select(item => Json.Read<string>(item.Entry)).SequenceEqual(["start", "slow-timer", "end"]),
                    "only the retried timer outcome lands after the prior callback returns");
            }
            else if (shape == "refused")
            {
                var view = await reads.GetAsync(_scope, reference);
                var history = await reads.TailAsync(_scope, reference);
                check(answer is { Status: Contract.ActorStatus.Faulted, Item: null } &&
                    view is { Header: { Status: Contract.ActorStatus.Faulted, TypeData: "refused", Labels.Count: 0 } } &&
                    history.Items.All(item => item.Entry != Json.Write("discard this entry")) &&
                    store.Instances.Count == 1 && store.Instances.Values.Single().State == "null" &&
                    store.Messages.Count == 0 && store.Subscriptions.Count == 0 &&
                    store.FoundationEvents.Count == 0 && actor.JobsEntered == 0,
                    "refusal rolls back the staged state, header, journal, response, sends, timer, job, subscription, publish and child");
            }
            else if (shape is "refused-later" or "refused-both")
            {
                var view = await reads.GetAsync(_scope, reference);
                var history = await reads.TailAsync(_scope, reference);
                var row = store.Instances.Values.Single();
                var empty = shape == "refused-both";
                check(answer is { Status: Contract.ActorStatus.Faulted, Item: null } &&
                    view is { Header.Status: Contract.ActorStatus.Faulted, Header.TypeData: "accepted", Header.Labels.Count: 1 } &&
                    row.State == (empty ? "null" : "checkpoint") && row.FoundationNullState == empty && store.Subscriptions.Count == 0 &&
                    history.Items.Select(item => Json.Read<string>(item.Entry)).SequenceEqual(["start", "waiting", "parked"]),
                    $"a refused later promotion retains the accepted history and the allowed checkpoint ({shape})");
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
        public int JobsEntered { get; private set; }
        public int TimerCalls { get; private set; }
        public bool FirstTurnWaiting { get; private set; }
        private readonly TaskCompletionSource _release = new();

        internal void Resume() => _release.SetResult();
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string? state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string? state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string? state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            JobsEntered++;
            return job.Key == "timed"
                ? new(new TaskCompletionSource<object?>().Task.WaitAsync(cancellationToken))
                : ValueTask.FromResult<object?>(job.Actor == Reference ? job.Key : "wrong actor");
        }

        public async ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            Reference ??= turn.Header.Ref;
            var recovered = turn.Inbox.Any(message => message.Kind == "resume");
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start && turn.Header.TypeData == "stale")
                {
                    FirstTurnWaiting = true;
                    await _release.Task;
                    turn.Emit("response", "stale");
                    turn.End(Contract.ActorStatus.Finished);
                    continue;
                }

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
                        case "timed":
                        case "timed-fast":
                            turn.StartJob(turn.Header.TypeData, timeout: TimeSpan.FromMilliseconds(20));
                            Remember("timed-updated");
                            break;
                        case "overlong":
                            turn.Send(turn.Header.Ref, "slow-timer", dueAt: turn.Now + 20);
                            break;
                        case "subscribe": turn.Subscribe("signal", "next"); break;
                        case "subscribe-later": turn.Send(turn.Header.Ref, "arm-subscription", dueAt: turn.Now + 10); break;
                        case "park-cutoff":
                            turn.StartJob("timed", timeout: TimeSpan.FromMilliseconds(30));
                            turn.Send(turn.Header.Ref, "throw-timer", dueAt: turn.Now + 10);
                            break;
                        case "refused-later":
                            turn.Send(turn.Header.Ref, "refused-timer", dueAt: turn.Now + 20);
                            Remember("accepted");
                            break;
                        case "child": turn.Emit("response", turn.Start(new(Type, "finish", null), detached: true)); break;
                        case "hold": turn.Hold(message); break;
                        case "refused":
                            turn.SetHeader("discard this header", new Dictionary<string, string> { ["discard"] = "this label" });
                            turn.State = "discard this state";
                            turn.Record("discard this entry");
                            turn.Send(turn.Header.Ref, "immediate");
                            turn.Send(turn.Header.Ref, "timer", dueAt: turn.Now + 20);
                            turn.StartJob("discard");
                            turn.Subscribe("signal", "discard", keep: true);
                            turn.Publish("signal", "discard", TimeSpan.FromSeconds(1));
                            turn.Emit("response", turn.Start(new(Type, "finish", null), detached: true));
                            break;
                    }
                    continue;
                }

                if (message.Kind == "resume") continue;
                if (message.Kind == "arm-subscription")
                {
                    turn.Subscribe("signal", "next");
                    continue;
                }
                if (message.Kind == "throw-timer") throw new InvalidOperationException("short-wait Park control");
                if (message.Kind == "refused-timer")
                {
                    turn.State = "discard this state";
                    turn.Subscribe("refused", "wait");
                    continue;
                }
                if (message.Kind == "slow-timer")
                {
                    if (++TimerCalls == 1) await _release.Task;
                    turn.Record(message.Kind);
                }
                var observation = message.Kind switch
                {
                    Contract.Kinds.Start when recovered => "hold",
                    Contract.Kinds.Start => turn.Ambient as string == "caller" ? "finish" : "wrong ambient",
                    Contract.Kinds.JobFinished => JobAnswer(message),
                    Contract.Kinds.Event => message.Payload,
                    _ => message.Kind
                };
                if (message.Kind == Contract.Kinds.JobFinished && turn.Header.TypeData == "timed-updated")
                    turn.Record(observation!);
                turn.Emit("response", observation);
                turn.End(Contract.ActorStatus.Finished);
            }

            void Remember(string typeData)
            {
                turn.State = "checkpoint";
                turn.SetHeader(typeData, new Dictionary<string, string> { ["phase"] = "waiting" });
                turn.Record("waiting");
            }

            static string? JobAnswer(Contract.Message message)
            {
                var job = Json.Read<Contract.JobFinished>(message.Payload!);
                return job.Result is { } result ? Json.Read<string>(result) : job.Error;
            }
        }
    }

    private sealed class Transport(Requests requests) : IReplyTransport
    {
        public void Send(string node, Reply reply) => requests.Receive(reply);
    }
}
