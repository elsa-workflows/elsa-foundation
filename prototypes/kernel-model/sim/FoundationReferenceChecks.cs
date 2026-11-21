namespace Kernel.Sim;

/// <summary>Exact root references and delivery through the built-in in-memory store.</summary>
internal static class FoundationReferenceChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const string tenant = "acme";
        var time = 1_700_000_000_000L;
        var store = new InMemoryStore(1, () => time);
        var target = InstanceRef.In(0, "acme_order");
        var first = new Message("creator-1", target, Kinds.Start,
            Start: new StartSpec("reference-probe", "probe", Creation: new(1, 0, "first")), TargetCreation: 1);

        var proposed = store.ReferenceFor(tenant, new(target.Id, 1));
        check(store.AddFoundationMessage(tenant, proposed, "uncommitted", "signal") is null && store.Messages.Count == 0,
            "a prospective ref cannot route before the durable creator is admitted");

        var admitted = store.AdmitFoundationStart(tenant, first);
        check(admitted == proposed && store.AdmitFoundationStart(tenant, first) == admitted && store.Messages.Count == 1,
            "the exact ref follows the durable creator and replays without a second creator");
        check(store.AdmitFoundationStart(tenant, first with { Payload = "changed" }) is null,
            "the same creator receipt does not admit a changed request");
        check(store.AddFoundationMessage("other", proposed, "foreign", "signal") is null &&
              store.AddFoundationMessage(tenant, target.Id, "raw", "signal") is null,
            "foreign and raw actor ids cannot route as exact refs");

        var oldDue = time + 100;
        check(store.AddFoundationMessage(tenant, proposed, "deferred-old", "signal", "\"old\"", oldDue) == true &&
              store.Messages[new InboxKey("deferred-old")].Message.TargetCreation == 1,
            "an exact send to a pending creator is pinned to its accepted life");

        Worker Owner(string node) => new(node, 0, store, new WorkflowInstance([new Probe()]),
            new WorkerOptions { Monotonic = () => time });

        var originalOwner = (await new DurableHost([Owner("original-ref")]).ActivateAsync(0))!;
        await Pump(originalOwner);
        check(store.Instances.GetValueOrDefault(target.Id) is { Creation: 1, Retired: false },
            "the committed creator opens the exact life through the worker");
        await originalOwner.DisposeAsync();

        await using var nextOwner = (await new DurableHost([Owner("replacement-ref")]).ActivateAsync(0))!;
        check(store.AddFoundationMessage(tenant, proposed, "before-end", "signal", "\"first\"") == true &&
              store.AddFoundationMessage(tenant, proposed, "before-end", "signal", "\"first\"") == false,
            "the same exact ref routes after owner replacement and the same id is a duplicate");
        await Pump(nextOwner);
        check(Read() is ["first"], "the replacement applies the exact send once");
        check(store.AddFoundationMessage(tenant, proposed, "finish", "finish") == true, "the exact ref can end its own life");
        await Pump(nextOwner);
        check(store.Instances.GetValueOrDefault(target.Id)?.Status == InstanceStatus.Finished, "the first life finishes");
        check(store.AddFoundationMessage(tenant, proposed, "delete-first", Kinds.Delete) == true, "a qualified delete is addressed to the first life");
        await Pump(nextOwner);
        check(store.Instances.GetValueOrDefault(target.Id) is { Creation: 1, Retired: true } &&
              store.AddFoundationMessage(tenant, proposed, "after-delete", "signal") is null,
            "a retired ref refuses a new send");

        var second = first with { Id = "creator-2", TargetCreation = 2, Start = first.Start! with { Creation = new(2, 1, "second") } };
        check(store.AdmitFoundationStart("other", second) is null,
            "a different tenant cannot claim the retired actor's successor");
        var successor = store.AdmitFoundationStart(tenant, second);
        check(successor is not null && successor != admitted && store.AdmitFoundationStart(tenant, first) == admitted,
            "a successor's ref differs and the previous admission still replays to its original life");
        check(store.AddFoundationMessage(tenant, proposed, "after-recreate", "signal") is null,
            "an old ref cannot select the successor");
        if (successor is not null)
            check(store.AddFoundationMessage(tenant, successor, "second-life", "signal", "\"new\"") == true,
                "the successor's own ref admits the new send");
        time = oldDue;
        await Pump(nextOwner);
        check(store.Instances.GetValueOrDefault(target.Id) is { Creation: 2, Retired: false } && Read() is ["new"] &&
              !store.Messages.ContainsKey(new InboxKey("deferred-old")),
            "delivery discards the old pinned input and applies only the successor's send");
        check(store.AddFoundationMessage(tenant, proposed, "after-successor-commit", "signal") is null,
            "the old ref stays unavailable after the successor's first turn lands");

        List<string>? Read() => store.Instances.TryGetValue(target.Id, out var row) ? Json.Read<List<string>>(row.State) : null;
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

    private sealed class Probe : Model<List<string>>
    {
        public override string Key => "reference-probe";

        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
                if (message.Kind == "signal")
                    state.Add(message.Read<string>());
                else if (message.Kind == "finish")
                    turn.SetStatus(InstanceStatus.Finished);
        }
    }
}
