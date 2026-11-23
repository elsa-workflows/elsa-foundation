using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Ef.Conformance;

internal static class FirstRootChecks
{
    internal static async Task Run(Func<KernelDb> create, EfStore store, EfStore cold)
    {
        var roots = new PostgresRootAdmission(create, TimeSpan.FromMinutes(1));
        await roots.InstallAsync();
        var timeout = TimeSpan.FromSeconds(30);
        const string tenant = "demo";
        var labels = new Dictionary<string, string> { ["purpose"] = "review", ["Purpose"] = "display" };
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

        var duplicate = new Dictionary<string, string>(ReferenceEqualityComparer.Instance)
        {
            [new string('x', 4)] = "first",
            [new string('x', 4)] = "second"
        };
        var tooMany = Enumerable.Range(0, 5).ToDictionary(i => i.ToString(), _ => "value");
        var tooLong = new string('é', 513);
        foreach (var invalidLabels in new[] { duplicate, tooMany, new() { [tooLong] = "value" }, new() { ["key"] = tooLong } })
        {
            try
            {
                await store.CommitFirstRootAsync(tenant, first.Reference, creator, fence, timeout, null, 14, "data", invalidLabels, "[]");
                throw new InvalidOperationException("The checkpoint accepted an invalid label set.");
            }
            catch (ArgumentException e) when (e.ParamName == "labels") { }
        }
        await using (var inspect = create())
        {
            Check(await roots.ReadPendingCreatorAsync(tenant, first.Reference) == creator &&
                await cold.ReadFirstRootAsync(tenant, first.Reference) is null &&
                (await cold.ReadJournalOfAsync(home, creator.Target.Id, 0)).Count == 0 &&
                await inspect.Partitions.Where(p => p.Id == home).Select(p => p.Position).SingleAsync() == fence.Position,
                "invalid labels preserve the creator, absent revision, fence position and empty journal");
        }

        var landed = await Save(tenant, first.Reference, creator, fence, state: null);
        var takenUp = await cold.ReadFirstRootAsync(tenant, first.Reference);
        Check(landed.Landed && takenUp is { State: null, Model: "workflow", TypeData: "changed data", Definition: "original header", StateVersion: 14, Revision: 1 } &&
            takenUp.Actor == new CreationAddress(creator.Target.Id, 1) && takenUp.Labels is { Count: 2 } && takenUp.Labels["purpose"] == "review" && takenUp.Labels["Purpose"] == "display",
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

        var exactSpec = new StartSpec("workflow", "original\ud800", "input\ud800");
        var exact = await roots.AdmitAsync(tenant, "exact-code-units", exactSpec);
        var exactCreator = (await roots.ReadPendingCreatorAsync(tenant, exact.Reference))!;
        var exactHome = exactCreator.Target.Partition;
        var exactLease = (await store.TakeAsync(exactHome, "root-owner", timeout))!;
        var exactFence = new PartitionFence(exactHome, exactLease.Epoch, exactLease.Position);
        var changedInput = exactCreator with { Payload = "input\ud801", Start = exactCreator.Start! with { Input = "input\ud801" } };
        var changedDefinition = exactCreator with { Start = exactCreator.Start! with { Definition = "original\ud801" } };
        foreach (var changed in new[] { changedInput, changedDefinition })
        {
            Check(CreationProtocol.Fingerprint(exactCreator.Start, exactCreator.Kind, exactCreator.Payload) ==
                CreationProtocol.Fingerprint(changed.Start, changed.Kind, changed.Payload), "old fingerprint equals the alternate code-unit spelling");
            await Refused(Save(tenant, exact.Reference, changed, exactFence), "substituted creator code unit");
        }
        Check(await roots.ReadPendingCreatorAsync(tenant, exact.Reference) == exactCreator &&
            await cold.ReadFirstRootAsync(tenant, exact.Reference) is null &&
            (await cold.ReadJournalOfAsync(exactHome, exactCreator.Target.Id, 0)).Count == 0,
            "rejected spellings preserve the exact creator payload and definition, with no checkpoint or entry");
        var exactLanded = await store.CommitFirstRootAsync(tenant, exact.Reference, exactCreator, exactFence, timeout,
            exactCreator.Payload, 14, exactSpec.Definition, labels, "[]");
        var exactImage = await cold.ReadFirstRootAsync(tenant, exact.Reference);
        Check(exactLanded.Landed && exactImage is not null && exactImage.State == exactSpec.Input && exactImage.Definition == exactSpec.Definition &&
            exactImage.TypeData == exactSpec.Definition, "the admitted input, initial definition and type data retain their exact UTF-16 units");

        await using var db = create();
        Task<long> DbNow() => db.Database.SqlQueryRaw<long>(
            """SELECT floor(extract(epoch from clock_timestamp()) * 1000)::bigint AS "Value" """).SingleAsync();
        foreach (var schedule in new long?[] { null, 0, 2_000 })
        {
            var due = schedule > 0 ? await DbNow() + schedule : schedule;
            var scheduled = await roots.AdmitAsync(tenant, Guid.NewGuid().ToString("N"), spec, due);
            var input = (await roots.ReadPendingCreatorAsync(tenant, scheduled.Reference))!;
            var partition = input.Target.Partition;
            var owner = (await store.TakeAsync(partition, "root-owner", timeout))!;
            var bound = new PartitionFence(partition, owner.Epoch, owner.Position);
            if (due is > 0)
            {
                Check(await DbNow() < due, "future creator is pending on the database clock");
                await Refused(Save(tenant, scheduled.Reference, input with { Due = null }, bound), "removing the stored future deadline");
                await Refused(Save(tenant, scheduled.Reference, input, bound), "future creator before its database deadline");
                Check(await roots.ReadPendingCreatorAsync(tenant, scheduled.Reference) == input &&
                    await cold.ReadFirstRootAsync(tenant, scheduled.Reference) is null &&
                    (await cold.ReadJournalOfAsync(partition, input.Target.Id, 0)).Count == 0,
                    "early attempts leave the creator, fence position and journal unchanged");
                var delay = due.Value - await DbNow();
                if (delay >= 0) await Task.Delay(TimeSpan.FromMilliseconds(delay + 1));
            }
            else
            {
                await Refused(Save(tenant, scheduled.Reference, input with { Due = due == 0 ? null : 0 }, bound),
                    "changing presence of an eligible deadline");
            }
            Check(input.Due == due && (await Save(tenant, scheduled.Reference, input, bound)).Landed,
                $"exact creator commits when eligible (due {due?.ToString() ?? "absent"})");
        }

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
