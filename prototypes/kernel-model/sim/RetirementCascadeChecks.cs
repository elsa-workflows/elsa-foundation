using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class RetirementCascadeChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await CheckTree(check, total: 1);
        await CheckTree(check, total: 1, refuseOpen: true);
        await CheckTree(check, total: 1, timedJob: true);
        await CheckTree(check, total: 1, timedJob: true, legacyDeadline: true);
        await CheckTree(check, total: 1, timedJob: true, legacyDeadline: true, retakeBeforeDelete: true);
        await CheckTree(check, total: CascadeProtocol.PageLimit + 1);
        await CheckRetirementShape(check);
    }

    private static async Task CheckTree(Action<bool, string> check, int total, bool refuseOpen = false, bool timedJob = false,
        bool legacyDeadline = false, bool retakeBeforeDelete = false)
    {
        const long now = 1_702_310_000_000;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("retirement-cascade");
        var probe = new Probe();
        var ingress = new InMemoryIngress(store, new Requests("retirement-cascade"), "retirement-cascade",
            (_, kind) => kind is Contract.Kinds.Delete or Contract.Kinds.Terminate);
        var reads = new ActorList(store, 10);
        var cascades = new List<Message>();
        var refused = 0;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed) cascades.AddRange(batch.Turns.SelectMany(turn => turn.Sent).Where(message => message.CascadeOrigin));
            else if (refuseOpen && refused < 2 && batch.Turns.Any(turn =>
                turn.Row is { Retired: true } && turn.FoundationCascade is { Input: null }))
            {
                refused++;
                throw new StoreException(Failure.Definite, "test refusal opening the held cascade");
            }
        });
        Worker NewWorker() => new("retirement-cascade", 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => 0 });
        var worker = NewWorker();
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "parent")), "parent", Contract.Kinds.Start);
        var parent = started.Actor!.Value;
        await Step(parent);
        var child = await Spawn(parent, "spawn-child");
        var descendant = await Spawn(child, "spawn-descendant");
        var last = await Spawn(descendant, "spawn-last");
        if (total > 1)
        {
            await Send(child, "extra", "spawn-many", (total - 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            await Step(child);
        }
        var obligation = ChildRegistry.Read(Row(child).Children);
        (InboxKey Ticket, InboxKey Deadline, CancellationToken Driver)? timed = null;
        if (timedJob)
        {
            await Send(child, "arm-job", "arm-job");
            await Step(child);
            var running = Row(child);
            var job = JobRegistry.Read(running.Jobs).Single();
            var ticket = InboxKey.Ticket(running.Id, job.Key, running.Creation, job.Generation);
            var deadline = InboxKey.Issued(DurableTurn.FoundationJobDeadlineId(running.Id, running.Creation, job.Generation));
            if (legacyDeadline)
            {
                var original = store.Messages[deadline];
                var stored = store.Messages[ticket];
                store.Messages.Remove(deadline);
                deadline = InboxKey.Issued($"{CreationProtocol.Origin(running.Id, running.Creation)}#{running.Revision}.0000");
                store.Messages[deadline] = new((original.Message with { Id = deadline.Id }).AsKernelTicket(), original.Seq, original.Seen);
                var timing = stored.Message.Read<JobTicket>() with { Timer = deadline.Id };
                store.Messages[ticket] = new((stored.Message with { Payload = Json.Write(timing) }).AsKernelTicket(), stored.Seq, stored.Seen);
            }
            await owner.WokenAsync(null);
            await owner.RunAsync();
            timed = (ticket, deadline, await probe.JobEntered.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        }

        await Send(child, "end-child", "finish");
        await Step(child);
        var finished = Row(child);
        await Step(parent); // Hold the final notice while the parent's child registration is still live.
        check(finished.Status == InstanceStatus.Finished && Row(parent).Children is not null &&
            Row(descendant).Status == InstanceStatus.Running, "an ended child still has a live subtree and an outstanding parent registration");

        if (retakeBeforeDelete)
        {
            await owner.DisposeAsync();
            worker = NewWorker();
        }
        await using var beforeDeleteOwner = retakeBeforeDelete
            ? (await new DurableHost([worker]).ActivateAsync(0))! : null;
        if (retakeBeforeDelete && timed is { } beforeDelete)
        {
            for (var pass = 0; pass < 4; pass++) await Step(child);
            check(Row(child) is { Retired: false, Status: InstanceStatus.Finished } &&
                !store.Messages.ContainsKey(beforeDelete.Ticket) && !store.Messages.ContainsKey(beforeDelete.Deadline),
                "a fresh owner settles the linked legacy deadline when it consumes the ticket before Delete");
        }

        var selection = (await reads.GetAsync(scope, child))!.Selection!;
        var operation = Json.Write(new Contract.OperatorCommand(selection));
        var delete = await Send(child, "delete-child", Contract.Kinds.Delete, operation);
        var terminate = await Send(child, "terminate-child", Contract.Kinds.Terminate, operation);
        await Step(child);
        var marker = store.Instances[finished.Id];
        check(delete.Outcome == Contract.AdmissionOutcome.Accepted && terminate.Outcome == Contract.AdmissionOutcome.Accepted &&
            marker is { Retired: true, HeldCascade: { } } && marker.Parent == Row(parent).Id &&
            marker.ParentCreation == Row(parent).Creation &&
            obligation.Count == total && marker.HeldCascade.Children.SequenceEqual(obligation) &&
            cascades.Count == 0, "qualified Delete retains the exact parent and the child's held obligation without terminating the subtree");
        await Step(child);
        check(store.Instances[finished.Id] == marker && Row(descendant).Status == InstanceStatus.Running && cascades.Count == 0,
            "an ordinary operator terminate at the marker leaves its obligation held");
        if (timed is { } retiredJob)
        {
            for (var pass = 0; pass < 4; pass++) await Step(child);
            check(retiredJob.Driver.IsCancellationRequested && marker.HeldCascade?.Jobs.Length == 1 &&
                !store.Messages.ContainsKey(retiredJob.Ticket) && store.Messages.ContainsKey(retiredJob.Deadline) != legacyDeadline,
                legacyDeadline ? "consuming a legacy retired job ticket also settles its linked ordinal deadline" :
                    "job cleanup can throw during Delete and the Worker still consumes the obsolete job ticket");
        }

        await Send(parent, "end-parent", "cancel");
        await Step(parent);
        check(cascades.Count == 1 && cascades[0].Target.Id == finished.Id && cascades[0].Sender == Row(parent).Id,
            "the parent's real ending commit stores an exact terminate for its retired child");
        if (refuseOpen)
        {
            await Step(child);
            check(refused == 2 && !worker.Owns && store.Instances[finished.Id] == marker && cascades.Count == 1,
                "a refused marker page resets the owner and preserves the held obligation for retry");
        }
        if (total > 1)
        {
            await Step(child);
            check(store.Instances[finished.Id] is { Retired: true, HeldCascade: null, Cascade.Cursor: CascadeProtocol.PageLimit } &&
                cascades.Count(message => message.Sender == finished.Id) == CascadeProtocol.PageLimit,
                "the first parent-bound page durably moves the held obligation to discharging");
        }
        if (beforeDeleteOwner is not null) await beforeDeleteOwner.DisposeAsync();
        else await owner.DisposeAsync();
        worker = NewWorker();
        await using var successor = (await new DurableHost([worker]).ActivateAsync(0))!;
        // Takeover recovers a suspect at a time with the default trial group.
        for (var pass = 0; pass < 4 * total + 32 && !Completed(); pass++)
        {
            await successor.WokenAsync(null);
            await successor.RunAsync();
        }
        var expectedPages = (total + CascadeProtocol.PageLimit - 1) / CascadeProtocol.PageLimit;
        var sent = cascades.Where(message => message.Sender == finished.Id).Select(message => new CreationAddress(message.Target.Id, message.TargetCreation ?? 0));
        check(store.Instances[finished.Id] == marker with { HeldCascade = null, Revision = marker.Revision + expectedPages } &&
            Completed() &&
            sent.SequenceEqual(obligation),
            "takeover opens the held cascade once and clears it while every live descendant cancels");
        if (timed is { } completedJob)
            check(!store.Messages.ContainsKey(completedJob.Deadline),
                legacyDeadline ? "the legacy cleanup remains settled after the next owner clears the held obligation" :
                    "the recorded parent's cascade removes the exact generation deadline after the job ticket is gone");

        bool Completed() => store.Instances[finished.Id].Cascade is null && store.Instances[finished.Id].HeldCascade is null &&
            obligation.All(address => store.Instances.GetValueOrDefault(address.Id)?.Status == InstanceStatus.Cancelled) &&
            Row(last).Status == InstanceStatus.Cancelled;

        async Task<Contract.ActorRef> Spawn(Contract.ActorRef from, string id)
        {
            await Send(from, id, "spawn");
            await Step(from);
            var next = new Contract.ActorRef(Row(from).State);
            await Step(next);
            return next;
        }

        ValueTask<Contract.Admission> Send(Contract.ActorRef to, string id, string kind, string? payload = null) =>
            ingress.SendAsync(scope, new Contract.Target.Ref(to), id, kind, payload);

        InstanceRow Row(Contract.ActorRef actor) => store.Instances[store.FoundationTarget(scope.Tenant, actor.Id)!.Id];

        async Task Step(Contract.ActorRef actor)
        {
            var address = store.FoundationTarget(scope.Tenant, actor.Id)!.Id;
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(address);
            await worker.FlushAsync();
        }
    }

    private static async Task CheckRetirementShape(Action<bool, string> check)
    {
        var store = new InMemoryStore(1, () => 100);
        var lease = (await store.TakeAsync(0, "retirement-shape", Defaults.Lease))!;
        var jobs = new List<JobRegistration> { new("J", 2), new("K", 3) };
        var children = new List<CreationAddress> { new("g0/child/0.1.1", 1) };
        var child = new InstanceRow("0/child", "probe", "child", InstanceStatus.Finished, "null", 4, "0/parent",
            Jobs: JobRegistry.Write(jobs), Children: ChildRegistry.Write(children), Creation: 1, StartIntent: "child", ParentCreation: 1);
        var root = child with { Id = "0/root", Parent = null, ParentCreation = 0 };
        store.Instances[child.Id] = child;
        store.Instances[root.Id] = root;

        var entries = CascadeRegistry.From(child);
        var marker = CreationProtocol.Retire(child, entries);
        var invalid = new[]
        {
            (marker with { HeldCascade = entries with { Jobs = [jobs[0], jobs[0]] } }, "a repeated job pair"),
            (marker with { HeldCascade = entries with { Jobs = [jobs[0], new JobRegistration("foreign", 3)] } }, "an off-registry job pair"),
            (marker with { HeldCascade = entries with { Jobs = [new JobRegistration("J", 1), jobs[1]] } }, "an older job generation"),
            (marker with { HeldCascade = entries with { Children = [children[0], children[0]] } }, "a repeated child creation"),
            (marker with { ParentCreation = 2 }, "an incorrect recorded parent creation"),
            (CreationProtocol.Retire(child), "an omitted obligation for the child")
        };
        foreach (var (next, reason) in invalid)
        {
            var refused = false;
            try { await Commit(child, next, lease.Position); }
            catch (StoreException error) when (error.Failure == Failure.Definite) { refused = true; }
            check(refused && store.Instances[child.Id] == child && store.Partitions[0].Position == lease.Position,
                $"the fenced retirement refuses {reason} without a checkpoint or journal write");
        }

        check((await Commit(child, marker, lease.Position)).Landed && store.Instances[child.Id] == marker,
            "the fenced retirement preserves all current job pairs and the exact attached child");
        var rootMarker = CreationProtocol.Retire(root, entries);
        check((await Commit(root, rootMarker, lease.Position + 1)).Landed &&
            store.Instances[root.Id] is { Retired: true, Parent: null, HasCascadeObligation: false },
            "a root retirement keeps its high-water mark and no held child or job obligation");

        ValueTask<Committed> Commit(InstanceRow before, InstanceRow after, long position) =>
            store.CommitAsync(new(0, lease.Epoch, position, Defaults.Lease, false,
                [new(before.Id, after, before.Revision, [], [], [], [], [], "[]", Delete: true)], [], []));
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "retirement-cascade";
        public TaskCompletionSource<CancellationToken> JobEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case "arm-job": turn.StartJob("timed", timeout: TimeSpan.FromMinutes(1)); break;
                    case "spawn": turn.State = turn.Start(new(Type, "child")).Id; break;
                    case "spawn-many":
                        for (var child = 0; child < int.Parse(message.Payload!, System.Globalization.CultureInfo.InvariantCulture); child++)
                            turn.Start(new(Type, "child"));
                        break;
                    case "finish": turn.End(Contract.ActorStatus.Finished); break;
                    case "cancel": turn.End(Contract.ActorStatus.Cancelled); break;
                    case Contract.Kinds.ChildFinished when turn.Header.TypeData == "parent": turn.Hold(message); break;
                    case Contract.Kinds.Terminate: turn.End(Contract.ActorStatus.Cancelled); break;
                }
            return default;
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            using var cleanup = cancellationToken.Register(static () => throw new InvalidOperationException("Test job cleanup failed."));
            JobEntered.TrySetResult(cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
    }
}
