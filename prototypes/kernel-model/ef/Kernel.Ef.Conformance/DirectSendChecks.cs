using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

        foreach (var unknown in new[] { reference[..63] + '\0', reference[..63] + '\ud800', reference[..63] })
        {
            Check(await roots.AdmitDirectAsync(tenant, unknown, "first", "input") is null &&
                (await roots.ReadPendingDirectAsync(tenant, unknown)).Count == 0,
                "an invalid generated reference is unavailable for both admission and pending reads");
        }
        var another = await roots.AdmitAsync(tenant, "start-another", new StartSpec("workflow", "definition"));
        Check(await roots.AdmitDirectAsync(tenant, another.Reference, "first", "input", "another root") == true &&
            (await roots.ReadPendingDirectAsync(tenant, another.Reference)) is [{ Id: "first", Payload: "another root" }] &&
            (await roots.ReadPendingDirectAsync(tenant, reference)) is [{ Id: "first", Payload: var firstBody }] && firstBody == original,
            "the same operation on two exact roots retains its own receipt and input on each");

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

        fence = await CheckDirectReadFailures(create, store, cold, tenant, reference, another.Reference,
            before[0], fence with { Position = fence.Position + 1 }, timeout);

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
        Check((await store.CommitRootTurnAsync(tenant, reference, current, before, fence, timeout,
            InstanceStatus.Running, "applied", 14, current.TypeData, current.Labels, """["applied"]""")).Landed &&
            (await cold.ReadPendingDirectAsync(tenant, reference)).Single().InboxKey == after[2].InboxKey,
            "admitted occurrences settle after their original receipts were swept");

        // In its logical form this is 3 MiB. The stored JSON representation needs six bytes per NUL.
        var expanded = new string('\0', 3 * 1024 * 1024);
        Check(await cold.AdmitDirectAsync(tenant, another.Reference, "first", "input", expanded) == false &&
            (await cold.ReadPendingDirectAsync(tenant, another.Reference)) is [{ Id: "first", Payload: "another root" }],
            "a live receipt wins over an oversized replacement encoding and preserves its original input");
        Check(await cold.AdmitDirectAsync(tenant, another.Reference, "smaller-retry", "input", expanded) is null &&
            (await cold.ReadPendingDirectAsync(tenant, another.Reference)).Count == 1 &&
            await cold.AdmitDirectAsync(tenant, another.Reference, "smaller-retry", "input", "fits") == true &&
            (await cold.ReadPendingDirectAsync(tenant, another.Reference)) is
                [{ Id: "first", Payload: "another root" }, { Id: "smaller-retry", Payload: "fits" }],
            "a physical size refusal releases its receipt reservation for a smaller retry");
    }

    private static async Task<PartitionFence> CheckDirectReadFailures(Func<KernelDb> create, EfStore store, PostgresRootAdmission admission,
        string tenant, string reference, string otherReference, Message input, PartitionFence fence, TimeSpan lease)
    {
        await using var db = create();
        var row = await db.Messages.SingleAsync(m => m.Id == input.InboxKey.Id && m.InboxScope == input.InboxKey.Scope);
        Check(PostgresRootAdmission.DecodeDirect(row, reference) == input, "the admitted occurrence decodes under its own reference");
        try
        {
            PostgresRootAdmission.DecodeDirect(row, otherReference);
            throw new InvalidOperationException("The decoder accepted the wrong bound reference.");
        }
        catch (StoreException e) when (e.Failure == Failure.Definite) { }

        var current = (await store.ReadFirstRootAsync(tenant, reference))!;
        var originalHeader = row.Start!;
        var snapshot = db.Instances.AsNoTracking().Where(i => i.Id == current.Actor.Id)
            .Select(i => new { i.State, i.Revision, i.Status });
        var before = await snapshot.SingleAsync();
        var journal = await store.ReadJournalOfAsync(fence.Partition, current.Actor.Id, 0);

        var bound = new EfStore(create, postgres: true, fenceInBatch: store.FenceInBatch) { CommitSql = store.CommitSql };
        await bound.BindWorkerRootAsync(tenant, reference);
        var live = (await bound.LoadAsync([current.Actor.Id])).Single();
        var turn = new TurnCommit(live.Id, live with { Revision = live.Revision + 1 }, live.Revision, [], [input.InboxKey], [], [], [], """["next"]""");
        ValueTask<Committed> WorkerCommit(PartitionFence f) => bound.CommitAsync(new(f.Partition, f.Epoch, f.Position, lease, false, [turn], [], []));

        // A journal entry for a sibling moves the position while this root's revision remains current.
        var oldPosition = fence;
        for (var n = 0; ; n++)
        {
            if (n == 100) throw new InvalidOperationException("No sibling was placed on the test partition.");
            var sibling = await admission.AdmitAsync(tenant, $"fence-sibling-{n}", new StartSpec("workflow", "definition"));
            var creator = (await admission.ReadPendingCreatorAsync(tenant, sibling.Reference))!;
            if (creator.Target.Partition != fence.Partition) continue;
            var saved = await store.CommitFirstRootAsync(tenant, sibling.Reference, creator, fence, lease, "sibling", 1,
                "runtime", new Dictionary<string, string>(), """["sibling"]""");
            Check(saved.Landed, "a sibling checkpoint advances the shared position");
            fence = fence with { Position = fence.Position + 1 };
            break;
        }

        await CheckPreparationTimeout(create, store, tenant, reference, turn, oldPosition, fence, lease);

        // Simulate a future envelope codec while keeping the admitted operation, physical key and payload intact.
        row.Start = "!kernel-direct/2:" + originalHeader[(originalHeader.IndexOf(':') + 1)..];
        await db.SaveChangesAsync();
        try
        {
            await RequireDefinite(async () => { await admission.ReadPendingDirectAsync(tenant, reference); });
            ValueTask<Committed> Commit(PartitionFence f) => store.CommitRootTurnAsync(tenant, reference, current,
                [input], f, lease, InstanceStatus.Running, "next", current.StateVersion, current.TypeData, current.Labels, """["next"]""");
            await RequireDefinite(async () => { await Commit(fence); });
            Check(!(await Commit(fence with { Position = fence.Position - 1 })).Landed,
                "the stale position refuses a commit on the unreadable input");
            Check(!(await WorkerCommit(oldPosition)).Landed,
                "the root-bound worker refuses the sibling's former position before surfacing the codec failure");
            var oldEpoch = fence;
            var retaken = await store.RetakeAsync(fence.Partition, "direct-fence", fence.Epoch, lease);
            Check(retaken is not null && retaken.Epoch == oldEpoch.Epoch + 1 && retaken.Position == fence.Position,
                "the same owner's retake advances only the epoch");
            fence = fence with { Epoch = retaken!.Epoch };
            Check(!(await WorkerCommit(oldEpoch)).Landed, "the root-bound worker refuses an old epoch at the current position");
            await RequireDefinite(async () => { await WorkerCommit(fence); });

            var stored = await db.Messages.AsNoTracking().SingleAsync(m => m.Id == row.Id && m.InboxScope == row.InboxScope);
            Check(!stored.Done && stored.Start == row.Start && stored.Payload == row.Payload &&
                (await snapshot.SingleAsync()).Equals(before) &&
                await db.Partitions.Where(p => p.Id == fence.Partition).Select(p => p.Position).SingleAsync() == fence.Position &&
                (await store.ReadJournalOfAsync(fence.Partition, current.Actor.Id, 0)).SequenceEqual(journal),
                "an unsupported direct envelope leaves its input, checkpoint, journal and position unchanged");
        }
        finally
        {
            row.Start = originalHeader;
            await db.SaveChangesAsync();
        }

        return fence;

        static async Task RequireDefinite(Func<Task> attempt)
        {
            try { await attempt(); }
            catch (StoreException e) when (e.Failure == Failure.Definite &&
                e.Message == "Unsupported PostgreSQL public input envelope.") { return; }
            throw new InvalidOperationException("The future envelope must be refused as a definite failure.");
        }
    }

    private static async Task CheckPreparationTimeout(Func<KernelDb> create, EfStore store, string tenant, string reference,
        TurnCommit turn, PartitionFence stale, PartitionFence held, TimeSpan lease)
    {
        await using var session = create();
        await session.Database.OpenConnectionAsync();
        await session.Database.ExecuteSqlRawAsync("SET lock_timeout = '150ms'");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(session.Database.GetDbConnection()).Options;
        var owner = new EfStore(() => new KernelDb(options), postgres: true, fenceInBatch: store.FenceInBatch) { CommitSql = store.CommitSql };
        await owner.BindWorkerRootAsync(tenant, reference);
        ValueTask<Committed> Commit(PartitionFence f) => owner.CommitAsync(new(f.Partition, f.Epoch, f.Position, lease, false, [turn], [], []));

        // Block the root image read in this fixture's disposable database. The partition fence remains readable.
        await using var blocker = create();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlRawAsync("""LOCK TABLE "Instances" IN ACCESS EXCLUSIVE MODE""");
        Check(!(await Commit(stale)).Landed, "a preparation lock timeout with a stale position refuses the batch");
        try { await Commit(held); }
        catch (StoreException error) when (error.Failure == Failure.Transient && error.InnerException is PostgresException { SqlState: "55P03" })
        {
            Console.WriteLine("ok a preparation lock timeout with the held fence retains its PostgreSQL transient classification");
            return;
        }
        throw new InvalidOperationException("The held fence must surface the preparation lock timeout.");
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
        Console.WriteLine($"ok {message}");
    }
}
