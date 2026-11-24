namespace Kernel.Native.Run;

internal static class ParkR12Checks
{
    private class Plain(bool releaseClassified, bool persistedRelease, bool acceptAlone) : IActorType
    {
        public int Calls, Queries;
        public readonly List<string[]> Batches = [];
        protected bool Classify(Message message) { Queries++; return message.Kind == (releaseClassified ? "release" : "boundary"); }
        public bool Withholds(InstanceRow row, ITurnServices services) => false;
        public TurnOutcome? Command(TurnInput input) => null;
        public ValueTask<TurnOutcome> TurnAsync(TurnInput input)
        {
            var turn = new DurableTurn(input);
            var state = input.Row is { } row ? Json.Read<List<string>>(row.State) : [];
            Batches.Add(turn.Inbox.Select(m => m.Kind).ToArray());
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == "boundary")
                {
                    Calls++;
                    var ready = acceptAlone || (persistedRelease ? state.Contains("released") : turn.Inbox.Any(m => m.Kind == "release"));
                    if (ready) state.Add("accepted"); else turn.Hold(message);
                }
                if (message.Kind == "release") state.Add("released");
            }
            var next = new InstanceRow(input.Actor, "plain", "plain", InstanceStatus.Running, Json.Write(state), turn.Revision, null);
            return ValueTask.FromResult(turn.Seal(turn.Outcome(next.Status), next, state, "[]"));
        }
        public TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error) =>
            throw new InvalidOperationException("unexpected generic actor Park: " + error);
        public ValueTask<object?> ExecuteJobAsync(string model, IJob job, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Legacy(bool release, bool persisted, bool alone) : Plain(release, persisted, alone), IActorType
    {
        public bool SingleMessage(Message message) => Classify(message);
    }
    private sealed class Contextual(bool release, bool persisted, bool alone) : Plain(release, persisted, alone), IActorType
    {
        public bool SingleMessage(string? model, InstanceRow? row, Delivery delivery)
        {
            if (model != "plain" || row?.Model != model) throw new InvalidOperationException("contextual classifier lost stored model or row");
            return Classify(delivery.Message);
        }
    }
    private static IActorType Route(IActorType actor) =>
        new ActorTypeRouter(new WorkflowInstance([]), new Dictionary<string, IActorType> { ["plain"] = actor });
    private static void Require(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    public static IEnumerable<(string Label, Func<Task> Run)> Cases(Func<IActorType, IActorType>? decorate = null)
    {
        foreach (var contextual in new[] { false, true })
        foreach (var routed in new[] { false, true })
        {
            foreach (var release in new[] { false, true })
            {
                yield return ($"R12 legacy classifier batches {contextual}/{routed}/{release}",
                    () => Run(contextual, routed, release, qualified: false, wake: false, decorate));
                yield return ($"R12 qualified classifier selects alone {contextual}/{routed}/{release}",
                    () => Run(contextual, routed, release, qualified: true, wake: false, decorate));
            }
            foreach (var qualified in new[] { false, true })
                yield return ($"R12 default committed wake {contextual}/{routed}/{qualified}",
                    () => Run(contextual, routed, release: true, qualified, wake: true, decorate));
        }
    }
    private static async Task Run(bool contextual, bool routed, bool release, bool qualified, bool wake, Func<IActorType, IActorType>? decorate)
    {
        const string id = "0/plain";
        long time = 100;
        var store = new InMemoryStore(1, () => time);
        Plain actor = contextual ? new Contextual(release, wake, qualified && !wake) : new Legacy(release, wake, qualified && !wake);
        IActorType host = routed ? Route(actor) : actor;
        if (decorate is not null) host = decorate(host);
        await using var worker = new Worker("one", 0, store, host, new() { Monotonic = () => time });
        await new DurableHost([worker]).ActivateAsync(0);
        async Task Add(string kind) => await store.AddAsync(new(kind, new(id), kind,
            Start: kind == Kinds.Start ? new("plain", "plain", Creation: qualified ? new(1, 0, "plain") : null) : null,
            TargetCreation: qualified ? 1 : null));
        async Task Drive() { for (var i = 0; i < 12; i++) { await worker.WokenAsync(null); await worker.RunAsync(); } }
        await Add(Kinds.Start); await Drive(); actor.Queries = 0;
        await Add("boundary");
        if (!qualified || wake)
        {
            await Drive();
            Require(actor.Calls == 1 && store.Messages.Count == 1 && store.Instances[id].State == "[]", "ordinary boundary was not actually held");
        }
        await Add("release"); await Drive(); time += 4_000; await worker.BeatAsync();
        var expected = wake ? new[] { "released", "accepted" } : new[] { "accepted", "released" };
        Require(Json.Read<List<string>>(store.Instances[id].State).SequenceEqual(expected), "generic actor lost legacy batching or default committed wake");
        Require(worker.Owns && store.Messages.Count == 0 && store.Ghost.Count == 0 && !worker.Ready.Any() && !worker.Trial.Any(),
            "generic actor left work stranded or lost ownership");
        if (!qualified && !wake)
        {
            Require(actor.Queries == 0, "legacy selection consulted an existing singleton classifier");
            Require(actor.Batches.Any(b => b.SequenceEqual(new[] { "boundary", "release" })), "legacy boundary/release were split");
        }
        if (qualified)
        {
            Require(actor.Queries > 0, "qualified selection stopped consulting the contextual classifier");
            Require(actor.Batches.Where(b => b.Contains(release ? "release" : "boundary")).All(b => b.Length == 1), "qualified classifier lost its singleton boundary");
        }
        if (wake) Require(actor.Calls == (qualified ? 2 : 3), "committed wake did not revisit exactly the previously held ordinary input");
    }
}
