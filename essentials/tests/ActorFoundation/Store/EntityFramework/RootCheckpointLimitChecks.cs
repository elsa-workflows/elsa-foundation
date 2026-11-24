using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Ef.Conformance;

internal static class RootCheckpointLimitChecks
{
    internal static async Task Run(Func<KernelDb> create, EfStore store, EfStore cold)
    {
        const string tenant = "checkpoint-limit";
        const string typeData = "<storage>🌐";
        const int budget = 16 * 1024 * 1024 - 24 * 1024;
        const string state = StoredState.Family + "sentinel";
        var labels = new Dictionary<string, string> { ["escaped"] = "\u0001é" };
        var lease = TimeSpan.FromMinutes(2);
        var ingress = new PostgresRootAdmission(create, lease);
        await ingress.InstallAsync();
        var take = (await store.TakeAsync(0, "checkpoint-limit", lease))!;
        PartitionFence fence = new(0, take.Epoch, take.Position);

        var calibration = await Admit("calibration");
        Check((await First(calibration, state, fence)).Landed, "the calibration checkpoint lands");
        var calibrationBytes = await PhysicalBytes(calibration.Creator.Target.Id);
        var padding = new string('x', budget - calibrationBytes);
        string fit = state + padding;
        string overflow = fit + "x";
        fence = fence with { Position = fence.Position + 1 };

        var target = await Admit("target");
        var id = target.Creator.Target.Id;
        Check(!(await First(target, overflow, fence with { Epoch = fence.Epoch - 1 })).Landed,
            "a stale fence wins over the oversized first checkpoint");
        await Reject(() => First(target, overflow, fence));
        Check(await cold.ReadFirstRootAsync(tenant, target.Reference) is null &&
              await ingress.ReadPendingCreatorAsync(tenant, target.Reference) == target.Creator &&
              (await cold.ReadJournalOfAsync(0, id, 0)).Count == 0 && await Position() == fence.Position,
            "one byte over on birth preserves the creator, checkpoint, journal and fence");

        Check((await First(target, fit, fence)).Landed && await PhysicalBytes(id) == budget,
            "the first checkpoint lands at the exact encoded budget");
        var current = (await cold.ReadFirstRootAsync(tenant, target.Reference))!;
        Check(current.Revision == 1 && current.State == fit, "the escaped state round-trips at the boundary");
        fence = fence with { Position = fence.Position + 1 };

        await ingress.AdmitDirectAsync(tenant, target.Reference, "complete", "finish");
        var input = (await ingress.ReadPendingDirectAsync(tenant, target.Reference)).Single();
        Check(!(await End(overflow, fence with { Position = fence.Position - 1 })).Landed,
            "a stale position wins over the oversized terminal checkpoint");
        await Reject(() => End(overflow, fence));
        Check((await cold.ReadFirstRootAsync(tenant, target.Reference)) is { Revision: 1, TerminalRevision: null } before &&
              before.State == fit && (await ingress.ReadPendingDirectAsync(tenant, target.Reference)).Single() == input &&
              (await cold.ReadJournalOfAsync(0, id, 0)).Count == 1 && await Position() == fence.Position,
            "one byte over on completion preserves the original revision, input, journal and fence");

        Check((await End(fit, fence)).Landed && await PhysicalBytes(id) == budget &&
              (await cold.ReadFirstRootAsync(tenant, target.Reference)) is { Revision: 2, TerminalRevision: 2 } ended &&
              ended.State == fit && (await ingress.ReadPendingDirectAsync(tenant, target.Reference)).Count == 0 &&
              (await cold.ReadJournalOfAsync(0, id, 0)).Select(row => row.Revision).SequenceEqual([1L, 2L]) &&
              await Position() == fence.Position + 1,
            "the exact terminal write settles its input and advances the journal and fence once");

        async Task<(string Reference, Message Creator)> Admit(string key)
        {
            var receipt = await ingress.AdmitAsync(tenant, key, new("workflow", "definition"));
            return (receipt.Reference, (await ingress.ReadPendingCreatorAsync(tenant, receipt.Reference))!);
        }

        ValueTask<Committed> First((string Reference, Message Creator) root, string value, PartitionFence expected) =>
            store.CommitFirstRootAsync(tenant, root.Reference, root.Creator, expected, lease, value, 1, typeData, labels, """["birth"]""");

        ValueTask<Committed> End(string value, PartitionFence expected) =>
            store.CommitRootTurnAsync(tenant, target.Reference, current, [input], expected, lease,
                InstanceStatus.Finished, value, 1, typeData, labels, """["finish"]""");

        async Task<int> PhysicalBytes(string actor)
        {
            await using var db = create();
            return await db.Database.SqlQuery<int>($"""
                SELECT octet_length("State") + octet_length("FoundationHeader") + coalesce(octet_length("Jobs"), 0) AS "Value"
                FROM "Instances" WHERE "Id" = {actor}
                """).SingleAsync();
        }

        async Task<long> Position()
        {
            await using var db = create();
            return await db.Partitions.Select(partition => partition.Position).SingleAsync();
        }
    }

    private static async Task Reject(Func<ValueTask<Committed>> commit)
    {
        try { await commit(); }
        catch (StoreException error) when (error.Failure == Failure.Definite &&
            error.Message == "The encoded PostgreSQL checkpoint exceeds its storage budget.")
        {
            Console.WriteLine("ok the store rejected the excess byte with a definite size failure");
            return;
        }
        throw new InvalidOperationException("The checkpoint writer accepted an oversized physical row.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
