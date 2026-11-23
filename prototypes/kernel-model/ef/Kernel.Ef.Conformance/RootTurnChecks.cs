using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Ef.Conformance;

internal static class RootTurnChecks
{
    internal static async Task Run(Func<KernelDb> create, EfStore store, EfStore cold)
    {
        const string tenant = "root-turns";
        var timeout = TimeSpan.FromSeconds(30);
        var roots = new PostgresRootAdmission(create, TimeSpan.FromMinutes(10));
        await roots.InstallAsync();
        // SubscribeAsync stores an actor subscription in partition zero. Exercise a different actor partition.
        string reference;
        Message creator;
        for (var candidate = 0; ; candidate++)
        {
            if (candidate == 100) throw new InvalidOperationException("Could not qualify a nonzero-partition root.");
            reference = (await roots.AdmitAsync(tenant, $"start-{candidate}", new("native", "wait"))).Reference;
            creator = (await roots.ReadPendingCreatorAsync(tenant, reference))!;
            if (creator.Target.Partition != 0) break;
        }
        var other = (await roots.AdmitAsync(tenant, "other", new("native", "wait"))).Reference;
        var id = creator.Target.Id;
        var home = creator.Target.Partition;
        var lease = (await store.TakeAsync(home, "turns", timeout))!;
        var fence = new PartitionFence(home, lease.Epoch, lease.Position);
        var labels = new Dictionary<string, string> { ["step"] = "ready" };
        Check((await store.CommitFirstRootAsync(tenant, reference, creator, fence, timeout, null, 1, "layout", labels, """["start"]""")).Landed,
            "the first checkpoint lands");
        fence = fence with { Position = fence.Position + 1 };
        var current = (await cold.ReadFirstRootAsync(tenant, reference))!;
        Check(current is { Revision: 1, Status: InstanceStatus.Running, State: null, TerminalRevision: null, TerminalAt: null },
            "the first Running revision has no terminal mark");

        await roots.AdmitDirectAsync(tenant, reference, "one", "step", "first");
        await roots.AdmitDirectAsync(tenant, reference, "two", "step", "second");
        await roots.AdmitDirectAsync(tenant, reference, "later", "step", due: long.MaxValue);
        await roots.AdmitDirectAsync(tenant, other, "one", "step");
        var inputs = await roots.ReadPendingDirectAsync(tenant, reference);
        ValueTask<Committed> Next(EfStore.RootImage before, IReadOnlyList<Message> selected, PartitionFence f, string t = tenant,
            string? r = null, InstanceStatus status = InstanceStatus.Running, string? state = "opaque\0\ud800") =>
            store.CommitRootTurnAsync(t, r ?? reference, before, selected, f, timeout, status, state, 1, "layout2", labels, """["step"]""");

        var duplicateLabels = new Dictionary<string, string>(ReferenceEqualityComparer.Instance)
        {
            [new string('x', 3)] = "first",
            [new string('x', 3)] = "second"
        };
        await Refused(() => store.CommitRootTurnAsync(tenant, reference, current, [inputs[0]], fence, timeout,
            InstanceStatus.Running, "changed", 1, "layout2", duplicateLabels, "[]"), "duplicate ordinal labels on a subsequent root checkpoint");
        await Refused(() => Next(current, [inputs[0]], fence with { Epoch = lease.Epoch - 1 }), "stale epoch");
        await Refused(() => Next(current, [inputs[0]], fence with { Position = 0 }), "old position");
        await Refused(() => Next(current, [inputs[0]], fence, t: "other"), "other tenant");
        await Refused(() => Next(current, [inputs[0]], fence, r: other), "another exact reference");
        await Refused(() => Next(current with { Revision = 2 }, [inputs[0]], fence), "stale input revision");
        await Refused(() => Next(current with { Model = "other" }, [inputs[0]], fence), "changed model identity");
        await Refused(() => Next(current, [inputs[0] with { TargetCreation = 2 }], fence), "other input creation");
        await Refused(() => Next(current, [inputs[0], inputs[0]], fence), "repeated physical key");
        await Refused(() => Next(current, [inputs[1]], fence), "later ready input ahead of the earlier one");
        await Refused(() => Next(current, [inputs[0], inputs[1] with { Payload = "changed" }], fence), "changed second envelope");
        await Refused(() => Next(current, inputs, fence), "future direct input in a group");
        var bare = new TurnCommit(id, null, null, [], [], [], [], [], """["bare"]""");
        CommitBatch Legacy(params TurnCommit[] turns) => new(home, fence.Epoch, fence.Position, timeout, false, turns, [], []);
        await Refused(() => store.CommitAsync(Legacy(bare with { Consumed = [inputs[0].InboxKey] })), "unqualified consumption");
        await Refused(() => store.CommitAsync(Legacy(bare)), "unqualified journal");
        await Refused(() => store.CommitAsync(Legacy(bare with { Sent = [new Message("unadmitted", creator.Target, "step").WithInboxScope(inputs[0].InboxKey.Scope)] })),
            "unqualified direct envelope insert");
        await using (var db = create())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION fail_final_root_write() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN RAISE EXCEPTION 'injected checkpoint refusal' USING ERRCODE = '23514'; END $$;
                CREATE TRIGGER test_root_refusal BEFORE UPDATE ON "Instances" FOR EACH ROW
                    WHEN (NEW."FoundationWriteRevision" = 2) EXECUTE FUNCTION fail_final_root_write()
                """);
            await Refused(() => Next(current, inputs.Take(2).ToArray(), fence), "checkpoint write failure after taking the group");
            await db.Database.ExecuteSqlRawAsync("""DROP TRIGGER test_root_refusal ON "Instances"; DROP FUNCTION fail_final_root_write()""");
        }
        Check((await roots.ReadPendingDirectAsync(tenant, reference)).Count == 3 && (await cold.ReadFirstRootAsync(tenant, reference))!.Revision == 1 &&
            (await cold.ReadJournalOfAsync(home, id, 0)).Count == 1, "refusals preserve the entire input group, revision and journal");

        Check((await Next(current, inputs.Take(2).ToArray(), fence)).Landed, "two ready inputs commit one Running revision");
        fence = fence with { Position = fence.Position + 1 };
        var running = (await cold.ReadFirstRootAsync(tenant, reference))!;
        Check(running is { Revision: 2, Status: InstanceStatus.Running, TerminalRevision: null, TerminalAt: null, State: "opaque\0\ud800" } &&
            running.Labels["step"] == "ready" && running.TypeData == "layout2" && (await roots.ReadPendingDirectAsync(tenant, reference)).Single().Id == "later" &&
            (await roots.ReadPendingDirectAsync(tenant, other)).Count == 1, "the next client reloads the encoded state and only the future input stays pending");
        await Refused(() => Next(current, [inputs[0]], fence), "old revision after an acknowledged commit");
        Check(await roots.AdmitDirectAsync(tenant, reference, "one", "step") == false, "the original direct receipt still deduplicates");

        await roots.AdmitDirectAsync(tenant, reference, "finish", "done", due: 0);
        var finish = (await roots.ReadPendingDirectAsync(tenant, reference)).Single(m => m.Id == "finish");
        await Refused(() => store.CommitRootTurnAsync(tenant, reference, running, [finish], fence, timeout, InstanceStatus.Finished,
            "terminal", 1, "layout2", labels, null), "finished checkpoint without a journal entry");
        Check((await cold.ReadFirstRootAsync(tenant, reference)) is { Revision: 2, Status: InstanceStatus.Running, TerminalRevision: null, TerminalAt: null } &&
            (await roots.ReadPendingDirectAsync(tenant, reference)).Contains(finish) && (await cold.ReadJournalOfAsync(home, id, 0)).Count == 2,
            "missing terminal journal leaves the original revision, pending input and history intact");
        var legacyRow = new InstanceRow(id, "", "", InstanceStatus.Running, "overwritten", 3, null);
        await Refused(() => store.CommitAsync(Legacy(bare with { Row = legacyRow, Expected = 2 })), "legacy overwrite after a qualified revision");
        await roots.AdmitDirectAsync(tenant, reference, "queued-1", "step");
        await roots.AdmitDirectAsync(tenant, reference, "queued-2", "step");
        var pending = await roots.ReadPendingDirectAsync(tenant, reference);
        var queued1 = pending.Single(input => input.Id == "queued-1").InboxKey;
        var queued2 = pending.Single(input => input.Id == "queued-2").InboxKey;
        var future = pending.Single(input => input.Id == "later").InboxKey;
        var otherActor = (await roots.ReadPendingCreatorAsync(tenant, other))!.Target.Id;
        await store.SubscribeAsync(new("terminal-check", id, "guard"));
        await store.SubscribeAsync(new("terminal-check", otherActor, "independent"));
        await using (var db = create())
        {
            Check(home != 0 && await db.Subscriptions.AnyAsync(s => s.Instance == id && s.Partition == 0),
                "ordinary subscription registration stored the nonzero-home actor's wait in partition zero");
            await Refused(() => Next(running, [finish], fence, status: InstanceStatus.Finished),
                "a terminal commit while its exact actor still has a wait in another storage partition");
            Check((await cold.ReadFirstRootAsync(tenant, reference)) is { Revision: 2, Status: InstanceStatus.Running } &&
                (await roots.ReadPendingDirectAsync(tenant, reference)).Contains(finish) &&
                await db.Subscriptions.CountAsync(s => s.Key == "terminal-check") == 2 &&
                (await cold.ReadJournalOfAsync(home, id, 0)).Count == 2,
                "refusing completion preserves the input, actor, journal and both registrations");
            await db.Subscriptions.Where(s => s.Instance == id && s.Key == "terminal-check").ExecuteDeleteAsync();
        }
        Check((await Next(running, [finish], fence, status: InstanceStatus.Finished, state: "null")).Landed, "the final input and terminal checkpoint commit");
        fence = fence with { Position = fence.Position + 1 };
        Check((await store.FindSubscriptionsAsync("terminal-check")).Single().Instance == otherActor,
            "terminal commit preserves the registration of the unrelated actor");
        var terminal = (await cold.ReadFirstRootAsync(tenant, reference))!;
        var journal = await cold.ReadJournalOfAsync(home, id, 0);
        Check(terminal is { Status: InstanceStatus.Finished, State: "null", Revision: 3, TerminalRevision: 3, TerminalAt: > 0 } &&
            journal.Select(entry => entry.Revision).SequenceEqual([1L, 2L, 3L]) &&
            journal.Select(entry => entry.Entries).SequenceEqual(["""["start"]""", """["step"]""", """["step"]"""]) &&
            journal.Select(entry => entry.Position).SequenceEqual([1L, 2L, 3L]), "a cold read returns one database-time terminal mark and all three journal positions and revisions");
        var oldFence = fence;
        var successor = (await cold.TakeAsync(home, "turns", timeout))!;
        fence = new(home, successor.Epoch, successor.Position);
        await Refused(() => Next(terminal, [finish], fence), "turn after Finished");
        await Refused(() => Next(running, [finish], fence), "repeat after losing the final answer");

        await cold.BindWorkerRootAsync(tenant, reference);
        ValueTask<Committed> Ended(PartitionFence f, params InboxKey[] keys)
        {
            var settled = new TurnCommit(id, null, null, [], keys, [], [], [], null);
            return cold.CommitAsync(new(f.Partition, f.Epoch, f.Position, timeout, false, [settled], [], []));
        }
        await Refused(() => Ended(oldFence, queued1), "old owner clearing the terminal suffix after a take");
        await Refused(() => Ended(fence, queued2), "terminal cleanup skipping an earlier due input");
        Check((await roots.ReadPendingDirectAsync(tenant, reference)).Count == 3 &&
              (await Ended(fence, queued1, queued2)).Landed, "the successor settles both due suffix inputs in FIFO order");
        await Refused(() => Ended(fence, future), "terminal cleanup before the remaining input is due");
        var after = (await cold.ReadFirstRootAsync(tenant, reference))!;
        Check(after.Revision == terminal.Revision && after.State == terminal.State && after.TerminalAt == terminal.TerminalAt &&
            (await cold.ReadJournalOfAsync(home, id, 0)).SequenceEqual(journal) &&
            (await roots.ReadPendingDirectAsync(tenant, reference)).Single().InboxKey == future,
            "cold terminal cleanup preserves checkpoint, terminal mark, journal and future input");
        Check(await roots.AdmitDirectAsync(tenant, reference, "queued-1", "step") == false &&
            await roots.AdmitDirectAsync("foreign", reference, "queued-1", "step") is null,
            "a live finished-root receipt stays private to its tenant");
        await using (var db = create())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "FoundationDirectReceipts" SET "Until" = 0 WHERE "Reference" = {reference}
                """);
            Check(await roots.AdmitDirectAsync(tenant, reference, "queued-1", "step") is null &&
                await roots.AdmitDirectAsync(tenant, reference, "after-finish", "step") is null &&
                await db.Database.SqlQuery<long>($"""
                    SELECT count(*) AS "Value" FROM "FoundationDirectReceipts" WHERE "Reference" = {reference}
                        AND "Until" <> 0
                    """).SingleAsync() == 0,
                "finished-root refusal creates no receipt and never refreshes an expired receipt");
        }
    }

    private static async Task Refused(Func<ValueTask<Committed>> call, string what)
    {
        bool landed;
        try { landed = (await call()).Landed; }
        catch (StoreException) { landed = false; }
        catch (ArgumentException) { landed = false; }
        Check(!landed, what + " refuses");
    }

    private static void Check(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + what);
        Console.WriteLine("ok " + what);
    }
}
