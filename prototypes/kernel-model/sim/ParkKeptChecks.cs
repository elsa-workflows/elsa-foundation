using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkKeptChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long initial = 1_701_400_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var actor = new Probe();
        var scope = new Contract.BoundScope("park");
        Worker MakeOwner(string node) => new(node, 0, store, new ActorAdapter(store, actor),
            new WorkerOptions { Monotonic = () => now - initial });
        var worker = MakeOwner("park-first");
        Contract.IIngress edge = new InMemoryIngress(store, new Requests("park-first"), "park-first");
        var start = await edge.SendAsync(scope, new Contract.Target.Start(new(actor.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(start.Actor!.Value);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(owner);

        await edge.SendAsync(scope, target, "older", "update", options: new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1)));
        await Pump(owner);
        var row = store.Instances.Values.Single();
        var ordinary = store.Messages.Values.Single(message => message.Message.Id == "older");
        check(ordinary is { Done: true, KeptBy: null } && row.State == "saved", "ordinary done input has no Park disposition");

        var poison = new Message("twin", new(row.Id), "poison", "private payload", TargetCreation: row.Creation).AsIssued();
        var twin = new Message("twin", new(row.Id), "update", "ordinary payload", TargetCreation: row.Creation, KeepUntil: now + 2);
        var staged = new TurnCommit(row.Id, null, null, [], [], [poison, twin], [], [], null);
        var fence = worker.Fences.Single();
        var wrote = await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [staged], [], []));
        check(wrote.Landed, "the fixture inserts both full keys under the owner's fence");
        await Pump(owner);

        var parked = await new ActorList(store, 10).GetAsync(scope, target.Actor);
        check(parked is { Header.Status: Contract.ActorStatus.Faulted, Park: not null } && store.Instances[row.Id].State == "saved",
            "the real failed turn parks without changing the last saved state");
        check(Retained(poison, "private payload", ParkKeepReason.Consumed) && Retained(twin, "ordinary payload", ParkKeepReason.Consumed),
            "the Park keeps both consumed deliveries by full key with their original payloads");
        check(store.Journal.Values.Count(entry => entry.Entries.Contains("parked", StringComparison.Ordinal)) == 1,
            "the Park checkpoint and journal land once");

        var priorOffers = actor.Offers;
        var admission = await edge.SendAsync(scope, target, "twin", "update", "late payload",
            options: new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1)));
        var late = store.Messages.Values.Single(message => message.Message.Payload == "late payload").Message;
        var checkpoint = store.Instances[row.Id];
        fence = worker.Fences.Single();
        var malformed = new TurnCommit(row.Id, checkpoint with { Revision = checkpoint.Revision + 1 }, checkpoint.Revision,
            [], [late.InboxKey], [], [], [], Json.Write(new[] { "must not land" }))
        { FoundationKept = new Dictionary<InboxKey, ParkKeepReason> { [late.InboxKey] = ParkKeepReason.Arrived } };
        var rejected = false;
        try
        {
            await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false,
                [malformed], [], [late.InboxKey]));
        }
        catch (StoreException error) when (error.Failure == Failure.Definite) { rejected = true; }
        check(rejected && store.Instances[row.Id] == checkpoint && store.Messages[late.InboxKey] is { Done: false, Trial: 0, KeptBy: null } &&
            store.Journal.Values.All(entry => !entry.Entries.Contains("must not land", StringComparison.Ordinal)),
            "a commit cannot keep a row and also put it on trial");

        await Pump(owner);
        check(admission.Outcome == Contract.AdmissionOutcome.Accepted && late.InboxKey != poison.InboxKey && late.InboxKey != twin.InboxKey &&
            Retained(late, "late payload", ParkKeepReason.Arrived) && actor.Offers == priorOffers &&
            (await new ActorList(store, 10).GetAsync(scope, target.Actor))?.Park == parked?.Park,
            "a late input with the same wire ID is kept as Arrived without entering the actor or replacing the Park");

        await edge.SendAsync(scope, target, "exhausted", "update", "exhausted payload",
            options: new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1)));
        var exhausted = store.Messages.Values.Single(message => message.Message.Id == "exhausted");
        exhausted.Attempts = 3;
        await Pump(owner);
        check(Retained(exhausted.Message, "exhausted payload", ParkKeepReason.Consumed) && actor.Offers == priorOffers &&
            (await new ActorList(store, 10).GetAsync(scope, target.Actor))?.Park == parked?.Park,
            "an exhausted late delivery is Consumed by the same Park on re-park");

        await edge.SendAsync(scope, target, "refused", "update", "refused payload",
            options: new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1)));
        var refused = store.Messages.Values.Single(message => message.Message.Id == "refused").Message;
        var refusals = 0;
        store.Refuses = candidate => candidate.Id == row.Id && ++refusals <= 2;
        await Pump(owner);
        store.Refuses = null;
        check(refusals == 3 && Retained(refused, "refused payload", ParkKeepReason.Consumed) && actor.Offers == priorOffers &&
            (await new ActorList(store, 10).GetAsync(scope, target.Actor))?.Park == parked?.Park,
            "a definite arrival commit refusal re-parks and keeps the input as Consumed");

        now += 3;
        await store.SweepAsync();
        check(!store.Messages.ContainsKey(ordinary.Message.InboxKey) && Retained(poison, "private payload", ParkKeepReason.Consumed) &&
            Retained(twin, "ordinary payload", ParkKeepReason.Consumed) && Retained(late, "late payload", ParkKeepReason.Arrived) &&
            Retained(exhausted.Message, "exhausted payload", ParkKeepReason.Consumed) && Retained(refused, "refused payload", ParkKeepReason.Consumed),
            "the sweep deletes expired ordinary markers and preserves expired kept rows");
        await owner.DisposeAsync();
        var successorWorker = MakeOwner("park-next");
        await using var successor = (await new DurableHost([successorWorker]).ActivateAsync(0))!;
        await Pump(successor);
        check(actor.PoisonOffers == 1 && actor.Offers == priorOffers && Retained(poison, "private payload", ParkKeepReason.Consumed) &&
            Retained(twin, "ordinary payload", ParkKeepReason.Consumed) && Retained(late, "late payload", ParkKeepReason.Arrived),
            "takeover does not offer done kept rows to the parked actor");

        await CheckKeptPages(store, successorWorker.Fences.Single(), row.Id, row.Creation, check);

        foreach (var timed in new[] { false, true })
            await CaptureParkedCompletion(timed, (ok, name) => check(ok, $"{(timed ? "timed" : "untimed")} completion: {name}"));

        bool Retained(Message message, string payload, ParkKeepReason reason) => store.Messages.GetValueOrDefault(message.InboxKey) is
            { Done: true, Held: false, Trial: 0, KeptBy: var actualReason, Message.Payload: var actual } && actual == payload && actualReason == reason;
    }

    private static async Task CheckKeptPages(InMemoryStore store, PartitionFence fence, string id, long creation, Action<bool, string> check)
    {
        var address = new CreationAddress(id, creation);
        var identity = store.FoundationIdentity(id, creation);
        var park = store.ReadFoundationActor(identity.Tenant, identity.Reference).Actor!.Park!;
        const int wideBytes = 5 * 1024 * 1024;
        var widePayload = new string('x', wideBytes);
        var input = Enumerable.Range(0, InMemoryStore.ParkPageLimit + 2).Reverse().Select(index =>
            new Message("page-tie", new(id), "update", index == 0 ? widePayload : null, TargetCreation: creation)
                .WithInboxScope(index / 2, index % 2 == 0 ? null : "match"))
            .ToArray();
        var keys = input.Select(message => message.InboxKey).ToArray();
        var add = new TurnCommit(id, null, null, [], [], input, [], [], null);
        var added = await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [add], [], []));
        var checkpoint = store.Instances[id];
        var keep = new TurnCommit(id, checkpoint with { Revision = checkpoint.Revision + 1 }, checkpoint.Revision,
            [], keys, [], [], [], null)
        { FoundationKept = keys.ToDictionary(key => key, _ => ParkKeepReason.Arrived) };
        var kept = await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [keep], [], []));
        check(added.Landed && kept.Landed, "the fenced store retains the complete paging fixture");

        // Model an allowed provider order-key collision without depending on the in-memory counter to generate one.
        var sharedSeq = store.Messages[keys[0]].Seq;
        foreach (var key in keys) store.Messages[key].Seq = sharedSeq;
        var after = new ParkPageCursor(sharedSeq - 1, new InboxKey("\uffff"));
        var front = store.ReadFoundationKeptPage(address, park, after)!;
        var start = keys[^1];
        check(front.Count == InMemoryStore.ParkPageLimit && front[0] == new ParkPageRow(new(sharedSeq, start), "update", null, ParkKeepReason.Arrived, wideBytes),
            "the page starts with an oversized payload's metadata and enforces its row limit");
        var tail = store.ReadFoundationKeptPage(address, park, front[^1].Cursor)!;
        check(front.Concat(tail).Select(item => item.Cursor.Key).SequenceEqual(keys.Reverse()) &&
            tail.Count == 2 && store.ReadFoundationKeptPage(address, park, tail[^1].Cursor) is { Count: 0 },
            "the full tuple advances through scope and match ties without skipping or repeating a kept input");
        check(store.ReadFoundationKeptPage(address, park with { Revision = park.Revision + 1 }, null) is null &&
            store.ReadFoundationKeptPage(address with { Creation = creation + 1 }, park, null) is null,
            "a mismatched Park mark or creation is distinguishable from an empty page");

        checkpoint = store.Instances[id];
        var altered = front.ToArray();
        altered[^1] = altered[^1] with { Reason = ParkKeepReason.Consumed };
        var divergent = false;
        try { await Clear(fence, altered); }
        catch (StoreException error) when (error.Failure == Failure.Unknown) { divergent = true; }
        var rolledBack = divergent && store.Instances[id] == checkpoint &&
            store.Partitions[fence.Partition].Position == fence.Position &&
            front.All(item => store.Messages[item.Cursor.Key].KeptBy == item.Reason);
        check(rolledBack, "one mismatched kept reason rolls back the entire page and checkpoint");
        if (!rolledBack) return;

        var stale = await Clear(fence with { Epoch = fence.Epoch - 1 }, front);
        check(!stale.Landed && store.Instances[id] == checkpoint && store.Messages[start].KeptBy == ParkKeepReason.Arrived,
            "a stale fence cannot clear kept inputs");

        var cleared = await Clear(fence, front);
        var until = store.FoundationClock() + (long)store.IngressDeduplicationWindow.TotalMilliseconds;
        check(cleared.Landed && store.Instances[id].Revision == checkpoint.Revision + 1 &&
            store.Partitions[fence.Partition].Position == fence.Position + 1 &&
            front.All(item => store.Messages[item.Cursor.Key] is { Done: true, KeptBy: null, Message.KeepUntil: var expires } && expires == until) &&
            store.Messages[start].Message.Payload == widePayload && tail.All(item => store.Messages[item.Cursor.Key].KeptBy == item.Reason),
            "one fenced commit clears exactly the page and refreshes its window while the suffix stays kept");

        ValueTask<Committed> Clear(PartitionFence authority, IReadOnlyList<ParkPageRow> page)
        {
            var turn = new TurnCommit(id, checkpoint with { Revision = checkpoint.Revision + 1 }, checkpoint.Revision, [],
                [], [], [], [], "[\"page\"]") { FoundationParkClear = new(park, page) };
            return store.CommitAsync(new(authority.Partition, authority.Epoch, authority.Position, Defaults.Lease, false, [turn], [], []));
        }
    }

    private static async Task CaptureParkedCompletion(bool timed, Action<bool, string> check)
    {
        const long now = 1_701_400_010_000;
        const string jobKey = "background";
        const int payloadLimit = 16 * 1024 * 1024 - 2 * 24 * 1024;
        var overhead = System.Text.Encoding.UTF8.GetByteCount(Json.Write(new JobFinished(jobKey, Json.Write(""), null)));
        var store = new InMemoryStore(1, () => now);
        var actor = new ParkedJobProbe(new string('x', payloadLimit - overhead + 1), timed);
        var scope = new Contract.BoundScope("parked-job");
        Worker MakeOwner(string node) => new(node, 0, store, new ActorAdapter(store, actor), new WorkerOptions { Monotonic = () => 0 });
        var worker = MakeOwner("parked-job-first");
        Contract.IIngress edge = new InMemoryIngress(store, new Requests(worker.Node), worker.Node);
        var start = await edge.SendAsync(scope, new Contract.Target.Start(new(actor.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(start.Actor!.Value);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(owner);
        var row = store.Instances.Values.Single();
        var task = worker.JobCompletion(row.Id, jobKey, row.Creation)!;
        var ticket = InboxKey.Ticket(row.Id, jobKey, row.Creation);
        var resultKey = InboxKey.JobResult(row.Id, jobKey, row.Creation);
        await edge.SendAsync(scope, target, "fault", "fault");
        await Pump(owner);
        var parked = store.Instances[row.Id];
        check(parked is { Status: InstanceStatus.Faulted, Jobs: not null } && store.Messages.ContainsKey(ticket) && actor.Entries == 1,
            "the fault parks the actor while the entered job and its counted ticket are outstanding");
        check(store.Messages.Values.Count(message => !message.Done && message.Message.Kind == Kinds.JobDeadline) == (timed ? 1 : 0),
            "the parked job still has its pending deadline if armed");

        actor.Complete.SetResult();
        await task.WaitAsync(TimeSpan.FromSeconds(5));
        worker.RunTurn(row.Id);
        await worker.FlushAsync();
        var result = store.Messages.GetValueOrDefault(resultKey);
        check(store.Instances[row.Id] == parked && !store.Messages.ContainsKey(ticket) &&
            result is { Done: false, Message.FoundationJobResult: true } &&
            result.Message.Read<JobFinished>() is { Result: null, Error: "result too large" } &&
            !store.Messages.Values.Any(message => !message.Done && message.Message.Kind == Kinds.JobDeadline),
            "a fenced standalone conversion stores the bounded authenticated fault without rewriting the actor checkpoint");

        await owner.DisposeAsync();
        await using var successor = (await new DurableHost([MakeOwner("parked-job-next")]).ActivateAsync(0))!;
        await Pump(successor);
        check(actor.Entries == 1 && actor.Completions == 0 && store.Instances[row.Id].Jobs == parked.Jobs &&
            store.Messages.GetValueOrDefault(resultKey) is { Done: true, KeptBy: ParkKeepReason.Arrived },
            "a successor keeps the bounded result as an arrival and does not rerun the job");
    }

    private static async Task Pump(IScopeActivation owner)
    {
        for (var step = 0; step < 10; step++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "kept-check";
        public int PoisonOffers { get; private set; }
        public int Offers { get; private set; }
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            Offers++;
            if (turn.Inbox.Any(message => message.Kind == "poison"))
            {
                PoisonOffers++;
                throw new InvalidOperationException("turn fault");
            }
            turn.State = "saved";
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }

    private sealed class ParkedJobProbe(string value, bool timed) : Contract.IActorType
    {
        public string Type => "parked-job-check";
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Entries { get; private set; }
        public int Completions { get; private set; }
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case Contract.Kinds.Start: turn.StartJob("background", timeout: timed ? TimeSpan.FromSeconds(1) : null); break;
                    case "fault": throw new InvalidOperationException("park with a running job");
                    case Contract.Kinds.JobFinished: Completions++; break;
                }
            return default;
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            Entries++;
            await Complete.Task.WaitAsync(cancellationToken);
            return value;
        }

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
