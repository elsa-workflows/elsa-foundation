using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static partial class ParkHandoffCreditChecks
{
    private static async Task AcrossParks(bool discard, Action<bool, string> check, bool sameOwner = false)
    {
        const long start = 1_702_610_023_000;
        var now = start;
        var name = sameOwner ? "same-owner discard" : discard ? "discard" : "resume";
        var scope = ContractScopes.Bind("cross-park");
        var probe = new Probe { HoldInputs = true, FaultOnSuspend = true };
        var store = new InMemoryStore(1, () => now);
        var ingress = new InMemoryIngress(store, new Requests("cross-park"), "cross-park",
            (_, kind) => kind is Contract.Kinds.Recover or Contract.Kinds.Discard or Contract.Kinds.Suspend);
        var actor = (await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "")),
            "start", Contract.Kinds.Start)).Actor!.Value;
        var target = new Contract.Target.Ref(actor);
        var id = store.FoundationTarget(scope.Tenant, actor.Id)!.Id;
        var issued = new Dictionary<long, HashSet<InboxKey>>();
        var firstGeneration = 0L;
        var restored = 0;
        var clearedOnPage = false;
        var settledDuringDiscard = false;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed) return;
            foreach (var turn in batch.Turns)
            {
                foreach (var control in turn.Sent.Where(message => message.Kind == Kinds.Drain))
                {
                    var generation = control.Read<ParkDrainEnvelope>().Generation;
                    if (!issued.TryGetValue(generation, out var keys)) issued.Add(generation, keys = []);
                    keys.Add(control.InboxKey);
                }
                if (firstGeneration == 0 || Row().ParkResolutionGeneration <= firstGeneration) continue;
                var old = Delivery("older");
                restored += turn.FoundationRecoveryPage?.Rows.Count(item => item.Cursor.Key == old.Message.InboxKey &&
                    item.ReleasedBy?.Generation == firstGeneration) ?? 0;
                clearedOnPage |= turn.FoundationParkClear?.Rows.Any(item => item.Cursor.Key == old.Message.InboxKey) == true;
                settledDuringDiscard |= Row().ParkDiscard is not null && old.Done;
            }
        });
        var first = Worker("first", 32);
        await using var initialOwner = (await new DurableHost([first]).ActivateAsync(0))!;
        await Step(first);
        await Send("park-one", "fault");
        await Step(first);
        await Send("older", "input");
        await Step(first);
        await Resolve("first-open", Contract.Kinds.Recover);
        for (var pass = 0; pass < 20 && Row().ParkCohort is null; pass++) await Step(first);
        var oldRow = Delivery("older");
        var tag = oldRow.ReleasedBy;
        var firstComplete = Row().ParkRecovery is null && Row().ParkCohort == tag && tag is not null &&
            oldRow is { Done: false, Held: true } && Json.Read<List<string>>(Row().State).Count == 0;
        check(firstComplete, $"P-11 {name}: the first Park resolves with an unconsumed release");
        if (!firstComplete) return;
        firstGeneration = tag!.Value.Generation;
        // Suspend is delivered alone. The actor throws while handling it and opens a separate Park.
        await Send("park-two", Contract.Kinds.Suspend);
        await Step(first);
        var parked = Row().Status == InstanceStatus.Faulted && Row().ParkResolutionGeneration == firstGeneration &&
            Row().ParkRecovery is null && !oldRow.Done && oldRow.ReleasedBy == tag;
        check(parked, $"P-11 {name}: the second Park keeps the earlier generation on its pending input");
        if (!parked) return;
        if (!sameOwner) await initialOwner.DisposeAsync();

        probe.HoldInputs = false;
        var resolving = sameOwner ? first : Worker("second", 1);
        await using var owner = sameOwner ? null : await new DurableHost([resolving]).ActivateAsync(0);
        if (!sameOwner)
        {
            await Send("earlier", "input");
            Delivery("earlier").Seq = oldRow.Seq - 2;
        }
        await Resolve("second-open", discard ? Contract.Kinds.Discard : Contract.Kinds.Recover);
        Delivery("second-open").Seq = oldRow.Seq - 1;
        for (var pass = 0; pass < 12 && Row().ParkDiscard is null && Row().ParkRecovery is null; pass++)
            await Step(resolving);
        var generation = Row().ParkResolutionGeneration;
        var opened = generation > firstGeneration && Row().Creation == tag.Value.Creation &&
            (discard ? Row().ParkDiscard is not null : Row().ParkRecovery is not null) &&
            oldRow is { Done: false, KeptBy: null } && oldRow.ReleasedBy == tag &&
            (sameOwner ? oldRow.Held && resolving.RecoveryHandedOff(oldRow.Message.InboxKey) : Delivery("earlier").KeptBy == ParkKeepReason.Arrived) &&
            Json.Read<List<string>>(Row().State).Count == 0;
        check(opened, $"P-11 {name}: the second Open advances the generation with the old release pending");
        if (!opened) return;
        for (var pass = 0; pass < 40 && (Row().ParkDiscard is not null || Row().ParkRecovery is not null || !oldRow.Done); pass++)
            await Step(resolving);
        var state = Json.Read<List<string>>(Row().State);
        var distinctControls = !issued[firstGeneration].Overlaps(issued[generation]);
        var settled = Row().ParkRecovery is null && Row().ParkDiscard is null && oldRow.Done &&
            oldRow.KeptBy is null && store.Messages.Values.All(row => row.KeptBy is null && row.Message.Kind != Kinds.Drain);
        check(distinctControls && settled && (discard
            ? Row().ParkDiscarded && restored == 0 && !clearedOnPage && !settledDuringDiscard && state.Count == 0
            : !Row().ParkDiscarded && restored == 1 && oldRow.ReleasedBy?.Generation == generation &&
                state.SequenceEqual(["earlier", "older"])),
            $"P-11 {name}: both Parks use distinct controls and settle the old release ({restored} restored, " +
            $"cleared on page {clearedOnPage}, settled during discard {settledDuringDiscard}, state {string.Join(",", state)})");

        InstanceRow Row() => store.Instances[id];
        InMemoryStore.MessageRow Delivery(string key) => store.Messages.Values.Single(row => row.Message.Id == key);
        Worker Worker(string node, int limit) => new(node, 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - start, PollLimit = limit });
        ValueTask<Contract.Admission> Send(string key, string kind, string? payload = null) =>
            ingress.SendAsync(scope, target, key, kind, payload);
        async Task Resolve(string key, string kind)
        {
            var selection = (await new ActorList(store, 8).GetAsync(scope, actor))!.Selection;
            var result = await Send(key, kind, Json.Write(new Contract.OperatorCommand(selection)));
            if (result.Outcome != Contract.AdmissionOutcome.Accepted)
                throw new InvalidOperationException($"The cross-Park fixture could not admit {key}.");
        }
        async Task Step(Worker worker)
        {
            now += 11;
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(id);
            await worker.FlushAsync();
        }
    }
}
