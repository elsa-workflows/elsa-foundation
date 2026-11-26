using Elsa.ActorFoundation.Hosting;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class SqliteRootIngressChecks
{
    private const string ActorType = "sqlite-root-ingress";
    private const string Tenant = "ingress";
    private const string Payload = "nul\0slash\\unpaired\uD800end";

    internal static async Task<int> RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        var folder = Directory.CreateTempSubdirectory("sqlite-root-ingress");
        var path = Path.Combine(folder.FullName, "kernel.db");
        var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(new SqliteConnectionStringBuilder
        {
            DataSource = path, ForeignKeys = true, Pooling = false
        }.ToString()).Options;
        KernelDb Open() => KernelDb.ForSqliteRootShipped(options);
        var scope = EmbeddedTenant.Bind(Tenant);

        try
        {
            File.WriteAllBytes(path, []);
            await RefusedAsync<StoreFormatException>(() => SqliteEmbeddedHosting.OpenFoundationAsync(path, ActorType, cancellationToken: token),
                "SQLite ingress requires an installed store");
            await using var db = Open();
            await EfStore.InstallSqliteShippedAsync(db, 2, token);
            var foundation = await SqliteEmbeddedHosting.OpenFoundationAsync(path, ActorType, cancellationToken: token);
            var start = new Contract.Target.Start(new(ActorType, "definition", Payload));
            var aliasPath = Path.Combine(folder.FullName, "KERNEL.DB");
            var alias = File.Exists(aliasPath)
                ? await SqliteEmbeddedHosting.OpenFoundationAsync(aliasPath, ActorType, cancellationToken: token)
                : foundation;
            var starts = await Task.WhenAll(Enumerable.Range(0, 6).Select(index =>
                Task.Run(() => StartAsync("start", index % 2 == 0 ? alias : foundation), token)));
            var root = starts.Single(result => result.Outcome == Contract.AdmissionOutcome.Accepted).Actor!.Value;
            Require(starts.Count(result => result.Outcome == Contract.AdmissionOutcome.Duplicate) == 5 &&
                starts.All(result => result.Actor == root) && FoundationRootCreator.IsGeneratedReference(root.Id),
                "concurrent starts of one operation admit one birth and replay its issued reference");

            var creatorRow = await db.Messages.AsNoTracking().SingleAsync(token);
            var creator = PostgresRootAdmission.DecodeCreator(creatorRow);
            Require(creator is { Id: "start", Due: 0, TargetCreation: 1 } && creator.Start?.Input == Payload
                && creatorRow.Payload is null && creatorRow.KeepUntil is null,
                "the creator preserves explicit due zero and exactly round-trips otherwise unrepresentable input code units");

            var target = new Contract.Target.Ref(root);
            var sends = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => SendAsync("send"), token)));
            Require(sends.Count(result => result.Outcome == Contract.AdmissionOutcome.Accepted) == 1 &&
                sends.Count(result => result.Outcome == Contract.AdmissionOutcome.Duplicate) == 5 &&
                sends.All(result => result.Actor == root),
                "a pending creator accepts exact-reference sends and records one occurrence for duplicate concurrent sends");
            var initial = await db.Messages.AsNoTracking().Where(row => row.InboxScope == InboxKey.FoundationOccurrenceScope).SingleAsync(token);
            var decoded = PostgresRootAdmission.DecodeDirect(initial, root.Id);
            Require(decoded is { Id: "send", Kind: "advance", Due: 0, TargetCreation: 1 } &&
                decoded.Payload == Payload && initial.KeepUntil > 0 && initial.Seq > creatorRow.Seq,
                "the direct occurrence preserves the envelope, retention bound, and creator-before-input order");

            var admission = new SqliteRootAdmission(Open, TimeSpan.FromHours(1), new());
            Require(await admission.SendAsync("another-tenant", root, "send", "advance", null, null, null, token) is
                { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch },
                "a tenant mismatch discloses no receipt for an otherwise valid reference");

            await db.Database.ExecuteSqlRawAsync("""UPDATE "FoundationDirectReceipts" SET "Until" = 0""", token);
            Require(await SendAsync("send") is { Outcome: Contract.AdmissionOutcome.Accepted },
                "an expired direct receipt admits a new occurrence");
            var directKeys = await db.Messages.Where(row => row.InboxScope == InboxKey.FoundationOccurrenceScope)
                .OrderBy(row => row.Seq).Select(row => row.Id).ToListAsync(token);
            Require(directKeys.Count == 2 && directKeys[0] == initial.Id && directKeys[1] != initial.Id,
                "the new occurrence has a distinct stored identity");

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Seq" = {long.MaxValue} WHERE "Id" = {creatorRow.Id} AND "InboxScope" = 0
                """, token);
            await RefusedAsync<SqliteException>(() => StartAsync("retry"),
                "a sequence outside the strict integer domain fails enqueue and rolls back start admission");
            var failedDigest = FoundationRootCreator.OperationDigest("retry");
            Require(!await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "FoundationRootReceipts" WHERE "Operation" = {failedDigest})
                    OR (SELECT count(*) FROM "FoundationRootBirths") <> 1 AS "Value"
                """).SingleAsync(token), "a failed creator leaves neither its receipt nor its birth");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Seq" = {creatorRow.Seq} WHERE "Id" = {creatorRow.Id} AND "InboxScope" = 0
                """, token);
            Require(await StartAsync("retry") is { Outcome: Contract.AdmissionOutcome.Accepted },
                "a retry after the rolled back enqueue admits a new root");

            await db.Database.ExecuteSqlRawAsync("""UPDATE "FoundationRootReceipts" SET "Until" = 0""", token);
            Require(await StartAsync("start") is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } later } && later != root,
                "an expired start receipt issues a new root reference");

            const string header = """{"Model":"sqlite-root-ingress","Labels":[]}""";
            await using (var bind = await db.Database.BeginTransactionAsync(token))
            {
                db.Instances.Add(new InstanceEntity
                {
                    Id = creatorRow.Target, Partition = creatorRow.Partition, Model = ActorType, Definition = "definition",
                    Status = InstanceStatus.Running, State = "null", Revision = 1, NextJobGeneration = 1
                });
                await db.SaveChangesAsync(token);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Instances" SET "FoundationReference" = {root.Id}, "FoundationTenant" = {Tenant},
                        "FoundationHeader" = {header} WHERE "Id" = {creatorRow.Target}
                    """, token);
                await db.Messages.Where(row => row.Id == creatorRow.Id && row.InboxScope == 0).ExecuteDeleteAsync(token);
                await bind.CommitAsync(token);
            }
            Require(await SendAsync("running") is { Outcome: Contract.AdmissionOutcome.Accepted },
                "a qualified running checkpoint continues to receive exact-reference sends after the creator is consumed");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "Status" = {(int)InstanceStatus.Finished}, "Revision" = 2, "FoundationWriteRevision" = 2,
                    "FoundationTerminalRevision" = 2, "FoundationTerminalAt" = 100 WHERE "Id" = {creatorRow.Target}
                """, token);
            Require(await SendAsync("running") is { Outcome: Contract.AdmissionOutcome.Duplicate }
                    && await SendAsync("finished") is { Outcome: Contract.AdmissionOutcome.Refused, Reason: null },
                "a finished checkpoint replays a live receipt and refuses a new send");

            await RefusedAsync<NotSupportedException>(() => foundation.RequestAsync(scope, target, "held", "advance", null, null, new(null, 1), token).AsTask(),
                "staged SQLite ingress requires the later held-reply slice before it accepts requests");
            var projector = await db.Database.SqlQueryRaw<string>("""
                SELECT sql AS "Value" FROM main.sqlite_schema WHERE name = 'kernel_project_root_actor_list'
                """).SingleAsync(token);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER kernel_project_root_actor_list", token);
            await RefusedAsync<StoreFormatException>(() => StartAsync("after-ddl"),
                "an already open ingress rechecks the definitions after reacquiring the file");
            await RefusedAsync<StoreFormatException>(() => SqliteEmbeddedHosting.OpenFoundationAsync(path, ActorType, cancellationToken: token),
                "opening ingress verifies the exact installed definitions");
            await db.Database.ExecuteSqlRawAsync(projector, token);

            var marker = StoreDefinitionsRevision.Seed(db)!;
            await db.StoreVersions.Where(row => row.Id == marker.Id).ExecuteDeleteAsync(token);
            await RefusedAsync<StoreFormatException>(() => SendAsync("after-marker-change"),
                "an already open ingress rechecks the store stamp in its write transaction");
            return 0;

            Task<Contract.Admission> StartAsync(string id, Contract.IIngress? route = null) => (route ?? foundation).SendAsync(scope, start, id, Contract.Kinds.Start,
                options: new(DueAt: 0), cancellationToken: token).AsTask();

            Task<Contract.Admission> SendAsync(string id) => foundation.SendAsync(scope, target, id, "advance", Payload,
                options: new(DueAt: 0), cancellationToken: token).AsTask();
        }
        finally { folder.Delete(recursive: true); }
    }

    private static async Task RefusedAsync<T>(Func<Task> action, string witness) where T : Exception
    {
        try { await action(); }
        catch (T)
        {
            Require(true, witness);
            return;
        }
        throw new InvalidOperationException(witness);
    }

    private static void Require(bool ok, string witness)
    {
        if (!ok) throw new InvalidOperationException(witness);
        Console.WriteLine($"ok {witness}");
    }
}
