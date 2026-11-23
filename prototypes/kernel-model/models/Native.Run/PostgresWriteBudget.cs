using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class PostgresWriteBudget
{
    private const int Lines = 10;
    private const int Samples = 3;
    private const int RoundTripBudget = 16;

    internal static async Task<int> RunAsync(CommitSql writer, bool inBatch, bool requireBudget)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
            throw new InvalidOperationException("Set KERNEL_PREVIEW_POSTGRES to a fresh disposable PostgreSQL database.");
        var settings = new NpgsqlConnectionStringBuilder(connection);
        if (settings.Host != "127.0.0.1" || settings.SslMode != SslMode.Disable ||
            settings.GssEncryptionMode == GssEncryptionMode.Require)
            throw new ArgumentException("The wire meter requires a disposable plaintext loopback PostgreSQL connection.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stop = timeout.Token;
        var direct = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Probe() => new(direct);
        await using (var database = Probe())
            Check(await PostgresFixtureDatabase.IsEmptyAsync(database, stop), "The database must be empty.");
        var observer = new EfStore(Probe, postgres: true);
        await observer.CreateAsync(1);
        var observeAdmission = new PostgresRootAdmission(Probe, TimeSpan.FromHours(1));
        await observeAdmission.InstallAsync(stop);

        var counter = new Counting();
        var wire = Wire.Start("postgres", $"Host=127.0.0.1;Port={settings.Port}");
        settings.Port = wire.LocalPort;
        settings.GssEncryptionMode = GssEncryptionMode.Disable;
        settings.Pooling = true;
        var measured = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(settings.ConnectionString)
            .AddInterceptors(counter, counter.Transactions).Options;
        KernelDb Open() => new(measured);
        var store = new EfStore(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = writer };
        var ingress = await store.OpenPreviewRootIngressAsync(PublicActor.ActorType, TimeSpan.FromHours(1), stop);
        var reads = await store.OpenPreviewRootReadsAsync(64, stop);
        var runtime = new WorkflowRuntime(ingress, reads);
        var verifyIngress = await observer.OpenPreviewRootIngressAsync(PublicActor.ActorType, TimeSpan.FromHours(1), stop);
        var verifyApi = await observer.OpenPreviewRootReadsAsync(64, stop);
        var verifyRuntime = new WorkflowRuntime(verifyIngress, verifyApi);
        var scope = FixedTenantScope.Bind("write-budget");
        var definition = Reference.WriteLines(Lines);
        var registry = new WorkflowRegistry().Add(definition);

        // This command measures on an already-running Embedded owner. Keep its startup cost visible separately.
        var baseline = Read();
        var node = await PostgresNativeRootOwner.PreparePartitionAsync(store, $"write-budget-{Environment.ProcessId}",
            0, registry, 1, stop);
        await using var owner = await node.TryActivateAsync(stop) ?? throw new InvalidOperationException("The owner could not acquire the partition.");
        Report("bind, heartbeat and take", Read() - baseline);

        var max = 0L;
        var previousOutput = WriteLine.Out;
        try
        {
            for (var sample = 1; sample <= Samples; sample++)
            {
                using var stdout = new StringWriter();
                WriteLine.Out = stdout;
                baseline = Read();
                var operation = $"budget-{sample}";
                var start = NativeExecutionModel.Start(definition.Id, Modes.Balanced);
                var accepted = await runtime.StartAsync(scope, operation, start, cancellationToken: stop);
                Check(accepted is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "The public start must be admitted.");
                var actor = accepted.Actor!.Value;
                var admitted = Read();
                await owner.WokenAsync(null, stop);
                EfStore.RootImage? checkpoint = null;
                for (var pass = 0; pass < 8 && checkpoint is null; pass++)
                {
                    await owner.RunAsync(stop);
                    checkpoint = await observer.ReadFirstRootAsync(scope.Tenant, actor.Id, stop);
                }
                var final = Read();
                var total = final - baseline;
                Report($"sample {sample} public admission", admitted - baseline);
                Report($"sample {sample} poll and fenced commit", final - admitted);
                Report($"sample {sample} total", total);

                Check(total.Connections == 0, "The sample must reuse warmed PostgreSQL connections.");
                Check(total.Trips > 0 && total.Groups > 0 && total.Completions > 0,
                    "The meter must observe real requests and at least one completed transaction.");
                if (checkpoint is not { Revision: 1, TerminalRevision: 1, TerminalAt: > 0, Status: InstanceStatus.Finished })
                    throw new InvalidOperationException("Balanced must complete this workflow at a single checkpoint revision.");
                Check(await verifyApi.GetAsync(scope, actor, stop) is { Header.Status: Contract.ActorStatus.Finished, Header.EndedAt: > 0 },
                    "The public read must expose an ended root with its terminal time.");
                await VerifyAtRest(checkpoint.Actor.Id);
                Check(stdout.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                    .SequenceEqual(Enumerable.Range(1, Lines).Select(line => $"line {line}")),
                    "The ten Native WriteLines must execute once in authored order.");
                var journal = await verifyApi.TailAsync(scope, actor, cancellationToken: stop);
                var entries = journal.Items.Select(PostgresPublicPreview.Decode).ToArray();
                var writes = entries.Where(entry => entry.StartsWith("c:", StringComparison.Ordinal) && entry.Contains(":write-", StringComparison.Ordinal))
                    .Select(entry => entry[(entry.LastIndexOf(':') + 1)..]);
                Check(journal.Items.All(entry => entry.Revision == 1) &&
                    writes.SequenceEqual(Enumerable.Range(1, Lines).Select(line => $"write-{line}")) &&
                    entries.Count(entry => entry == "end") == 1 && journal.Next is { } bookmark &&
                    (await verifyApi.TailAsync(scope, actor, bookmark, stop)).Items.Count == 0,
                    "The public journal must preserve the ordered completions and one end in revision 1.");
                var replay = await verifyRuntime.StartAsync(scope, operation, start, cancellationToken: stop);
                var afterReplay = await observer.ReadFirstRootAsync(scope.Tenant, actor.Id, stop);
                Check(replay is { Outcome: Contract.AdmissionOutcome.Duplicate } && replay.Actor == actor &&
                    afterReplay is not null && afterReplay.Actor == checkpoint.Actor && afterReplay.Revision == checkpoint.Revision &&
                    afterReplay.State == checkpoint.State && afterReplay.Status == checkpoint.Status &&
                    afterReplay.TerminalRevision == checkpoint.TerminalRevision && afterReplay.TerminalAt == checkpoint.TerminalAt &&
                    (await verifyApi.TailAsync(scope, actor, journal.Next, stop)).Items.Count == 0,
                    "A repeat of the start must retain the same complete checkpoint and add no journal entry.");
                await VerifyAtRest(checkpoint.Actor.Id);
                max = Math.Max(max, total.Trips);
            }
        }
        finally
        {
            WriteLine.Out = previousOutput;
        }

        await VerifySubscriptionGuard(((Worker)owner).Fences.Single());

        var meetsBudget = max <= RoundTripBudget;
        var sql = writer == CommitSql.Owned ? "owned SQL" : "EF";
        Console.WriteLine($"PostgreSQL {sql} ({(inBatch ? "in-batch" : "checked")} fence), {Samples} Balanced public starts: maximum {max} wire round trips, budget {RoundTripBudget} ({(meetsBudget ? "met" : "missed")}).");
        return requireBudget && !meetsBudget ? 1 : 0;

        async Task VerifySubscriptionGuard(PartitionFence fence)
        {
            var ready = await verifyRuntime.StartAsync(scope, "budget-guard", NativeExecutionModel.Start(definition.Id, Modes.Balanced),
                cancellationToken: stop);
            if (ready is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } actor } ||
                await observeAdmission.ReadPendingCreatorAsync(scope.Tenant, actor.Id, stop) is not { Start: { } spec } creator)
                throw new InvalidOperationException("The subscription control requires an admitted creator.");
            var header = new FoundationHeaderChange(spec.Definition, new Dictionary<string, string>());
            var row = new InstanceRow(creator.Target.Id, spec.Model, spec.Definition, InstanceStatus.Finished, "null", 1, null,
                History: spec.History, Creation: 1, StartIntent: CreationProtocol.Fingerprint(spec, creator.Kind, creator.Payload))
            {
                FoundationDispatchHeader = new(scope.Tenant, actor.Id, header)
            };
            var turn = new TurnCommit(row.Id, row, null, [creator.InboxKey], [], [], [], [], """["end"]""", Unsubscribe: true)
            {
                FoundationHeader = header
            };
            var batch = new CommitBatch(fence.Partition, fence.Epoch, fence.Position, TimeSpan.FromSeconds(10), false, [turn], [], []);
            await using var database = Probe();
            var wait = new SubscriptionEntity { Instance = row.Id, Key = "block-first-finish", Wait = "wait", Partition = fence.Partition };
            database.Subscriptions.Add(wait);
            await database.SaveChangesAsync(stop);
            Check(!(await store.CommitAsync(batch, stop)).Landed &&
                await observeAdmission.ReadPendingCreatorAsync(scope.Tenant, actor.Id, stop) == creator &&
                await observer.ReadFirstRootAsync(scope.Tenant, actor.Id, stop) is null &&
                await database.Subscriptions.AnyAsync(subscription => subscription.Instance == row.Id, stop) &&
                !await database.Journal.AnyAsync(entry => entry.Instance == row.Id, stop) &&
                await database.Partitions.Where(partition => partition.Id == fence.Partition)
                    .Select(partition => partition.Position).SingleAsync(stop) == fence.Position,
                "A live subscription must refuse first completion without consuming its creator, wait or journal position.");
            database.Subscriptions.Remove(wait);
            await database.SaveChangesAsync(stop);
            Check((await store.CommitAsync(batch, stop)).Landed &&
                await observer.ReadFirstRootAsync(scope.Tenant, actor.Id, stop) is { Status: InstanceStatus.Finished, TerminalRevision: 1 },
                "The same pending creator must finish after the blocking wait is removed.");
            await VerifyAtRest(row.Id);
            Console.WriteLine("Subscription guard: the live wait refused terminal birth, and the same creator finished after removal.");
        }

        async Task VerifyAtRest(string id)
        {
            await using var db = Probe();
            Check(await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM "Instances" i JOIN "Journal" j ON j."Instance" = i."Id"
                        AND j."Partition" = i."Partition" AND j.xmin = i.xmin
                    WHERE i."Id" = {id} AND j."Revision" = 1
                        AND (SELECT count(*) FROM "Journal" history WHERE history."Instance" = i."Id") = 1
                        AND NOT EXISTS (SELECT 1 FROM "Messages" m WHERE m."Target" = i."Id")
                ) AS "Value"
                """).SingleAsync(stop), "The exact actor must have one journal row sharing the checkpoint transaction and no inbox work.");
        }

        Measurement Read() => new(wire.Trips, wire.ProtocolRequests, wire.Connections, counter.Commands, counter.Transactions.Ended);
    }

    private readonly record struct Measurement(long Trips, long Groups, long Connections, int Commands, int Completions)
    {
        public static Measurement operator -(Measurement after, Measurement before) => new(after.Trips - before.Trips,
            after.Groups - before.Groups, after.Connections - before.Connections, after.Commands - before.Commands,
            after.Completions - before.Completions);
    }

    private static void Report(string phase, Measurement m) =>
        Console.WriteLine($"{phase}: {m.Trips} wire round trips, {m.Groups} PostgreSQL Sync/Query groups, {m.Connections} new TCP connections, {m.Commands} EF commands, {m.Completions} transaction completions.");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
