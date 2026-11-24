using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.Ef.Conformance;

internal static class RootCheckpointLimitChecks
{
    private const int Budget = 16 * 1024 * 1024 - 24 * 1024;

    internal static async Task Run(Func<KernelDb> create, EfStore store, EfStore cold, bool mixedText, bool unrepresentableText)
    {
        const string tenant = "checkpoint-limit";
        var typeData = mixedText ? "<storage>" : "<storage>🌐";
        var state = StoredState.Family + (mixedText ? "sentinel/é" : "sentinel");
        var labels = new Dictionary<string, string> { ["escaped"] = mixedText ? "\u0001" : "\u0001é" };
        var lease = TimeSpan.FromMinutes(2);
        var ingress = new PostgresRootAdmission(create, lease);
        await ingress.InstallAsync();
        var take = (await store.TakeAsync(0, "checkpoint-limit", lease))!;
        PartitionFence fence = new(0, take.Epoch, take.Position);

        var calibration = await Admit("calibration");
        Check((await First(calibration, state, fence)).Landed, "the calibration checkpoint lands");
        var calibrationBytes = await PhysicalBytes(calibration.Creator.Target.Id);
        var padding = new string('x', Budget - calibrationBytes);
        string fit = state + padding;
        string overflow = fit + "x";
        fence = fence with { Position = fence.Position + 1 };

        var target = await Admit("target");
        var id = target.Creator.Target.Id;
        if (unrepresentableText)
        {
            // EUC_JP cannot store this scalar. Passing the cheap size bound forces
            // the provider measurement before either transactional fence runs.
            var unrepresentable = fit + "🌐";
            Check(!(await First(target, unrepresentable, fence with { Epoch = fence.Epoch - 1 })).Landed,
                "a stale fence wins over the PostgreSQL measurement conversion error");
            try
            {
                await First(target, unrepresentable, fence);
                throw new InvalidOperationException("The current owner did not report the conversion error.");
            }
            catch (StoreException error) when (error is
                { Failure: Failure.Definite, InnerException: PostgresException { SqlState: "22P05" } })
            {
                Console.WriteLine("ok a current owner reports the conversion error as a definite failure");
            }
        }
        Check(!(await First(target, overflow, fence with { Epoch = fence.Epoch - 1 })).Landed,
            "a stale fence wins over the oversized first checkpoint");
        await Reject(() => First(target, overflow, fence));
        Check(await cold.ReadFirstRootAsync(tenant, target.Reference) is null &&
              await ingress.ReadPendingCreatorAsync(tenant, target.Reference) == target.Creator &&
              (await cold.ReadJournalOfAsync(0, id, 0)).Count == 0 && await Position() == fence.Position,
            "one byte over on birth preserves the creator, checkpoint, journal and fence");

        Check((await First(target, fit, fence)).Landed && await PhysicalBytes(id) == Budget,
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

        Check((await End(fit, fence)).Landed && await PhysicalBytes(id) == Budget &&
              (await cold.ReadFirstRootAsync(tenant, target.Reference)) is { Revision: 2, TerminalRevision: 2 } ended &&
              ended.State == fit && (await ingress.ReadPendingDirectAsync(tenant, target.Reference)).Count == 0 &&
              (await cold.ReadJournalOfAsync(0, id, 0)).Select(row => row.Revision).SequenceEqual([1L, 2L]) &&
              await Position() == fence.Position + 1,
            "the exact terminal write settles its input and advances the journal and fence once");
        fence = fence with { Position = fence.Position + 1 };
        var legacy = await Admit("legacy");
        Check((await First(legacy, fit, fence)).Landed, "the predecessor fixture starts from a real qualified checkpoint");
        fence = fence with { Position = fence.Position + 1 };
        // Recreate what the predecessor wrote at one byte over today's count. Its check
        // omitted the state family so this size was still inside its logical budget.
        await using (var db = create())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Journal" ("Partition", "Position", "Instance", "Revision", "Entries")
                VALUES (0, {fence.Position + 1}, {legacy.Creator.Target.Id}, 2, '["legacy checkpoint"]')
                """);
            Check(await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "State" = "State" || 'x', "Revision" = 2, "FoundationWriteRevision" = 2
                WHERE "Id" = {legacy.Creator.Target.Id} AND "Revision" = 1
                """) == 1, "the predecessor-shaped revision is installed");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Partitions" SET "Position" = {fence.Position + 1} WHERE "Id" = 0 AND "Position" = {fence.Position}
                """);
            await transaction.CommitAsync();
        }
        fence = fence with { Position = fence.Position + 1 };
        var legacyRow = (await cold.ReadFirstRootAsync(tenant, legacy.Reference))!;
        Check(legacyRow is { Revision: 2, State: not null } && legacyRow.State == overflow &&
              await PhysicalBytes(legacy.Creator.Target.Id) == Budget + 1, "the old row remains readable at budget plus one");
        await ingress.AdmitDirectAsync(tenant, legacy.Reference, "complete", "finish");
        var legacyInput = (await ingress.ReadPendingDirectAsync(tenant, legacy.Reference)).Single();
        ValueTask<Committed> EndLegacy(string value) => store.CommitRootTurnAsync(tenant, legacy.Reference, legacyRow, [legacyInput],
            fence, lease, InstanceStatus.Finished, value, 1, typeData, labels, """["finish"]""");
        await Reject(() => EndLegacy(overflow + "x"));
        Check((await cold.ReadFirstRootAsync(tenant, legacy.Reference))?.Revision == 2 && await Position() == fence.Position,
            "a predecessor row cannot grow beyond its existing size");
        Check((await EndLegacy(overflow)).Landed &&
              (await cold.ReadFirstRootAsync(tenant, legacy.Reference)) is { Revision: 3, TerminalRevision: 3 } legacyEnd &&
              legacyEnd.State == overflow && await PhysicalBytes(legacy.Creator.Target.Id) == Budget + 1 &&
              (await ingress.ReadPendingDirectAsync(tenant, legacy.Reference)).Count == 0,
            "a non-growing completion can settle a predecessor row over today's budget");


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

    internal static async Task RunJobs(Func<KernelDb> create, EfStore store, EfStore cold)
    {
        const string tenant = "checkpoint-jobs";
        const string model = "job-model";
        const string definition = "job-workflow";
        const string jobKey = "checkpoint-job";
        var lease = TimeSpan.FromMinutes(2);
        var header = new FoundationHeaderChange(definition, new Dictionary<string, string>());
        var registry = JobRegistry.Write([new(jobKey, 0)]);
        var ingress = new PostgresRootAdmission(create, lease);
        await ingress.InstallAsync();
        await store.EnableRootJobsAsync(CancellationToken.None);
        store.BindWorkerNode();
        var take = (await store.TakeAsync(0, "checkpoint-job-limit", lease))!;
        var fence = new PartitionFence(0, take.Epoch, take.Position);
        var calibration = await Admit("calibration");
        Check((await Birth(calibration, "x")).Landed, "a qualified birth commits its generated job and registry");
        var firstBytes = await Read(calibration.Creator.Target.Id);
        Check(firstBytes.Jobs > 1, "the physical calibration includes a nonempty job registry");
        fence = fence with { Position = fence.Position + 1 };

        var fit = new string('x', Budget - firstBytes.State - firstBytes.Header - firstBytes.Jobs + 1);
        var overflow = fit + "x";
        var target = await Admit("target");
        var id = target.Creator.Target.Id;
        await Reject(() => Birth(target, overflow));
        await using (var db = create())
        {
            Check(await ingress.ReadPendingCreatorAsync(tenant, target.Reference) == target.Creator &&
                await cold.ReadFirstRootAsync(tenant, target.Reference) is null &&
                !await db.Messages.AnyAsync(message => message.Target == id && message.Kind == Kinds.Job),
                "the job registry tips the birth over the cap without publishing its ticket");
        }
        Check((await Birth(target, fit)).Landed, "the birth with the registered job lands");
        var started = await Read(id);
        Check(started.State + started.Header + started.Jobs == Budget, "the job birth lands at the exact cap");
        fence = fence with { Position = fence.Position + 1 };

        await ingress.AdmitDirectAsync(tenant, target.Reference, "grow", "grow");
        var direct = (await ingress.ReadPendingDirectAsync(tenant, target.Reference)).Single();
        var checkpoint = (await store.LoadAsync([id])).Single();
        var grown = checkpoint with { Revision = 2, State = overflow };
        TurnCommit Growth(InstanceRow row) => new(id, row, 1, [], [direct.InboxKey], [], [], [], """["grow"]""");
        await Reject(() => Submit(Growth(grown)));
        Check((await cold.ReadFirstRootAsync(tenant, target.Reference)) is { Revision: 1, Jobs: not null } &&
            (await ingress.ReadPendingDirectAsync(tenant, target.Reference)).Single() == direct,
            "the job registry tips a running update over the cap and preserves its input");
        Check((await Submit(Growth(grown with { State = fit }))).Landed, "the exact running update lands with the job still registered");
        fence = fence with { Position = fence.Position + 1 };

        var original = (await store.PollAsync(0, fence.Epoch, TimeSpan.Zero, 10)).Messages
            .Single(input => input.Message.Target.Id == id && input.Message.Kind == Kinds.Job).Message.AsKernelTicket();
        var result = (original with { Kind = Kinds.JobFinished, Payload = Json.Write(new JobFinished(jobKey, Json.Write("done"), null)) })
            .AsKernelTicket();
        var terminal = checkpoint with { Status = InstanceStatus.Finished, State = overflow, Revision = 3, Jobs = null };
        TurnCommit complete = new(id, terminal, 2, [original.InboxKey], [], [], [], [], """["end"]""", Unsubscribe: true)
            { FoundationRootJobCompletion = new(original, result) };
        Check((await Submit(complete)).Landed, "the completed job settles in its ending commit");
        var ended = await Read(id);
        Check(ended.Jobs == 0 && ended.State + ended.Header == Budget - started.Jobs + 1 &&
            (await cold.ReadFirstRootAsync(tenant, target.Reference)) is { State: not null, TerminalRevision: 3 } final &&
            final.State == overflow && (await cold.ReadJournalOfAsync(0, id, 0)).Count == 3,
            "clearing the real ticket and registry permits the previously oversized state");

        async Task<(string Reference, Message Creator)> Admit(string key)
        {
            var receipt = await ingress.AdmitAsync(tenant, key, new(model, definition));
            return (receipt.Reference, (await ingress.ReadPendingCreatorAsync(tenant, receipt.Reference))!);
        }

        ValueTask<Committed> Birth((string Reference, Message Creator) targetRoot, string state)
        {
            var actor = targetRoot.Creator.Target.Id;
            var row = new InstanceRow(actor, model, definition, InstanceStatus.Running, state, 1, null,
                Jobs: registry, History: targetRoot.Creator.Start!.History, Creation: 1,
                StartIntent: CreationProtocol.Fingerprint(targetRoot.Creator.Start, targetRoot.Creator.Kind, targetRoot.Creator.Payload))
            {
                FoundationDispatchHeader = new(tenant, targetRoot.Reference, header),
                NextJobGeneration = 1
            };
            var key = InboxKey.Ticket(actor, jobKey, 1, 0);
            var ticket = new Message(key.Id, new(actor), Kinds.Job, Json.Write(new JobTicket(model, jobKey, null)),
                actor, TargetCreation: 1, SenderCreation: 1).WithInboxScope(key.Scope).AsKernelTicket();
            return Submit(new(actor, row, null, [targetRoot.Creator.InboxKey], [], [ticket], [], [], """["birth"]""")
                { FoundationHeader = header });
        }

        ValueTask<Committed> Submit(TurnCommit turn) =>
            store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, lease, false, [turn], [], []));

        async Task<JobCheckpointBytes> Read(string actor)
        {
            await using var db = create();
            return await db.Database.SqlQuery<JobCheckpointBytes>($"""
                SELECT octet_length("State") AS "State", octet_length("FoundationHeader") AS "Header",
                    coalesce(octet_length("Jobs"), 0) AS "Jobs"
                FROM "Instances" WHERE "Id" = {actor}
                """).SingleAsync();
        }
    }

    private sealed record JobCheckpointBytes(int State, int Header, int Jobs);

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
