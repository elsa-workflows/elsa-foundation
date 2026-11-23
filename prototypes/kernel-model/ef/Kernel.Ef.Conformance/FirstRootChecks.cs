using Kernel.EntityFramework;

namespace Kernel.Ef.Conformance;

internal static class FirstRootChecks
{
    internal static async Task Run(Func<KernelDb> create, EfStore store, EfStore cold)
    {
        var roots = new PostgresRootAdmission(create, TimeSpan.FromMinutes(1));
        await roots.InstallAsync();
        var timeout = TimeSpan.FromSeconds(30);
        const string tenant = "demo";
        var labels = new Dictionary<string, string> { ["purpose"] = "review" };
        var spec = new StartSpec("workflow", "original header", "in");
        var first = await roots.AdmitAsync(tenant, "first", spec);
        var second = await roots.AdmitAsync(tenant, "second", spec);
        var creator = (await roots.ReadPendingCreatorAsync(tenant, first.Reference))!;
        var home = creator.Target.Partition;
        var lease = (await store.TakeAsync(home, "root-owner", timeout))!;
        var fence = new PartitionFence(home, lease.Epoch, lease.Position);

        ValueTask<Committed> Save(string owner, string reference, Message input, PartitionFence f, string? state = "state") =>
            store.CommitFirstRootAsync(owner, reference, input, f, timeout, state, 14, "changed data", labels, """["started","waiting"]""");

        await Refused(Save(tenant, first.Reference, creator, fence with { Epoch = lease.Epoch - 1 }), "stale epoch");
        await Refused(Save(tenant, first.Reference, creator, fence with { Position = lease.Position + 1 }), "wrong journal position");
        await Refused(Save("other", first.Reference, creator, fence), "other tenant");
        await Refused(Save(tenant, second.Reference, creator, fence), "other creation reference");
        await Refused(Save(tenant, first.Reference, creator with { Start = creator.Start! with { Definition = "different" } }, fence), "wrong creator intent");
        try
        {
            await Save(tenant, first.Reference, creator with { Id = "other operation" }, fence);
            throw new InvalidOperationException("a different logical operation was accepted as the creator");
        }
        catch (ArgumentException error) when (error.ParamName == "creator") { }
        Check(await cold.ReadFirstRootAsync(tenant, first.Reference) is null &&
            await roots.ReadPendingCreatorAsync(tenant, first.Reference) == creator &&
            (await cold.ReadJournalOfAsync(home, creator.Target.Id, 0)).Count == 0,
            "all refused first checkpoints preserve the creator, journal and absence of state");

        var landed = await Save(tenant, first.Reference, creator, fence, state: null);
        var takenUp = await cold.ReadFirstRootAsync(tenant, first.Reference);
        Check(landed.Landed && takenUp is { State: null, Model: "workflow", TypeData: "changed data", Definition: "original header", StateVersion: 14, Revision: 1 } &&
            takenUp.Actor == new CreationAddress(creator.Target.Id, 1) && takenUp.Labels["purpose"] == "review",
            "landed first checkpoint restores the exact qualified identity, absent state, headers, model and revision");
        Check(await roots.ReadPendingCreatorAsync(tenant, first.Reference) is null &&
            (await cold.ReadJournalOfAsync(home, creator.Target.Id, 0)).Single() is { Revision: 1, Entries: """["started","waiting"]""" } j &&
            j.Position == lease.Position + 1, "creator consumption and journal position share the landed checkpoint");
        Check(await cold.ReadFirstRootAsync("other", first.Reference) is null &&
            await cold.ReadFirstRootAsync(tenant, second.Reference) is null, "scoped point read separates tenants and roots");

        var successor = (await store.TakeAsync(home, "root-owner", timeout))!;
        await Refused(Save(tenant, first.Reference, creator, new(home, successor.Epoch, successor.Position)), "cold retry after lost success");
        Check((await cold.ReadJournalOfAsync(home, creator.Target.Id, 0)).Count == 1 &&
            (await cold.ReadFirstRootAsync(tenant, first.Reference))!.Revision == 1, "retry retains exactly one checkpoint and one journal row");
        try
        {
            await cold.LoadAsync([creator.Target.Id]);
            throw new InvalidOperationException("legacy actor read accepted qualified state");
        }
        catch (StoreException error) when (error.Failure == Failure.Definite) { }
        var unqualified = new InstanceRow(creator.Target.Id, "m", "d", InstanceStatus.Running, "wrong", 2, null);
        var update = new TurnCommit(unqualified.Id, unqualified, 1, [], [], [], [], [], """["wrong"]""");
        var delete = new TurnCommit(unqualified.Id, null, 1, [], [], [], [], [], """["wrong"]""", Delete: true);
        foreach (var turn in new[] { update, delete })
            await Refused(store.CommitAsync(new CommitBatch(home, successor.Epoch, successor.Position, timeout, false, [turn], [], [])),
                turn.Delete ? "legacy delete" : "legacy update");
        Check((await cold.ReadFirstRootAsync(tenant, first.Reference))?.TypeData == "changed data" &&
            (await cold.ReadJournalOfAsync(home, creator.Target.Id, 0)).Count == 1, "unqualified access preserves an exact checkpoint");

        foreach (var state in new[] { "null", "!kernel-state/3:absent", "state\u0000value", "state\ud800value" })
        {
            var root = await roots.AdmitAsync(tenant, Guid.NewGuid().ToString("N"), spec);
            var input = (await roots.ReadPendingCreatorAsync(tenant, root.Reference))!;
            var partition = input.Target.Partition;
            var current = (await store.TakeAsync(partition, "root-owner", timeout))!;
            var result = await Save(tenant, root.Reference, input, new(partition, current.Epoch, current.Position), state);
            Check(result.Landed && (await cold.ReadFirstRootAsync(tenant, root.Reference))?.State == state,
                $"opaque state survives the first public checkpoint ({state.Length} code units)");
        }
    }

    private static async Task Refused(ValueTask<Committed> attempt, string description)
    {
        try { if ((await attempt).Landed) throw new InvalidOperationException(description + " landed"); }
        catch (StoreException error) when (error.Failure == Failure.Definite) { }
        Console.WriteLine("ok refused " + description);
    }

    private static void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
