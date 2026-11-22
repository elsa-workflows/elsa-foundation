using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ActorJournalChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var now = 1_700_300_000_000L;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        var foreign = new Contract.BoundScope("other");
        var reader = new ActorList(store, 1);
        var target = InstanceRef.In(0, "journal-root");
        var neighbor = InstanceRef.In(0, "journal-neighbor");
        var first = Admit(target, 1, "first");
        var neighborRef = Admit(neighbor, 1, "neighbor");
        var owner = await Owner("tail-owner");
        check((await reader.TailAsync(scope, first)).Items.Count == 0,
            "a journal page omits an admitted start until its turn commits");
        await Pump(owner);

        var head = await reader.TailAsync(scope, first);
        check(head is { Items: [{ Revision: 1, Entry: "\"first-a\"" }], Next: not null } && head.AsOf == now,
            "the journal starts with the first committed entry and its own AsOf");
        await owner.DisposeAsync();
        await using var replacement = await Owner("tail-successor");

        now++;
        var next = await reader.TailAsync(scope, first, head.Next);
        var caughtUp = await reader.TailAsync(scope, first, next.Next);
        check(next is { Items: [{ Revision: 1, Entry: "\"first-b\"" }], Next: not null } && next.AsOf == now &&
            caughtUp is { Items.Count: 0, Next: null },
            "journal pages continue within a commit across owner replacement and bookmark the tail");
        check((await reader.TailAsync(foreign, first)).Items.Count == 0 &&
            await Refused(() => reader.TailAsync(foreign, first, head.Next)) &&
            await Refused(() => reader.TailAsync(scope, neighborRef, head.Next)) &&
            await Refused(() => reader.ListAsync(scope, new(), head.Next)),
            "journal bookmarks cannot cross tenant, actor or read kind");

        check(store.AddFoundationMessage(scope.Tenant, first.Id, "finish", "finish") == true,
            "the exact journal fixture admitted its final input");
        await Pump(replacement);
        var later = await reader.TailAsync(scope, first, next.Next);
        check(later is { Items: [{ Revision: 2, Entry: "\"finish-a\"" }], Next: not null },
            "a saved tail bookmark returns only entries committed later");

        check(store.AddFoundationMessage(scope.Tenant, first.Id, "retire", Kinds.Delete) == true,
            "the ended fixture admitted its retirement");
        await Pump(replacement);
        var second = Admit(target, 2, "second");
        await Pump(replacement);
        check(first != second && (await reader.TailAsync(scope, first, next.Next)).Items.Count == 0 &&
            (await reader.TailAsync(scope, second)) is { Items: [{ Revision: 1, Entry: "\"second-a\"" }], Next: not null },
            "retirement and reuse keep the old journal and delete receipt out of the new life");

        Contract.ActorRef Admit(InstanceRef address, long generation, string id)
        {
            var spec = new StartSpec("journal", id, Creation: new(generation, generation - 1, id));
            var reference = store.AdmitFoundationStart(scope.Tenant,
                new Message(id, address, Kinds.Start, Start: spec, TargetCreation: generation));
            return reference is { } match ? new(match) : throw new InvalidOperationException("The test creator was refused.");
        }

        async Task<IScopeActivation> Owner(string node)
        {
            var worker = new Worker(node, 0, store, new WorkflowInstance([new Probe()]), new WorkerOptions { Monotonic = () => 0 });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }
    }

    private static async Task<bool> Refused<T>(Func<ValueTask<Contract.Page<T>>> read)
    {
        try { await read(); return false; }
        catch (ArgumentException exception) when (exception.ParamName == "from") { return true; }
    }

    private static async Task Pump(IScopeActivation activation)
    {
        for (var i = 0; i < 20; i++)
        {
            await activation.WokenAsync(null);
            await activation.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class Probe : Model<List<int>>
    {
        public override string Key => "journal";

        protected override void Turn(ITurn turn, List<int> state)
        {
            var label = turn.Inbox[0].Kind == "finish" ? "finish" : turn.Instance.Definition;
            turn.Record(label + "-a");
            turn.Record(label + "-b");
            if (label == "finish") turn.SetStatus(InstanceStatus.Finished);
        }
    }
}
