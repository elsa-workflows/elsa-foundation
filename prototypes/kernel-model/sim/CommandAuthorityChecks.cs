namespace Kernel.Sim;

/// <summary>FC-C3 commits on the built-in store with read-back of every relevant table.</summary>
internal static class CommandAuthorityChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var store = new InMemoryStore(2, () => 100);
        var parent = InstanceRef.In(0, "parent");
        var child = InstanceRef.In(1, "child");
        var foreign = InstanceRef.In(1, "foreign");
        var leaseFor = TimeSpan.FromSeconds(20);
        var lease = (await store.TakeAsync(0, "parent-owner", leaseFor))!;
        var row = new InstanceRow(parent.Id, "probe", "definition", InstanceStatus.Running, "{}", 1, null,
            Children: ChildRegistry.Write([new(child.Id, 7)]));
        var birth = new TurnCommit(parent.Id, row, null, [], [], [], [], [], "[]");
        check((await store.CommitAsync(new CommitBatch(0, lease.Epoch, 0, leaseFor, false, [birth], [], []))).Landed,
            "the owner's commit records an exact child in the parent's registry");

        var inputs = new[] { ("suspend", Kinds.Suspend, child, 7L), ("foreign", Kinds.Terminate, foreign, 7L),
            ("stale", Kinds.Terminate, child, 6L), ("private", "kernel.cascade", child, 7L),
            ("migrate", "kernel.migrate", child, 7L) };

        foreach (var (id, kind, target, creation) in inputs)
        {
            var source = new Message(id, parent, "trigger");
            await store.AddAsync(source);
            var next = row with { Revision = 2, State = "{\"changed\":true}" };
            var authored = new Message("authored-" + id, target, kind, Sender: parent.Id, TargetCreation: creation) { ActorAuthored = true };
            var batch = new CommitBatch(0, lease.Epoch, 1, leaseFor, true,
                [new TurnCommit(parent.Id, next, 1, [source.InboxKey], [], [authored], [new("key", parent.Id, id)], [], "[2]")], [], []);
            var refused = false;
            try { await store.CommitAsync(batch); }
            catch (StoreException exception) when (exception.Failure == Failure.Definite) { refused = true; }
            check(refused && store.Instances[parent.Id] == row && store.Messages.ContainsKey(source.InboxKey) &&
                !store.Messages.ContainsKey(authored.InboxKey) && store.Journal.Count == 1 && store.Subscriptions.Count == 0 &&
                store.Partitions[0].Position == 1,
                $"the commit refuses actor-authored {id} and rolls back every effect");
        }

        var terminate = new Message("child-terminate", child, Kinds.Terminate, Sender: parent.Id, TargetCreation: 7) { ActorAuthored = true };
        var valid = new TurnCommit(parent.Id, row with { Revision = 2 }, 1, [], [], [terminate], [], [], "[]");
        check((await store.CommitAsync(new CommitBatch(0, lease.Epoch, 1, leaseFor, false, [valid], [], []))).Landed &&
            store.Messages.GetValueOrDefault(terminate.InboxKey)?.Message.TargetCreation == 7,
            "the parent can send Terminate to the exact registered child without looking up the child row");
    }
}
