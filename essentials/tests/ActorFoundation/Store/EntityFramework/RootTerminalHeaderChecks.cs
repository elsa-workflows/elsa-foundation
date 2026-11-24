using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Ef.Conformance;

internal static class RootTerminalHeaderChecks
{
    internal static async Task Run(Func<KernelDb> create, EfStore store, EfStore cold)
    {
        const int contentBudget = 16 * 1024 * 1024 - 24 * 1024;
        const string tenant = "terminal-header";
        var lease = TimeSpan.FromMinutes(2);
        var ingress = new PostgresRootAdmission(create, lease);
        await ingress.InstallAsync();
        var taken = (await store.TakeAsync(0, "terminal-header", lease))!;
        var fence = new PartitionFence(0, taken.Epoch, taken.Position);
        var liveLabels = new Dictionary<string, string>();
        var maximalType = new string('\u0001', 16 * 1024);
        var maximalLabels = Enumerable.Range(1, 4).ToDictionary(index => new string((char)index, 1024), _ => new string('\u0001', 1024));

        var calibration = await Admit("calibration");
        Check((await Birth(calibration, "\0")).Landed, "a small escaped state calibrates the storage codec");
        Advance();
        var calibrationInput = await Add(calibration, "complete");
        Check((await Write(calibration, calibrationInput, "\0", ended: true)).Landed, "the full escaped terminal header lands for the calibration");
        Advance();
        var minimumTerminal = await Bytes(calibration);
        // The larger of the encoded state-escape flags is 'false', one byte longer than
        // the actual 'true' on these rows. Each logical NUL adds six encoded state bytes.
        var capacity = contentBudget - minimumTerminal.Count - 1;
        var fit = new string('\0', capacity / 6 + 1) + new string('x', capacity % 6);
        var overflow = fit + "x";
        var target = await Admit("target");
        var id = target.Creator.Target.Id;

        await Reject(() => Birth(target, overflow));
        Check(await cold.ReadFirstRootAsync(tenant, target.Reference) is null &&
            await ingress.ReadPendingCreatorAsync(tenant, target.Reference) == target.Creator &&
            (await cold.ReadJournalOfAsync(0, id, 0)).Count == 0 && await Position() == fence.Position,
            "a live birth one byte over the projected budget leaves the creator, journal and fence intact");
        Check((await Birth(target, fit)).Landed, "the projected exact-fit live birth lands");
        Advance();
        var active = await Bytes(target);
        Check(active.Count < contentBudget, "the live row is physically below the cap before the terminal header write");
        var next = await Add(target, "update");
        await Reject(() => Write(target, next, overflow, ended: false));
        Check(await Bytes(target) == active && (await cold.ReadFirstRootAsync(tenant, target.Reference))?.Revision == 1 &&
            (await ingress.ReadPendingDirectAsync(tenant, target.Reference)).Single() == next &&
            (await cold.ReadJournalOfAsync(0, id, 0)).Count == 1 && await Position() == fence.Position,
            "a live update cannot consume the encoded header reserve");
        Check((await Write(target, next, fit, ended: false)).Landed, "the checkpoint can still accept at the projected limit");
        Advance();

        var finish = await Add(target, "finish");
        Check((await Write(target, finish, fit, ended: true)).Landed, "the exact terminal write spends the reserved header bytes");
        var ended = (await cold.ReadFirstRootAsync(tenant, target.Reference))!;
        var terminal = await Bytes(target);
        Check(terminal.Count == contentBudget - 1 && terminal.StateHash == active.StateHash && ended.State == fit &&
            ended is { TerminalRevision: 3, Status: InstanceStatus.Finished } && ended.TypeData == maximalType &&
            ended.Labels.OrderBy(pair => pair.Key).SequenceEqual(maximalLabels.OrderBy(pair => pair.Key)) &&
            (await cold.ReadJournalOfAsync(0, id, 0)).Select(entry => entry.Revision).SequenceEqual([1L, 2L, 3L]),
            "the same saved state and every maximum header field round-trip in the terminal checkpoint");
        Console.WriteLine($"live content {active.Count}, terminal content {terminal.Count}, cap {contentBudget}");
        Advance();
        var legacy = await Admit("legacy");
        Check((await Birth(legacy, fit)).Landed, "the legacy fixture starts from a real qualified checkpoint");
        Advance();
        // Import an older formatted row that occupied the extra byte of reserved headroom.
        // Insert before the closing quote of this fixture's escaped string.
        var legacyId = legacy.Creator.Target.Id;
        await using (var db = create())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var position = fence.Position + 1;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Journal" ("Partition", "Position", "Instance", "Revision", "Entries")
                VALUES (0, {position}, {legacyId}, 2, '["previous format-11 checkpoint"]')
                """);
            Check(await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "State" = left("State", -1) || 'x"', "Revision" = 2, "FoundationWriteRevision" = 2
                WHERE "Id" = {legacyId} AND "Revision" = 1
                """) == 1 && await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Partitions" SET "Position" = {position} WHERE "Id" = 0 AND "Position" = {fence.Position}
                """) == 1, "the predecessor-sized checkpoint is imported with its logical state intact");
            await transaction.CommitAsync();
        }
        Advance();
        var legacyInput = await Add(legacy, "settle");
        var previous = await Bytes(legacy);
        await Reject(() => Write(legacy, legacyInput, overflow + "x", ended: false));
        Check(await Bytes(legacy) == previous && await Position() == fence.Position,
            "a prior row with less reserve cannot grow further");
        Check((await Write(legacy, legacyInput, overflow, ended: false)).Landed,
            "a non-growing write on the exact prior row still settles");
        Advance();
        var legacyEnd = await Add(legacy, "finish");
        Check((await Write(legacy, legacyEnd, overflow, ended: true)).Landed && (await Bytes(legacy)).Count == contentBudget,
            "the predecessor-sized row can finish at the exact physical cap");


        async Task<(string Reference, Message Creator)> Admit(string operation)
        {
            var receipt = await ingress.AdmitAsync(tenant, operation, new("fixture<🌐", "definition"));
            return (receipt.Reference, (await ingress.ReadPendingCreatorAsync(tenant, receipt.Reference))!);
        }

        async Task<Message> Add((string Reference, Message Creator) root, string operation)
        {
            await ingress.AdmitDirectAsync(tenant, root.Reference, operation, "advance");
            return (await ingress.ReadPendingDirectAsync(tenant, root.Reference)).Single();
        }

        ValueTask<Committed> Birth((string Reference, Message Creator) root, string state) =>
            store.CommitFirstRootAsync(tenant, root.Reference, root.Creator, fence, lease, state, 1, "", liveLabels, """["birth"]""");

        async ValueTask<Committed> Write((string Reference, Message Creator) root, Message input, string state, bool ended)
        {
            var current = (await cold.ReadFirstRootAsync(tenant, root.Reference))!;
            return await store.CommitRootTurnAsync(tenant, root.Reference, current, [input], fence, lease,
                ended ? InstanceStatus.Finished : InstanceStatus.Running, state, 1,
                ended ? maximalType : "", ended ? maximalLabels : liveLabels, ended ? """["end"]""" : """["update"]""");
        }

        async Task<Content> Bytes((string Reference, Message Creator) root)
        {
            await using var db = create();
            return await db.Database.SqlQuery<Content>($"""
                SELECT md5("State") AS "StateHash", octet_length("State") + octet_length("FoundationHeader") AS "Count"
                FROM "Instances" WHERE "Id" = {root.Creator.Target.Id}
                """).SingleAsync();
        }

        async Task<long> Position()
        {
            await using var db = create();
            return await db.Partitions.Select(row => row.Position).SingleAsync();
        }

        void Advance() => fence = fence with { Position = fence.Position + 1 };
    }

    private sealed record Content(string StateHash, int Count);

    private static async Task Reject(Func<ValueTask<Committed>> commit)
    {
        try { await commit(); }
        catch (StoreException error) when (error.Failure == Failure.Definite &&
            error.Message == "The encoded PostgreSQL checkpoint exceeds its storage budget.")
        {
            Console.WriteLine("ok the projected terminal header causes a definite refusal");
            return;
        }
        throw new InvalidOperationException("The live write consumed the reserved terminal header.");
    }

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
