using Kernel.Workflows;
using Public = Kernel.Foundation.Contracts.Kinds;

namespace Kernel.Sim;

internal static class PrivateKindsChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const string actor = "0/consumer";
        var store = new InMemoryStore(1, () => 1_700_000_000_000);
        var worker = new Worker("private-kinds", 0, store, new WorkflowInstance([]));
        await using var active = (await new DurableHost([worker]).ActivateAsync(0))!;
        var lease = worker.Fences.Single();
        var turn = new DurableTurn(new TurnInput(worker, new Slot(), actor, [], 0, new("model", "definition"), worker.Now));

        await store.AcceptAsync(new KeyRequest("known", "start", Kinds.Start, Start: new("model", "definition"), KeepForMillis: 10_000));
        store.AdmitFoundationKey("scope", "known", "start", Kinds.Start, null, new("model", "definition"), null, null);
        var before = store.Messages.Count;
        foreach (var kind in new[] { "kernel.drain", "kernel.park-expiry", "kernel.cascade", "kernel.future-kind", "kernel.archive" })
        {
            var message = new Message(kind, new(actor), kind, Sender: actor).AsIssued();
            await Refused(() => store.AddAsync(message).AsTask(), $"store add rejects {kind}");
            await Refused(() => Task.FromResult(worker.Post(message)), $"local post rejects {kind}");
            await Refused(() => store.AcceptAsync(new KeyRequest("known", "command", kind)).AsTask(), $"legacy keyed add rejects {kind}");
            await Refused(() => Task.FromResult(store.AdmitFoundationKey("scope", "known", "command", kind, null, null, null, null)), $"bound keyed add rejects {kind}");
            await Refused(() => Task.FromResult(turn.Send(new InstanceRef(actor), kind)), $"turn send rejects {kind}");
            await Refused(() => Task.FromResult(turn.SendFromReceivedAddress(actor, new(actor), kind, null)), $"turn send from a received address rejects {kind}");
            await Refused(() => { turn.Add(message); return Task.CompletedTask; }, $"composed turn send rejects {kind}");

            var composed = new TurnCommit(actor, null, null, [], [], [message], [], [], null);
            try
            {
                await store.CommitAsync(new CommitBatch(0, lease.Epoch, lease.Position, TimeSpan.FromSeconds(10), false, [composed], [], []));
                check(false, $"direct outcome rejects {kind} as a definite error");
            }
            catch (StoreException error) when (error.Failure == Failure.Definite)
            {
                check(true, $"direct outcome rejects {kind} as a definite error");
            }
        }

        check(store.Messages.Count == before && turn.SentCount == 0,
            "private-kind refusals leave the store and the turn without any send");
        foreach (var command in new[] { Public.Suspend, Public.Resume, Public.Terminate, Public.Delete, Public.Recover, Public.Discard, Public.Migrate })
        {
            var accepted = await store.AddAsync(new Message(command, new(actor), command));
            check(accepted, $"the reserved prefix still admits public command {command}");
        }
        turn.StartJob("trusted");
        var row = new InstanceRow(actor, "model", "definition", InstanceStatus.Running, "null", 1, null);
        var sealedTurn = turn.Seal(turn.Outcome(InstanceStatus.Running), row, null, null);
        var job = sealedTurn.Sent.Single(message => message.Kind == Kinds.Job);
        var kernelOutcome = new TurnCommit(actor, null, null, [], [], [job], [], [], null);
        var commit = await store.CommitAsync(new CommitBatch(0, lease.Epoch, lease.Position, TimeSpan.FromSeconds(10), false, [kernelOutcome], [], []));
        check(commit.Landed && store.Messages.ContainsKey(job.InboxKey), "the trusted StartJob ticket still lands");
        foreach (var returnsFromCommand in new[] { false, true })
        {
            await ComposedVolatile(returnsFromCommand, copiesTicket: false, check);
            await ComposedVolatile(returnsFromCommand, copiesTicket: true, check);
        }

        async Task Refused(Func<Task> act, string expectation)
        {
            try
            {
                await act();
                check(false, expectation);
            }
            catch (ArgumentException)
            {
                check(true, expectation);
            }
        }
    }

    private static async Task ComposedVolatile(bool returnsFromCommand, bool copiesTicket, Action<bool, string> check)
    {
        const string address = "0/volatile";
        var store = new InMemoryStore(1, () => 1_700_000_000_000);
        var type = new DirectOutcomeProbe(returnsFromCommand, copiesTicket);
        var worker = new Worker("volatile-kind", 0, store, type);
        await using var active = (await new DurableHost([worker]).ActivateAsync(0))!;
        worker.Post(new("boot", new(address), Kinds.Start, Start: new("probe", "none", Ephemeral: true)), fresh: true);
        await active.RunAsync();
        var parked = store.Instances.GetValueOrDefault(address);
        check(type.Outcomes == 1 && parked is { Status: InstanceStatus.Faulted, Fault: "The outcome includes a private kernel kind with no issuer." } &&
            !store.Messages.Values.Any(row => row.Message.Id == "invalid") && !worker.Messages.Any(item => item.Id.Id == "invalid"),
            $"a directly composed volatile {(returnsFromCommand ? "command" : "authored turn")} parks before local handover (copied ticket: {copiesTicket})");
    }

    private sealed class DirectOutcomeProbe(bool returnsFromCommand, bool copiesTicket) : IActorType
    {
        internal int Outcomes { get; private set; }
        public bool Withholds(InstanceRow row, ITurnServices services) => false;
        public TurnOutcome? Command(TurnInput input) => returnsFromCommand ? Compose(input) : null;
        public ValueTask<TurnOutcome> TurnAsync(TurnInput input) => new(Compose(input));

        private TurnOutcome Compose(TurnInput input)
        {
            Outcomes++;
            var message = new Message("invalid", new(input.Actor), "kernel.future-kind", Sender: input.Actor).AsIssued();
            if (copiesTicket)
            {
                var turn = new DurableTurn(input);
                turn.StartJob("real");
                var checkpoint = new InstanceRow(input.Actor, "probe", "none", InstanceStatus.Running, "null", 1, null);
                var outcome = turn.Seal(turn.Outcome(InstanceStatus.Running), checkpoint, null, null);
                var issued = outcome.Sent.Single();
                message = issued with { Id = message.Id, Kind = message.Kind };
            }
            return new(input.Actor) { Consumed = [.. input.Taken], Sent = [message], Volatile = [message.InboxKey] };
        }

        public TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error) =>
            new(actor) { Row = new(actor, "probe", "none", InstanceStatus.Faulted, "null", 1, null, Fault: error),
                Status = InstanceStatus.Faulted, Consumed = [.. taken] };

        public ValueTask<object?> ExecuteJobAsync(string model, IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
