using Kernel.Foundation.InMemory;
using Kernel.Native;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Contract = Kernel.Foundation.Contracts;
using WorkflowActorType = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

internal static class WorkflowRunHistoryChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long stamp = 1_792_000_000_000;
        var now = stamp;
        var blue = new Contract.BoundScope("history-blue");
        var red = new Contract.BoundScope("history-red");
        var store = new InMemoryStore(1, () => now);
        var ingress = new InMemoryIngress(store, new Requests("run-history"), "run-history", (_, kind) => kind == Contract.Kinds.Delete);
        Contract.IActorReads actors = new ActorList(store, 2);
        var runtime = new WorkflowRuntime(ingress, actors);
        var history = new WorkflowRunList(store, WorkflowActorType.ActorType, 2);
        var query = new WorkflowRunQuery(DirectApprovalWorkflow.Definition, Contract.ActorStatus.Running);
        var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build());
        var worker = Owner("history-owner");
        await using var active = await Activate(worker);
        var older = await Start(blue, "older");
        check((await history.ListAsync(blue, query)).Items.Count == 0, "admission alone does not project a workflow run");
        now++;
        await Pump(active);
        check((await history.ListAsync(blue, query)).Items is [{ Ref: var initial, UpdatedAt: var created }] &&
            initial == older && created == At(stamp + 1), "the first projected time is the first committed checkpoint's clock");

        now = stamp + 10;
        var tied = new[] { await Start(blue, "tied-a"), await Start(blue, "tied-b"), await Start(blue, "tied-c") };
        var foreign = await Start(red, "foreign");
        await Pump(active);

        var ordered = tied.OrderBy(actor => actor.Id, StringComparer.Ordinal).Append(older).ToArray();
        var first = await history.ListAsync(blue, query);
        var secondReader = new WorkflowRunList(store, WorkflowActorType.ActorType, 2);
        var second = await secondReader.ListAsync(blue, query, first.Next);
        check(first is { Items.Count: 2, Next: not null } && second is { Items.Count: 2, Next: null } &&
            first.Items.Concat(second.Items).Select(item => item.Ref).SequenceEqual(ordered) &&
            first.AsOf == now && second.AsOf == now,
            "keyset pages order committed times newest first and visit tied references once across pages");
        check((await history.ListAsync(red, query)).Items is [{ Ref: var isolated }] && isolated == foreign &&
            (await history.ListAsync(blue, query with { Definition = "other" })).Items.Count == 0 &&
            (await history.ListAsync(blue, query with { Status = Contract.ActorStatus.Finished })).Items.Count == 0,
            "tenant, definition and status constrain the workflow run list");

        var atTie = At(stamp + 10);
        var exact = query with { From = atTie, To = atTie };
        var filtered = await history.ListAsync(blue, exact);
        var equivalent = exact with { From = atTie.ToOffset(TimeSpan.FromHours(2)), To = atTie.ToOffset(TimeSpan.FromHours(-3)) };
        var last = await history.ListAsync(blue, equivalent, filtered.Next);
        check(filtered is { Items.Count: 2, Next: not null } && last is { Items.Count: 1, Next: null } &&
            filtered.Items.Concat(last.Items).Select(item => item.Ref).SequenceEqual(ordered.Take(3)),
            "both date bounds are inclusive and equivalent UTC offsets continue the same filtered list");
        check((await history.ListAsync(blue, query with { From = atTie.AddTicks(1) })).Items.Count == 0 &&
            (await history.ListAsync(blue, query with { To = atTie.AddTicks(-1) })).Items is [{ Ref: var below }] && below == older &&
            (await history.ListAsync(blue, query with { From = atTie.AddTicks(-1), To = atTie.AddTicks(1) })).Items.Count == 2,
            "100 ns date bounds retain their precision around millisecond checkpoint times");

        var generic = await runtime.ListAsync(blue, new Contract.ActorQuery());
        var genericLast = await runtime.ListAsync(blue, new Contract.ActorQuery(), generic.Next);
        check(generic.Items.Concat(genericLast.Items).Select(item => item.Header.Ref)
            .SequenceEqual(ordered.OrderBy(actor => actor.Id, StringComparer.Ordinal)),
            "the generic workflow actor list keeps exact-reference paging");
        var token = first.Next!.Value.Value;
        var tampered = new Contract.PageToken((token[0] == 'A' ? 'B' : 'A') + token[1..]);
        check(await RejectToken(red, query, first.Next) && await RejectToken(blue, exact, first.Next) &&
            await RejectToken(blue, query with { Definition = null }, first.Next) &&
            await RejectToken(blue, query with { Status = null }, first.Next) &&
            await RejectToken(blue, query, generic.Next) && await RejectToken(blue, query, tampered),
            "continuations bind tenant, date, definition, status and the workflow read route");
        try
        {
            await history.ListAsync(blue, query with { From = atTie.AddTicks(1), To = atTie });
            check(false, "a reversed date range is refused");
        }
        catch (ArgumentException error) when (error.ParamName == "query")
        {
            check(true, "a reversed date range is refused");
        }

        var original = store.ReadFoundationActor(blue.Tenant, older.Id).Actor!.Row;
        var frame = Json.Read<NativeState>(original.State).Frames.Single().Key;
        var tail = await actors.TailAsync(blue, older);
        var position = store.Partitions[0].Position;
        now = stamp + 15;
        var submitted = await Approve("one", "Ada");
        await worker.PollAsync();
        await worker.LoadAsync();
        await worker.FlushAsync();
        worker.RunTurn(original.Id);
        check(submitted.Outcome == Contract.AdmissionOutcome.Accepted && (await Collect(activeQuery: query))
            .Single(item => item.Ref == older).UpdatedAt == At(stamp + 1),
            "a staged workflow turn leaves history at the previous committed time");
        now = stamp + 20;
        await worker.FlushAsync();
        var saved = store.ReadFoundationActor(blue.Tenant, older.Id).Actor!.Row;
        var after = await history.ListAsync(blue, query);
        check(after.Items[0] is { Ref: var updated, UpdatedAt: var time } && updated == older && time == At(now) &&
            saved.Revision == original.Revision + 1 && Json.Read<NativeState>(saved.State).Frames[frame].State!.AsArray().Count == 1 &&
            store.Partitions[0].Position == position && (await actors.TailAsync(blue, older, tail.Next)).Items.Count == 0,
            "a journal-silent Minimal update reorders the list using commit time and retains its real state change");

        now = stamp + 25;
        var fence = worker.Fences.Single();
        var staleTurn = new TurnCommit(saved.Id, saved with { Revision = saved.Revision + 1 }, saved.Revision, [], [], [], [], [], null);
        var refused = await store.CommitAsync(new(0, fence.Epoch + 1, fence.Position, Defaults.Lease, false, [staleTurn], [], []));
        var duplicate = await Approve("one", "different");
        await Pump(active);
        check(!refused.Landed && duplicate.Outcome == Contract.AdmissionOutcome.Duplicate &&
            (await history.ListAsync(blue, query)).Items[0].UpdatedAt == At(stamp + 20),
            "a fenced refusal and a duplicate delivery preserve the committed chronology");

        await active.DisposeAsync();
        worker = Owner("history-successor");
        await using var successor = await Activate(worker);
        now = stamp + 30;
        check((await Approve("two", "Grace")).Outcome == Contract.AdmissionOutcome.Accepted, "a replacement owner accepts the second approval");
        await Pump(successor);
        var finish = await history.ListAsync(blue, query with { Status = Contract.ActorStatus.Finished });
        var selected = await actors.GetAsync(blue, older);
        check(finish.Items is [{ Ref: var ended, UpdatedAt: var completed }] && ended == older && completed == At(now) &&
            selected?.Header.EndedAt == now && (await Collect(activeQuery: query)).All(item => item.Ref != older),
            "the replacement owner's terminal commit updates status and chronology");
        var deletion = await ingress.SendAsync(blue, new Contract.Target.Ref(older), "delete", Contract.Kinds.Delete,
            Json.Write(new Contract.OperatorCommand(selected?.Selection)));
        await Pump(successor);
        check(deletion.Outcome == Contract.AdmissionOutcome.Accepted &&
            (await history.ListAsync(blue, query with { Status = Contract.ActorStatus.Finished })).Items.Count == 0 &&
            (await history.ListAsync(red, query)).Items.Single().Ref == foreign,
            "fenced retirement removes the projected workflow without changing another tenant's history");

        async Task<Contract.ActorRef> Start(Contract.BoundScope scope, string id) =>
            (await runtime.StartAsync(scope, id, NativeExecutionModel.Start(DirectApprovalWorkflow.Definition) with
                { History = Contract.History.Minimal })).Actor ?? throw new InvalidOperationException("Expected an admitted workflow.");

        ValueTask<Contract.Admission> Approve(string operation, string approver) => ingress.SendAsync(blue, new Contract.Target.Ref(older),
            operation, CompositeSignal.Kind, Json.Write(new CompositeSignal(frame, "approve", approver)));

        async Task<List<WorkflowRunView>> Collect(WorkflowRunQuery activeQuery)
        {
            var items = new List<WorkflowRunView>();
            Contract.PageToken? from = null;
            for (var step = 0; step < 3; step++)
            {
                var page = await history.ListAsync(blue, activeQuery, from);
                items.AddRange(page.Items);
                if (page.Next is not { } next) return items;
                from = next;
            }
            throw new InvalidOperationException("The workflow run history did not reach the end of the fixture.");
        }

        async Task<bool> RejectToken(Contract.BoundScope scope, WorkflowRunQuery filter, Contract.PageToken? token)
        {
            try { await history.ListAsync(scope, filter, token); return false; }
            catch (ArgumentException error) when (error.ParamName == "from") { return true; }
        }

        Worker Owner(string name) => new(name, 0, store, new ActorAdapter(store, new WorkflowActorType([new NativeExecutionModel(registry)])),
            new WorkerOptions { Monotonic = () => 0 });

        static DateTimeOffset At(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);
        static async Task<IScopeActivation> Activate(Worker owner) => (await new DurableHost([owner]).ActivateAsync(0))!;
        static async Task Pump(IScopeActivation owner)
        {
            for (var i = 0; i < 10; i++)
            {
                await owner.WokenAsync(null);
                await owner.RunAsync();
            }
        }
    }
}
