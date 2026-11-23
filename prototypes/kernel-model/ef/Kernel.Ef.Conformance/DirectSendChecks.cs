using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Ef.Conformance;

internal static class DirectSendChecks
{
    internal static async Task Run(Func<KernelDb> create, EfStore store)
    {
        const string tenant = "demo";
        var roots = new PostgresRootAdmission(create, TimeSpan.FromMinutes(1));
        await roots.InstallAsync();
        var admitted = await roots.AdmitAsync(tenant, "start-direct", new StartSpec("workflow", "definition"));
        var reference = admitted.Reference;
        var creator = (await roots.ReadPendingCreatorAsync(tenant, reference))!;
        var original = "payload\u0000\ud800";
        const string originalKind = "input\u0000next";
        Check(await roots.AdmitDirectAsync(tenant, reference, "first", originalKind, original, due: 0) == true,
            "the exact pending creator accepts an ordinary input");
        Check(await roots.AdmitDirectAsync(tenant, reference, "first", "input", "replacement") == false,
            "a replay remains a duplicate and cannot replace the pending payload");
        Check(await roots.AdmitDirectAsync("other", reference, "wrong-scope", "input") is null &&
            (await roots.ReadPendingDirectAsync("other", reference)).Count == 0,
            "reads and admissions require the bound tenant");
        Check(await roots.AdmitDirectAsync(tenant, reference, "requires-qualification", Kinds.Delete) is null,
            "the ordinary route leaves qualified commands to their operator path");

        var timeout = TimeSpan.FromSeconds(30);
        var acquired = (await store.TakeAsync(creator.Target.Partition, "direct-fence", timeout))!;
        var fence = new PartitionFence(creator.Target.Partition, acquired.Epoch, acquired.Position);
        var saved = await store.CommitFirstRootAsync(tenant, reference, creator, fence, timeout, "waiting", 14, "runtime",
            new Dictionary<string, string>(), """["started"]""");
        Check(saved.Landed, "the live checkpoint consumes only its creator");
        var cold = new PostgresRootAdmission(create, TimeSpan.FromMinutes(1));
        var sameId = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(index => cold.AdmitDirectAsync(tenant, reference, "second", "input", $"path-{index}")));
        var winner = Array.FindIndex(sameId, value => value == true);
        Check(winner >= 0 && sameId.Count(value => value == true) == 1 && sameId.Count(value => value == false) == 7,
            "eight concurrent live admissions leave one durable receipt and one input");
        var before = await cold.ReadPendingDirectAsync(tenant, reference);
        Check(before is [{ Id: "first", Due: 0, TargetCreation: 1 }, { Id: "second", Due: null, TargetCreation: 1 }] &&
              before[0].Payload == original && before[0].Kind == originalKind && before[1].Payload == $"path-{winner}" &&
              before.All(message => message.Target == creator.Target),
            "cold reads preserve actor life, FIFO, due presence, and exact kind and payload");

        var valid = before[0].InboxKey.Id;
        Check(PostgresRootAdmission.MatchesDirectOccurrence(reference, "first", valid) &&
            !PostgresRootAdmission.MatchesDirectOccurrence(reference, "second", valid) &&
            !PostgresRootAdmission.MatchesDirectOccurrence(reference, "first", valid[..^1] + "!"),
            "occurrence validation binds the original operation and its generated suffix");

        await using (var db = create())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "FoundationDirectReceipts" SET "Until" = 0 WHERE "Reference" = {reference}
                """);
        }
        Check(await cold.SweepDirectReceiptsAsync() == 2 &&
              await cold.AdmitDirectAsync(tenant, reference, "first", "input", "new occurrence", due: 2_000_000_000_000) == true,
            "receipt expiry permits a new occurrence while the original remains pending");
        var after = await cold.ReadPendingDirectAsync(tenant, reference);
        Check(after.Count == 3 && after[0] == before[0] && after[1] == before[1] &&
              after[2] is { Id: "first", Payload: "new occurrence", Due: 2_000_000_000_000 } &&
              after.Select(message => message.InboxKey).Distinct().Count() == 3,
            "reused operation has a separate physical occurrence and leaves earlier input untouched");
        var current = (await store.ReadFirstRootAsync(tenant, reference))!;
        Check((await store.CommitRootTurnAsync(tenant, reference, current, before, fence with { Position = fence.Position + 1 }, timeout,
            InstanceStatus.Running, "applied", 14, current.TypeData, current.Labels, """["applied"]""")).Landed &&
            (await cold.ReadPendingDirectAsync(tenant, reference)).Single().InboxKey == after[2].InboxKey,
            "admitted occurrences settle after their original receipts were swept");
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
        Console.WriteLine($"ok {message}");
    }
}
