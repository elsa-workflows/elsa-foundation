using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkDeleteChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await Delete(protectedAtOpen: false, protectedAtEnd: false, check);
        await Delete(protectedAtOpen: true, protectedAtEnd: true, check);
        await Delete(protectedAtOpen: false, protectedAtEnd: true, check);
        await AttachToDrain(ParkClearKind.Discard, check);
        await AttachToDrain(ParkClearKind.Expiry, check);
        await AttachToDrain(ParkClearKind.Terminate, check);
        await AttachToDrain(ParkClearKind.Terminate, check, beforeFirstPage: true);
        await AttachToDrain(ParkClearKind.Terminate, check, parentTerminate: true);
        await AttachToDrain(ParkClearKind.Terminate, check, parentTerminate: true, beforeFirstPage: true);
        await AttachToDrain(ParkClearKind.Terminate, check, protectsAtComplete: true);
        await AttachToDrain(ParkClearKind.Terminate, check, protectsAtOpen: true);
        await AttachToDrain(ParkClearKind.Terminate, check, protectsAtOpen: true, thenDelete: true);
    }

    private static async Task Delete(bool protectedAtOpen, bool protectedAtEnd, Action<bool, string> check)
    {
        const long initial = 1_702_310_070_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now) { ParkedRetentionWindow = TimeSpan.FromMinutes(10) };
        var scope = new Contract.BoundScope("park-delete");
        var probe = new Probe();
        var requests = new Requests("park-delete");
        var edge = new InMemoryIngress(store, requests, "park-delete", (_, kind) => kind == Contract.Kinds.Delete);
        var commits = new List<TurnCommit>();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed) commits.AddRange(batch.Turns);
            else if (batch.Turns.FirstOrDefault(turn => turn.Delete && turn.Row is { HeldCascade: not null }) is { } complete)
                CheckRetirementGuards(store, batch, complete, check);
        });
        Worker Owner(string name) => new(name, 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - initial, Transport = new Replies(requests) });
        var worker = Owner("park-delete-first");
        await using var first = (await new DurableHost([worker]).ActivateAsync(0))!;
        var parent = (await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "root")), "parent", Contract.Kinds.Start)).Actor!.Value;
        await Step(worker, parent);
        var child = await Spawn(parent);
        var descendant = await Spawn(child);
        var id = Row(child).Id;

        await Send(child, "park", "park");
        await Step(worker, child);
        var reads = new ActorList(store, 10);
        var selected = (await reads.GetAsync(scope, child))!.Selection!;
        var previous = Checkpoint(child);
        var expiry = store.FoundationExpiry(previous.Row)!.Armed!.Value;
        const int count = 2 * InMemoryStore.ParkPageLimit + 1;
        for (var index = 0; index < count - 1; index++)
            await Send(child, $"wait-{index}", "pending");
        await Step(worker, child);

        probe.Protect = protectedAtOpen;
        var beforeOpen = Row(child);
        var payload = Json.Write(new Contract.OperatorCommand(selected));
        var request = await Send(child, "delete", Contract.Kinds.Delete, payload);
        var repeated = protectedAtOpen ? null : edge.RequestAsync(scope, new Contract.Target.Ref(child),
            "different-operation", Contract.Kinds.Delete, payload, null, new Contract.RequestOptions(null, now + 1_000)).AsTask();
        await Step(worker, child);
        if (protectedAtOpen)
        {
            check(request.Outcome == Contract.AdmissionOutcome.Accepted && Row(child) == beforeOpen &&
                Kept() == count && store.Messages.ContainsKey(expiry),
                "a protected parked instance refuses Delete at Open and keeps its Park, expiry and retained inputs");
            return;
        }

        var started = Row(child);
        var opened = started is { Status: InstanceStatus.Faulted, ParkDiscard.Kind: ParkClearKind.Delete, Children: not null } &&
            started.ParkResolutionGeneration == beforeOpen.ParkResolutionGeneration + 1 && Kept() == count &&
            Checkpoint(child).Terminal == previous.Terminal && !store.Messages.ContainsKey(expiry) && Notices() == 0;
        check(opened, "qualified Delete opens the drain without replacing the current Park mark or issuing its final notice");
        if (!opened) return;
        await Step(worker, child);
        var reply = await repeated!.WaitAsync(TimeSpan.FromSeconds(5));
        check(reply is { Error: "in progress", Item: "delete" } &&
              store.Instances[id].ParkDiscard is { Operation: "delete", Cleared: 0 },
            "another qualified Delete reports the operation already owning this drain");
        await Step(worker, child);
        check(Row(child).ParkDiscard?.Cleared == InMemoryStore.ParkPageLimit && Kept() == count - InMemoryStore.ParkPageLimit,
            "Delete clears one bounded page and leaves a durable cursor for its successor");
        await first.DisposeAsync();

        now++;
        var successor = Owner("park-delete-later");
        await using var current = (await new DurableHost([successor]).ActivateAsync(0))!;
        for (var pass = 0; pass < 15 && Row(child).ParkDiscard?.Cleared < count; pass++)
        {
            await Step(successor, parent);
            await Step(successor, child);
        }
        check(Row(child).ParkDiscard?.Cleared == count && Kept() == 0 && Notices() == 0,
            "the successor clears the suffix before the final disposition");
        probe.Protect = protectedAtEnd;
        now++;
        await Step(successor, child);
        await Step(successor, parent);
        var after = store.Instances[id];
        var journal = store.Journal.Values.Where(row => row.Instance == id).ToArray();
        var matched = Notices() == 1 && Kept() == 0 && Row(descendant).Status == InstanceStatus.Running &&
            (protectedAtEnd
                ? after is { Retired: false, Status: InstanceStatus.Faulted, ParkDiscarded: true } &&
                    Checkpoint(child) is { Park: null, Terminal: { } mark } && mark.Revision == after.Revision && mark.At == now &&
                    journal.Any(row => row.Entries.Contains("\"protected\"", StringComparison.Ordinal))
                : after is { Retired: true, HeldCascade: not null } && await reads.GetAsync(scope, child) is null &&
                    journal is [{ Revision: -1 } deleted] && deleted.Entries.Contains("\"deleted\"", StringComparison.Ordinal));
        var name = protectedAtEnd ? "protection at Complete keeps a new Faulted mark after clearing the Park and delivering a single notice" :
            "unprotected Complete retires the row with one deletion marker, one final notice and the descendant obligation";
        check(matched, name + (matched ? "" : $" (retired {after.Retired}, held cascade {after.HeldCascade?.Count}, " +
            $"notices {Notices()}, kept {Kept()}, journal {string.Join(", ", journal.Select(row => row.Revision))}, worker owns {successor.Owns})"));
        if (matched && protectedAtEnd)
        {
            var laterSelection = (await reads.GetAsync(scope, child))!.Selection!;
            var again = await Send(child, "later-delete", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(laterSelection)));
            await Step(successor, child);
            check(again.Outcome == Contract.AdmissionOutcome.Accepted && laterSelection != selected &&
                  Row(child) == after && Notices() == 1 && Kept() == 0,
                "the new Faulted selection can name the row and protection still refuses a later Delete");
        }

        int Kept() => store.Messages.Values.Count(row => row.Message.Target.Id == id && row.KeptBy is not null);
        int Notices() => commits.SelectMany(turn => turn.Sent).Count(message => message.Sender == id && message.Kind == Kinds.ChildFinished &&
            message.Read<FoundationChildNotice>() is { Final: true, Status: InstanceStatus.Faulted });

        async Task<Contract.ActorRef> Spawn(Contract.ActorRef actor)
        {
            await Send(actor, "spawn", "spawn");
            await Step(worker, actor);
            var spawned = new Contract.ActorRef(Row(actor).State);
            await Step(worker, spawned);
            return spawned;
        }

        InMemoryStore.ActorCheckpoint Checkpoint(Contract.ActorRef actor) => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!;
        InstanceRow Row(Contract.ActorRef actor) => Checkpoint(actor).Row;
        ValueTask<Contract.Admission> Send(Contract.ActorRef actor, string operation, string kind, string? payload = null) =>
            edge.SendAsync(scope, new Contract.Target.Ref(actor), operation, kind, payload);

        async Task Step(Worker owner, Contract.ActorRef actor)
        {
            await owner.PollAsync();
            await owner.LoadAsync();
            await owner.FlushAsync();
            owner.RunTurn(store.FoundationTarget(scope.Tenant, actor.Id)!.Id);
            await owner.FlushAsync();
        }
    }

    private static async Task AttachToDrain(ParkClearKind kind, Action<bool, string> check,
        bool parentTerminate = false, bool beforeFirstPage = false, bool protectsAtComplete = false,
        bool protectsAtOpen = false, bool thenDelete = false)
    {
        const long initial = 1_702_310_083_000;
        var now = initial;
        var scope = new Contract.BoundScope("delete-takeover");
        var store = new InMemoryStore(1, () => now)
        {
            ParkedRetentionWindow = kind == ParkClearKind.Expiry ? TimeSpan.FromMilliseconds(1) : null
        };
        var probe = new Probe { Protect = protectsAtOpen };
        var requests = new Requests("delete-takeover");
        var edge = new InMemoryIngress(store, requests, "delete-takeover", (_, _) => true);
        var reads = new ActorList(store, 5);
        var commits = new List<TurnCommit>();
        Action? admit = null;
        var scenario = thenDelete ? "ThenDelete, protected" : parentTerminate ? "parent Terminate" :
            protectsAtOpen ? "Terminate, protected at Open" : protectsAtComplete ? "Terminate, protected at Complete" : kind.ToString();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed) commits.AddRange(batch.Turns);
            else if (admit is { } submit && batch.Turns.Any(turn => beforeFirstPage
                ? turn.Row?.ParkDiscard is { Cursor: null }
                : turn.FoundationParkClear is not null))
            {
                admit = null;
                submit();
            }
        });
        var first = Owner("attach-first");
        await using var initialOwner = await Activate(first);
        var born = await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "parent")), "parent", Contract.Kinds.Start);
        var parent = born.Actor!.Value;
        await Step(first, parent);
        await Send(parent, "spawn", "spawn");
        await Step(first, parent);
        var child = new Contract.ActorRef(Row(parent).State);
        await Step(first, child);
        var id = Row(child).Id;
        await Send(child, "park", "park");
        await Step(first, child);
        await Step(first, parent); // Settle the Park notice before isolating the child after takeover.
        for (var index = 0; index <= 2 * InMemoryStore.ParkPageLimit; index++)
            await Send(child, $"kept-{index:D4}", "pending");
        await Step(first, child);
        var selection = (await reads.GetAsync(scope, child))!.Selection!;
        var payload = Json.Write(new Contract.OperatorCommand(selection));
        Request? request = null;
        Request? competing = null;
        Request? retention = null;
        if (beforeFirstPage) admit = AdmitCompetingDeletes;
        switch (kind)
        {
            case ParkClearKind.Discard:
                await Send(child, "original-discard", Contract.Kinds.Discard, payload);
                break;
            case ParkClearKind.Terminate when parentTerminate:
                await Send(parent, "cancel-parent", "cancel");
                await Step(first, parent);
                break;
            case ParkClearKind.Terminate:
                var command = new Contract.OperatorCommand(selection, ThenDelete: thenDelete, ExpectedTarget: thenDelete ? child : null);
                await Send(child, "original-terminate", Contract.Kinds.Terminate, Json.Write(command));
                break;
            case ParkClearKind.Expiry:
                now++;
                break;
        }
        await Step(first, child);
        var opened = Row(child).ParkDiscard;
        check(opened?.Kind == kind && opened.Deletion is null && opened.Cursor is null && opened.ThenDelete == thenDelete,
            $"{scenario}: the original drain opens with its own kind and first control");
        if (opened?.Kind != kind) return;

        if (!beforeFirstPage)
        {
            admit = AdmitCompetingDeletes;
            await Step(first, child);
        }
        var prefixed = Row(child);
        var retainedBefore = Kept();
        var firstEpoch = first.Fences.Single().Epoch;
        check(request is not null && prefixed.ParkDiscard?.Cleared == (beforeFirstPage ? 0 : InMemoryStore.ParkPageLimit) &&
              retainedBefore.Length > 0,
            $"{scenario}: Delete is queued before the next clear page");
        if (request is null) return;

        var attaching = beforeFirstPage ? first : Owner("attach-middle");
        if (!beforeFirstPage) await initialOwner.DisposeAsync();
        now++;
        await using var attachOwner = beforeFirstPage ? null : await Activate(attaching);
        if (beforeFirstPage && retention is not null)
        {
            await DriveUntil(attaching, retention);
            check(retention.Answer is CreationResult { Result: "NotEligible" } && Row(child) == prefixed,
                $"{scenario}: Retention Delete cannot take over the unresolved Park");
        }

        await DriveUntil(attaching, request);
        var attached = Row(child);
        if (protectsAtOpen)
        {
            var refused = thenDelete ? request.Answer is CreationResult { Result: "InProgress", OwningOperation: "original-terminate" }
                : request.Answer is CreationResult { Result: "Protected" };
            check(refused && attached == prefixed,
                $"{scenario}: Delete is refused by the owning operation or the protection check");
        }
        else
        {
            var expected = prefixed.ParkDiscard! with { Deletion = new("takeover-delete", selection) };
            var accepted = request.Answer is "started" && attached == (prefixed with
            {
                Revision = prefixed.Revision + 1, ParkDiscard = expected
            });
            check(accepted && retainedBefore.SequenceEqual(Kept()),
                $"{scenario}: takeover preserves the cursor, deletion selection and exact control");
            if (!accepted) return;
            var publicView = await reads.GetAsync(scope, child);
            var parkedList = await reads.ListAsync(scope, new(Parked: true));
            check(publicView?.Park?.OpenResolution == "deleting" &&
                  parkedList.Items.Any(actor => actor.Header.Ref == child && actor.Park?.OpenResolution == "deleting"),
                $"{scenario}: the public point and list reads report the attached Delete as the open resolution");
            probe.Protect = protectsAtComplete;
            await DriveUntil(attaching, competing!);
            check(competing!.Answer is CreationResult { Result: "InProgress", OwningOperation: "takeover-delete" } &&
                  Row(child) == attached && retainedBefore.SequenceEqual(Kept()),
                $"{scenario}: a competing Delete reports the owning operation before protection");
            if (!beforeFirstPage && retention is not null)
            {
                await DriveUntil(attaching, retention);
                check(retention.Answer is CreationResult { Result: "InProgress", OwningOperation: "takeover-delete" } &&
                      Row(child) == attached && retainedBefore.SequenceEqual(Kept()),
                    $"{scenario}: Retention Delete reports the attached operation and preserves the pending page");
            }
        }
        if (attachOwner is not null) await attachOwner.DisposeAsync();
        else await initialOwner.DisposeAsync();

        now++;
        var cold = Owner("attach-cold");
        await using var successor = await Activate(cold);
        for (var pass = 0; pass < 12 && store.Instances[id].ParkDiscard is not null; pass++) await Step(cold, child);
        await Step(cold, parent);
        var final = store.Instances[id];
        var status = kind == ParkClearKind.Terminate ? InstanceStatus.Cancelled : InstanceStatus.Faulted;
        var notices = commits.SelectMany(turn => turn.Sent).Where(message => message.Kind == Kinds.ChildFinished && message.Sender == id)
            .Select(message => message.Read<FoundationChildNotice>()).ToArray();
        var journals = store.Journal.Values.Where(row => row.Instance == id).ToArray();
        var operation = protectsAtOpen ? "original-terminate" : "takeover-delete";
        var finish = journals.SingleOrDefault(row => row.Entries.Contains($"\"{operation}\"", StringComparison.Ordinal));
        var preservedNotice = notices.Count(notice => notice.Final) == 1 && notices.Single(notice => notice.Final).Status == status &&
            Row(parent).Children is null;
        var pages = commits.Where(turn => turn.Instance == id && turn.FoundationParkClear is not null)
            .SelectMany(turn => turn.FoundationParkClear!.Rows.Select(row => row.Cursor)).ToArray();
        var currentMark = store.ReadFoundationActor(scope.Tenant, child.Id).Actor?.Terminal;
        var validOutcome = protectsAtOpen || protectsAtComplete
            ? final is { Retired: false, ParkDiscard: null } && final.Status == status && currentMark?.At == now &&
              (!thenDelete && !protectsAtComplete || finish?.Entries.Contains("protected", StringComparison.Ordinal) == true)
            : final.Retired && final.Status == status && journals.Length == 1 && finish?.Revision == -1;
        check(cold.Fences.Single().Epoch > firstEpoch && Kept().Length == 0 && preservedNotice && finish is not null &&
              finish.Entries.Contains(InMemoryStore.ParkClearOutcome(kind), StringComparison.Ordinal) && validOutcome &&
              pages.Length == 2 * InMemoryStore.ParkPageLimit + 2 && pages.Distinct().Count() == pages.Length &&
              pages.SequenceEqual(pages.Order()),
            $"{scenario}: the cold owner clears each key once and completes the original disposition with one notice");

        ParkPageCursor[] Kept() => store.Messages.Values.Where(row => row.Message.Target.Id == id && row.KeptBy is not null)
            .Select(row => new ParkPageCursor(row.Seq, row.Message.InboxKey)).Order().ToArray();
        void AdmitCompetingDeletes()
        {
            if (beforeFirstPage) AdmitRetention();
            request = Ask("takeover-delete");
            if (protectsAtOpen) return;
            competing = Ask("later-delete");
            if (!beforeFirstPage) AdmitRetention();
        }
        void AdmitRetention()
        {
            retention = requests.OpenBound("retention", now + 1_000, scope.Tenant, null);
            var accepted = store.AdmitRetentionDelete(scope.Tenant, child.Id, selection,
                replyTo: retention.ReplyAddress("delete-takeover"));
            check(accepted.Outcome == InMemoryStore.OperatorAdmission.Accepted,
                $"{scenario}: Retention Delete reaches the owner for the selected mark");
        }

        Worker Owner(string node) => new(node, 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - initial, Transport = new Replies(requests) });
        async Task<IScopeActivation> Activate(Worker worker) => (await new DurableHost([worker]).ActivateAsync(0))!;
        InstanceRow Row(Contract.ActorRef actor) => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row;
        ValueTask<Contract.Admission> Send(Contract.ActorRef actor, string op, string messageKind, string? body = null) =>
            edge.SendAsync(scope, new Contract.Target.Ref(actor), op, messageKind, body);
        Request Ask(string op)
        {
            var open = requests.OpenBound(op, now + 1_000, scope.Tenant, null);
            var admitted = store.AdmitFoundationOperator(scope.Tenant, child.Id, null, op, Contract.Kinds.Delete,
                payload, null, null, selection, open.ReplyAddress("delete-takeover"));
            check(admitted.Outcome == InMemoryStore.OperatorAdmission.Accepted, $"{scenario}: {op} is admitted for the current mark");
            return open;
        }
        async Task DriveUntil(Worker worker, Request pending)
        {
            for (var pass = 0; pass < 5 && pending.Answer is null; pass++) await Step(worker, child);
        }
        async Task Step(Worker worker, Contract.ActorRef actor)
        {
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(store.FoundationTarget(scope.Tenant, actor.Id)!.Id);
            await worker.FlushAsync();
        }
    }

    private static void CheckRetirementGuards(InMemoryStore store, CommitBatch batch, TurnCommit complete, Action<bool, string> check)
    {
        var before = store.Instances[complete.Instance];
        var control = before.ParkDiscard!.Control;
        var position = store.Partitions[batch.Partition].Position;
        var obligation = complete.Row!.HeldCascade!;
        var oldChild = obligation.Children[0];
        var wrongChild = obligation with { Children = obligation.Children.SetItem(0, oldChild with { Creation = oldChild.Creation + 1 }) };
        var changes = new (TurnCommit Turn, string Missing)[]
        {
            (complete with { Consumed = [], Done = [] }, "control"),
            (complete with { Sent = [] }, "notice"),
            (complete with { Row = complete.Row! with { HeldCascade = null, Parent = null, ParentCreation = 0 } }, "obligation"),
            (complete with { Row = complete.Row! with { HeldCascade = wrongChild } }, "exact child creation")
        };
        foreach (var change in changes)
        {
            var turns = batch.Turns.Select(turn => ReferenceEquals(turn, complete) ? change.Turn : turn).ToArray();
            var refused = false;
            try { _ = store.CommitAsync(batch with { Turns = turns }).GetAwaiter().GetResult(); }
            catch (StoreException error) when (error.Failure == Failure.Definite) { refused = true; }
            check(refused && store.Instances[complete.Instance] == before && store.Messages.ContainsKey(control) &&
                  store.Partitions[batch.Partition].Position == position,
                $"the store refuses a retirement without its {change.Missing} before writing anything");
        }
    }

    private sealed class Replies(Requests requests) : IReplyTransport
    {
        public void Send(string node, Reply reply) => requests.Receive(reply);
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "park-delete";
        public bool Protect { get; set; }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == "spawn") turn.State = turn.Start(new(Type, "child")).Id;
                if (message.Kind == "park") throw new InvalidOperationException("Park the child.");
                if (message.Kind == "cancel") turn.End(Contract.ActorStatus.Cancelled);
            }
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => Protect;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
    }
}
