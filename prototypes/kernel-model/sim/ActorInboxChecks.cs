using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ActorInboxChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long start = 1_700_400_000_000;
        const long due = start + 100;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        Contract.IActorReads reader = new ActorList(store, 1);
        var scope = new Contract.BoundScope("acme");
        var other = new Contract.BoundScope("other");
        var address = InstanceRef.In(0, "actor-inbox");
        var initial = Admit(1);
        var owner = await Owner("inbox-first");
        await Pump(owner);
        check(store.AddFoundationMessage(scope.Tenant, initial.Id, "work", "hold") == true &&
            store.AddFoundationMessage(scope.Tenant, initial.Id, "later", "next", dueAt: due) == true,
            "the exact inbox fixture inserts its hold and future timer");

        var pending = await reader.InboxAsync(scope, initial);
        await Pump(owner);
        now++;
        var first = await reader.InboxAsync(scope, initial);
        check(first is { Items: [{ Id: "work", State: Contract.DeliveryState.Held, Sender: null }], Next: not null } &&
            pending.Items is [{ State: Contract.DeliveryState.Pending }] && pending.Items[0].Handle == first.Items[0].Handle &&
            first.Items[0].Handle.Length == 65 && first.Items[0].Size > 0 && first.AsOf == now,
            "the inbox exposes pending and fenced held states with one opaque handle and its AsOf reading");
        await owner.DisposeAsync();
        await using var successor = await Owner("inbox-successor");
        var second = await reader.InboxAsync(scope, initial, first.Next);
        check(second is { Items: [{ Id: "later", Due: due, State: Contract.DeliveryState.Pending }], Next: null } &&
            second.Items[0].Handle != first.Items[0].Handle &&
            (await reader.InboxAsync(other, initial)).Items.Count == 0,
            "an inbox continuation reaches the next committed row across owner change without crossing tenants");

        var foreignCursor = false;
        try { await reader.InboxAsync(other, initial, first.Next); }
        catch (ArgumentException error) when (error.ParamName == "from") { foreignCursor = true; }
        check(foreignCursor, "an inbox continuation cannot be replayed in another tenant");

        check(store.AddFoundationMessage(scope.Tenant, initial.Id, "finish", "finish") == true, "the held work can be released by an outside input");
        await Pump(successor);
        check((await reader.InboxAsync(scope, initial)) is { Items: [{ Id: "later" }] },
            "consumed deduplication markers are absent from the operational inbox");

        check(store.AddFoundationMessage(scope.Tenant, initial.Id, "delete", Kinds.Delete) == true, "the ended inbox fixture admitted its retirement");
        await Pump(successor);
        var replacement = Admit(2);
        await Pump(successor);
        check(store.Messages.Values.Any(row => row.Message.Target.Id == address.Id && row.Message.TargetCreation == 1 && row.Message.Id == "later") &&
            (await reader.InboxAsync(scope, initial)).Items.Count == 0 &&
            (await reader.InboxAsync(scope, replacement)).Items.Count == 0,
            "a pending earlier-life timer remains invisible in either the retired actor or its successor's inbox");

        Contract.ActorRef Admit(long generation)
        {
            var id = generation.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var spec = new StartSpec("inbox", "probe", Creation: new(generation, generation - 1, id));
            var reference = store.AdmitFoundationStart(scope.Tenant,
                new Message(id, address, Kinds.Start, Start: spec, TargetCreation: generation));
            return reference is { } found ? new(found) : throw new InvalidOperationException("The fixture start was refused.");
        }

        async Task<IScopeActivation> Owner(string node)
        {
            var worker = new Worker(node, 0, store, new WorkflowInstance([new Probe()]), new WorkerOptions { Monotonic = () => now - start });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }
    }

    private static async Task Pump(IScopeActivation activation)
    {
        for (var step = 0; step < 20; step++)
        {
            await activation.WokenAsync(null);
            await activation.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class Probe : Model<List<int>>
    {
        public override string Key => "inbox";

        protected override void Turn(ITurn turn, List<int> state)
        {
            if (turn.Inbox.Any(message => message.Kind == "finish")) turn.SetStatus(InstanceStatus.Finished);
            else
                foreach (var message in turn.Inbox.Where(message => message.Kind == "hold")) turn.Hold(message);
        }
    }
}
