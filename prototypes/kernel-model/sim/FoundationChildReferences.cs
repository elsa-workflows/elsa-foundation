namespace Kernel.Sim;

/// <summary>Follow attached and detached creator bindings through a fenced commit and its lost answer.</summary>
internal static class FoundationChildReferences
{
    public static async Task Run(Action<bool, string> check)
    {
        const string tenant = "acme";
        var time = 1_700_100_000_000L;
        var store = new InMemoryStore(2, () => time);
        var root = InstanceRef.In(0, "acme_parent");
        var spec = new StartSpec("child-refs", "parent", Creation: new(1, 0, "root"));
        var rootCreator = new Message("root", root, Kinds.Start, Start: spec, TargetCreation: 1);
        var rootRef = store.AdmitFoundationStart(tenant, rootCreator);
        var leaseFor = TimeSpan.FromSeconds(10);
        var original = (await store.TakeAsync(0, "child-issuer", leaseFor))!;

        var child = InstanceRef.ForChild(1, 0, original.Epoch, 1);
        var detached = InstanceRef.ForChild(1, 0, original.Epoch, 2);
        string Ref(InstanceRef instance) => store.ReferenceFor(tenant, new(instance.Id, 1));
        Message Create(InstanceRef instance, bool separate)
        {
            var start = new StartSpec("child-refs", "child", Parent: separate ? null : root.Id,
                Creation: new(1, 0, instance.Id), ParentCreation: separate ? 0 : 1);
            return new Message("creator/" + instance.Id, instance, Kinds.Start, Sender: root.Id, Start: start,
                TargetCreation: 1, SenderCreation: 1) { BindFoundationChild = true }.WithInboxScope(InboxKey.ChildCreatorScope);
        }

        var parent = new InstanceRow(root.Id, spec.Model, spec.Definition, InstanceStatus.Running, "null", 1, null,
            Creation: 1, StartIntent: CreationProtocol.Fingerprint(spec));
        var commit = new CommitBatch(0, original.Epoch, original.Position, leaseFor, false,
            [new TurnCommit(root.Id, parent, null, [rootCreator.InboxKey], [], [Create(child, false), Create(detached, true)], [], [], "[]")], [], []);

        var stale = await store.CommitAsync(commit with { Epoch = original.Epoch + 1 });
        check(rootRef is not null && !stale.Landed && !store.Instances.ContainsKey(root.Id) &&
              store.AddFoundationMessage(tenant, Ref(child), "unbound", "signal") is null,
            "a rejected parent's fence creates neither a child delivery nor a usable child ref");

        var wire = new DelayedCommit(store) { LoseNext = true };
        var lost = false;
        try { await wire.CommitAsync(commit); }
        catch (IOException) { lost = true; }
        check(lost && store.Instances.ContainsKey(root.Id) && store.AddFoundationMessage(tenant, Ref(child), "one", "signal") == true &&
              store.AddFoundationMessage(tenant, Ref(detached), "two", "signal") == true,
            "the landed parent commit binds both children even when the acknowledgement is lost");

        time += (long)leaseFor.TotalMilliseconds + 1;
        var replacement = (await store.TakeAsync(0, "child-replacement", leaseFor))!;
        check(replacement.Epoch > original.Epoch && !(await store.CommitAsync(commit)).Landed &&
              store.AddFoundationMessage("other", Ref(detached), "foreign", "signal") is null,
            "a stale creator retry cannot repeat, and the detached child remains tenant-bound");

        var owner = new Worker("recipient", 1, store, new WorkflowInstance([new Probe()]), new WorkerOptions { Monotonic = () => 0 });
        await using var activation = (await new DurableHost([owner]).ActivateAsync(1))!;
        for (var step = 0; step < 20; step++)
        {
            await activation.WokenAsync(null);
            await activation.RunAsync();
            await Task.Yield();
        }
        var childRow = store.ReadFoundationActor(tenant, Ref(child)).Actor?.Row;
        var detachedRow = store.ReadFoundationActor(tenant, Ref(detached)).Actor?.Row;
        check(childRow is { ParentCreation: 1, State: "[1]" } && childRow.Parent == root.Id &&
              detachedRow is { Parent: null, ParentCreation: 0, State: "[1]" },
            "the real recipient opens attached and detached child lives and applies one exact send to each");
    }

    private sealed class Probe : Model<List<int>>
    {
        public override string Key => "child-refs";

        protected override void Turn(ITurn turn, List<int> state)
        {
            foreach (var message in turn.Inbox)
                if (message.Kind == "signal") state.Add(1);
        }
    }
}
