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
        var reference = (await roots.AdmitAsync(tenant, "start", new("native", "wait"))).Reference;
        var other = (await roots.AdmitAsync(tenant, "other", new("native", "wait"))).Reference;
        var creator = (await roots.ReadPendingCreatorAsync(tenant, reference))!;
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
        var legacyRow = new InstanceRow(id, "", "", InstanceStatus.Running, "overwritten", 3, null);
        await Refused(() => store.CommitAsync(Legacy(bare with { Row = legacyRow, Expected = 2 })), "legacy overwrite after a qualified revision");
        Check((await Next(running, [finish], fence, status: InstanceStatus.Finished, state: "null")).Landed, "the final input and terminal checkpoint commit");
        fence = fence with { Position = fence.Position + 1 };
        var terminal = (await cold.ReadFirstRootAsync(tenant, reference))!;
        var journal = await cold.ReadJournalOfAsync(home, id, 0);
        Check(terminal is { Status: InstanceStatus.Finished, State: "null", Revision: 3, TerminalRevision: 3, TerminalAt: > 0 } &&
            journal.Select(entry => entry.Revision).SequenceEqual([1L, 2L, 3L]) &&
            journal.Select(entry => entry.Entries).SequenceEqual(["""["start"]""", """["step"]""", """["step"]"""]) &&
            journal.Select(entry => entry.Position).SequenceEqual([1L, 2L, 3L]), "a cold read returns one database-time terminal mark and all three journal positions and revisions");
        var successor = (await cold.TakeAsync(home, "turns", timeout))!;
        fence = new(home, successor.Epoch, successor.Position);
        await Refused(() => Next(terminal, [finish], fence), "turn after Finished");
        await Refused(() => Next(running, [finish], fence), "repeat after losing the final answer");
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
