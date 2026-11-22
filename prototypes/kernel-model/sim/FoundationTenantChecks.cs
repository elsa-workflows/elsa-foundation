using System.Collections.Concurrent;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class FoundationTenantChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long start = 1_701_700_000_000;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        var probe = new Probe();
        var first = new Contract.BoundScope("acme");
        var second = new Contract.BoundScope("beta");
        var ingress = new InMemoryIngress(store, new Requests("tenant-isolation"), "tenant-isolation");
        var reads = new ActorList(store, 10);
        var spec = new Contract.StartSpec(probe.Type, "ordinary");
        var keyed = new Contract.Target.Key("invoice-9", spec with { TypeData = "keyed" });
        var eventStart = spec with { TypeData = "event-start" };
        var waitEvent = new Contract.Target.Event("order-17");
        var createEvent = new Contract.Target.Event("create-order");
        var keep = new Contract.AddOptions(KeepFor: TimeSpan.FromMinutes(1));

        var left = await ingress.SendAsync(first, new Contract.Target.Start(spec), "birth", Contract.Kinds.Start);
        var right = await ingress.SendAsync(second, new Contract.Target.Start(spec), "birth", Contract.Kinds.Start);
        var repeatedBirth = await ingress.SendAsync(first, new Contract.Target.Start(spec), "birth", Contract.Kinds.Start);
        var separateBirths = IsDistinct(left, right) && repeatedBirth is { Outcome: Contract.AdmissionOutcome.Duplicate } &&
            repeatedBirth.Actor == left.Actor;
        check(separateBirths, "T13 tenant-scoped outside operation receipts admit both roots and deduplicate only the original tenant");
        if (!separateBirths) return;

        var leftKey = await ingress.SendAsync(first, keyed, "keyed-birth", Contract.Kinds.Start);
        var rightKey = await ingress.SendAsync(second, keyed, "keyed-birth", Contract.Kinds.Start);
        var separateKeys = IsDistinct(leftKey, rightKey);
        check(separateKeys, "T13 identical caller keys and operation ids bind two tenant-owned actors");
        if (!separateKeys) return;

        var leftActor = leftKey.Actor!.Value;
        var rightActor = rightKey.Actor!.Value;
        var leftRef = new Contract.Target.Ref(leftActor);
        var rightRef = new Contract.Target.Ref(rightActor);
        var foreign = await ingress.SendAsync(second, leftRef, "op-1", "note", "foreign");
        var leftSend = await ingress.SendAsync(first, leftRef, "op-1", "note", "acme/direct");
        var rightSend = await ingress.SendAsync(second, rightRef, "op-1", "note", "beta/direct");
        var repeatedSend = await ingress.SendAsync(first, leftRef, "op-1", "note", "acme/duplicate");
        check(foreign.Outcome == Contract.AdmissionOutcome.Refused && IsDistinct(leftSend, rightSend) &&
              repeatedSend is { Outcome: Contract.AdmissionOutcome.Duplicate } && repeatedSend.Actor == leftActor,
            "T2 T13 both direct sends keep their own occurrence and the foreign target is refused");

        var firstEarly = await ingress.SendAsync(first, waitEvent, "early", "publish", "acme/early", keep);
        var secondEarly = await ingress.SendAsync(second, waitEvent, "early", "publish", "beta/early", keep);
        await using var owner = await Owner("first-owner");
        await Pump(owner);
        await ingress.SendAsync(first, leftRef, "register", "keep");
        await ingress.SendAsync(second, rightRef, "register", "keep");
        await ingress.SetStartAsync(first, createEvent.CorrelationKey, "creator", eventStart);
        await ingress.SetStartAsync(second, createEvent.CorrelationKey, "creator", eventStart);
        await Pump(owner);
        await owner.DisposeAsync();

        now++;
        await using var successor = await Owner("successor");
        var firstLive = await ingress.SendAsync(first, waitEvent, "live", "publish", "acme/live");
        var secondLive = await ingress.SendAsync(second, waitEvent, "live", "publish", "beta/live");
        var firstCreate = await ingress.SendAsync(first, createEvent, "create", "publish", "acme/created");
        var secondCreate = await ingress.SendAsync(second, createEvent, "create", "publish", "beta/created");
        await Pump(successor);
        var admissions = new[] { firstEarly, secondEarly, firstLive, secondLive, firstCreate, secondCreate };
        check(admissions.All(receipt => receipt.Outcome == Contract.AdmissionOutcome.Accepted) &&
            admissions.Select(receipt => receipt.Publication?.Incarnation).Distinct().Count() == admissions.Length &&
            History(first, leftActor) is ["acme/direct", "acme/early", "acme/live"] &&
            History(second, rightActor) is ["beta/direct", "beta/early", "beta/live"],
            "T3 T13 live and kept events with colliding ids reach only the bound tenant after takeover");

        var leftList = await reads.ListAsync(first, new Contract.ActorQuery());
        var rightList = await reads.ListAsync(second, new Contract.ActorQuery());
        var listedLeft = leftList.Items.Select(view => view.Header.Ref).ToHashSet();
        var listedRight = rightList.Items.Select(view => view.Header.Ref).ToHashSet();
        check(leftList.Items.Count == 3 && rightList.Items.Count == 3 && listedLeft.Count == 3 && listedRight.Count == 3 &&
            left.Actor is { } leftRoot && listedLeft.Contains(leftRoot) && listedLeft.Contains(leftActor) &&
            right.Actor is { } rightRoot && listedRight.Contains(rightRoot) && listedRight.Contains(rightActor) &&
            !listedLeft.Overlaps(listedRight) &&
            leftList.Items.All(view => view.Header.Tenant == first.Tenant) &&
            rightList.Items.All(view => view.Header.Tenant == second.Tenant) &&
            leftList.Items.Count(view => view.Header.TypeData == "event-start" &&
                History(first, view.Header.Ref) is ["acme/created"]) == 1 &&
            rightList.Items.Count(view => view.Header.TypeData == "event-start" &&
                History(second, view.Header.Ref) is ["beta/created"]) == 1,
            "T4 T13 an exact event-created life and read listing stay bound to each tenant");
        check((await reads.GetAsync(first, rightActor)) is null && (await reads.GetAsync(second, leftActor)) is null &&
            await reads.GetAsync(first, leftActor) is { Header.Tenant: "acme" } &&
            await reads.GetAsync(second, rightActor) is { Header.Tenant: "beta" },
            "T13 the point read refuses a foreign exact reference and returns the bound checkpoint");

        await ingress.SendAsync(first, leftRef, "job", "job", "acme/effect");
        await ingress.SendAsync(second, rightRef, "job", "job", "beta/effect");
        await Pump(successor);
        check(probe.Effects.Count == 2 && probe.Effects.Contains((leftActor, "acme/effect")) &&
            probe.Effects.Contains((rightActor, "beta/effect")) &&
            History(first, leftActor) is ["acme/direct", "acme/early", "acme/live", "acme/effect"] &&
            History(second, rightActor) is ["beta/direct", "beta/early", "beta/live", "beta/effect"],
            "T13 each Worker job receives only its actor's arguments and settles into that exact actor");

        var missingScope = false;
        try { await ingress.SendAsync(null!, leftRef, "unbound", "note"); }
        catch (ArgumentNullException error) when (error.ParamName == "scope") { missingScope = true; }
        check(missingScope && store.Messages.Values.All(row => row.Message.Id != "unbound"),
            "T11 the public ingress refuses an unbound send before admitting a message");

        var rightBefore = store.ReadFoundationActor(second.Tenant, rightActor.Id).Actor!.Row;
        var rightMessageKeys = store.Messages.Values.Where(row => row.Message.Target.Id == rightBefore.Id)
            .Select(row => row.Message.InboxKey).ToHashSet();
        var rightJournal = store.Journal.Values.Where(row => row.Instance == rightBefore.Id).ToArray();
        var missingRoot = (await ingress.SendAsync(first, new Contract.Target.Start(spec), "missing-probe", Contract.Kinds.Start)).Actor!.Value;
        await ingress.SendAsync(first, leftRef, "send-foreign", "send-target", rightActor.Id);
        await ingress.SendAsync(first, new Contract.Target.Ref(missingRoot), "send-missing", "send-target", "unissued");
        await Pump(successor);
        var foreignFault = store.ReadFoundationActor(first.Tenant, leftActor.Id).Actor!.Row;
        var missingFault = store.ReadFoundationActor(first.Tenant, missingRoot.Id).Actor!.Row;
        check(foreignFault is { Status: InstanceStatus.Faulted, Fault: { } refusal } &&
              refusal.Contains("The actor reference was not found.", StringComparison.Ordinal) &&
              missingFault is { Status: InstanceStatus.Faulted } && missingFault.Fault == refusal &&
              store.Journal.Values.Any(entry => entry.Instance == foreignFault.Id &&
                  entry.Entries.Contains("parked", StringComparison.Ordinal)),
            "T7 a turn's foreign exact reference follows the same not-found path as an unissued reference");

        var root = left.Actor!.Value;
        await ingress.SendAsync(first, new Contract.Target.Ref(root), "start-generated", "start-with-hint", rightActor.Id);
        await Pump(successor);
        var child = new Contract.ActorRef(History(first, root)!.Last());
        var ownChild = await reads.GetAsync(first, child);
        check(child != rightActor && ownChild is { Header.Tenant: "acme" } && ownChild.Header.Parent == root &&
              (await reads.GetAsync(second, child)) is null,
            "T7 a turn cannot choose another tenant's generated address for Start");

        var delivered = new Message("cross-tenant-compose", new(rightBefore.Id), "note", "must not reach beta", foreignFault.Id,
            TargetCreation: rightBefore.Creation, SenderCreation: foreignFault.Creation) { ActorAuthored = true }.AsIssued();
        var batch = new TurnCommit(foreignFault.Id, null, null, [], [], [delivered], [], [], "must not commit");
        var fence = store.Partitions[0];
        var rejected = false;
        try { await store.CommitAsync(new(0, fence.Epoch, fence.Position, Defaults.Lease, false, [batch], [], [])); }
        catch (StoreException error) when (error.Failure == Failure.Definite) { rejected = true; }
        check(rejected && !store.Messages.ContainsKey(delivered.InboxKey) &&
              store.ReadFoundationActor(second.Tenant, rightActor.Id).Actor!.Row == rightBefore &&
              store.Messages.Values.Where(row => row.Message.Target.Id == rightBefore.Id).Select(row => row.Message.InboxKey).ToHashSet().SetEquals(rightMessageKeys) &&
              store.Journal.Values.Where(row => row.Instance == rightBefore.Id).SequenceEqual(rightJournal) &&
              store.Journal.Values.All(row => row.Entries != "must not commit"),
            "T7 the fenced store rejects a composed foreign-tenant send before it can change either tenant's rows");
        await CheckStoredScope(check);

        async Task<IScopeActivation> Owner(string node)
        {
            var worker = new Worker(node, 0, store, new ActorAdapter(store, probe),
                new WorkerOptions { Monotonic = () => now - start });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }

        List<string>? History(Contract.BoundScope scope, Contract.ActorRef actor) =>
            store.ReadFoundationActor(scope.Tenant, actor.Id).Actor is { } checkpoint
                ? Json.Read<List<string>>(checkpoint.Row.State) : null;
    }

    private static async Task CheckStoredScope(Action<bool, string> check)
    {
        const long start = 1_702_700_000_000;
        var now = start;
        var store = new InMemoryStore(1, () => now) { IngressDeduplicationWindow = TimeSpan.FromMilliseconds(100) };
        var scope = new Contract.BoundScope("stored-tenant");
        var observed = new List<FoundationScopeCheck>();
        store.OnInvalidStoredScope = observed.Add;
        var probe = new Probe();
        var ingress = new InMemoryIngress(store, new Requests("scope-validation"), "scope-validation");
        var worker = new Worker("scope-validation", 0, store, new ActorAdapter(store, probe),
            new WorkerOptions { PollLimit = 1, Monotonic = () => now - start });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var birth = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "scope")), "start", Contract.Kinds.Start);
        var actor = birth.Actor!.Value;
        var target = new Contract.Target.Ref(actor);
        await Pump(owner);
        var current = store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row;
        var address = new CreationAddress(current.Id, current.Creation);
        await ingress.SendAsync(scope, target, "missing-tenant", "note", "INVALID");
        await ingress.SendAsync(scope, target, "wrong-tenant", "note", "FOREIGN");
        await ingress.SendAsync(scope, target, "sound-tenant", "note", "valid");
        var corrupt = store.Messages.Values.Single(message => message.Message.Id == "missing-tenant");
        var foreign = store.Messages.Values.Single(message => message.Message.Id == "wrong-tenant");
        var retained = store.Messages.Values.Single(message => message.Message.Id == "sound-tenant");
        var validScope = new[] { corrupt, foreign, retained }.All(row => row.Scoped && row.Tenant == scope.Tenant) &&
            store.FoundationCheckpointTenants.GetValueOrDefault(address) == scope.Tenant;
        corrupt.Tenant = null;
        foreign.Tenant = "another-tenant";
        await Pump(owner);
        current = store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row;
        check(validScope && observed.Contains(FoundationScopeCheck.Poll) &&
              Json.Read<List<string>>(current.State) is ["valid"] && retained.Done &&
              corrupt is { Done: false, Seen: 0 } && foreign is { Done: false, Seen: 0 },
            "T11 the owner refuses and reports invalid message tenants ahead of valid work in a one-row poll");

        await ingress.SendAsync(scope, target, "late-checkpoint", "note", "unsafe");
        var pending = store.Messages.Values.Single(message => message.Message.Id == "late-checkpoint");
        var fence = worker.Fences.Single();
        var read = await store.PollAsync(fence.Partition, fence.Epoch, TimeSpan.Zero, 1);
        store.FoundationCheckpointTenants.Remove(address);
        var malformed = await store.LoadAsync([current.Id]);
        var proposal = new TurnCommit(current.Id, current with { Revision = current.Revision + 1 }, current.Revision,
            [pending.Message.InboxKey], [], [], [], [], "must not commit");
        var refused = false;
        try { await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [proposal], [], [])); }
        catch (StoreException error) when (error.Failure == Failure.Definite) { refused = true; }
        check(read.Messages is [var observedInput] && observedInput.Message.InboxKey == pending.Message.InboxKey &&
              malformed.Count == 0 && observed.Contains(FoundationScopeCheck.Load) && refused &&
              observed.Contains(FoundationScopeCheck.Commit) && store.Instances[current.Id] == current && !pending.Done,
            "T11 a missing checkpoint tenant is refused on load and under the real fence after a successful delivery read");

        // Model a persisted Done row with damaged metadata. The sweep must leave its evidence intact.
        corrupt.Done = true;
        now += 101;
        await store.SweepAsync();
        check(observed.Contains(FoundationScopeCheck.Sweep) && store.Messages.ContainsKey(corrupt.Message.InboxKey) &&
              store.Messages.ContainsKey(retained.Message.InboxKey),
            "T11 the sweep reports and retains expired rows with a missing message or checkpoint tenant");

        store.FoundationCheckpointTenants[address] = scope.Tenant;
        await store.SweepAsync();
        check(store.Messages.ContainsKey(corrupt.Message.InboxKey) && !store.Messages.ContainsKey(retained.Message.InboxKey),
            "T11 a damaged row does not block a healthy expired row from being swept");
    }

    private static bool IsDistinct(Contract.Admission first, Contract.Admission second) =>
        first is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } reference } &&
        second is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } other } && reference != other;

    private static async Task Pump(IScopeActivation owner)
    {
        for (var pass = 0; pass < 40; pass++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "tenant-check";
        internal ConcurrentQueue<(Contract.ActorRef Actor, string? Argument)> Effects { get; } = new();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var history = turn.State is null ? [] : Json.Read<List<string>>(turn.State);
            foreach (var message in turn.Inbox)
            {
                switch (message)
                {
                    case { Kind: "keep" }:
                        turn.Subscribe("order-17", "slot", keep: true);
                        break;
                    case { Kind: "job" }:
                        turn.StartJob("effect", message.Payload);
                        break;
                    case { Kind: "send-target", Payload: { } target }:
                        turn.Send(new Contract.ActorRef(target), "note", "cross-tenant attempt");
                        break;
                    case { Kind: "start-with-hint", Payload: { } hint }:
                        history.Add(turn.Start(new(Type, hint)).Id);
                        break;
                    case { Kind: Contract.Kinds.JobFinished, Payload: { } result }:
                        if (Json.Read<Contract.JobFinished>(result) is { Error: null, Result: { } value })
                            history.Add(Json.Read<string>(value));
                        break;
                    case { Payload: { } content }:
                        history.Add(content);
                        break;
                }
            }
            turn.State = Json.Write(history);
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            Effects.Enqueue((job.Actor, job.Arguments));
            return new(job.Arguments);
        }

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
