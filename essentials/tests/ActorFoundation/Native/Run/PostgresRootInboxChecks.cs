using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresRootInboxChecks
{
    internal static async Task<int> RunAsync(bool owned, bool inBatch, bool operators)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Context(DbContextOptions<KernelDb> configuration) =>
            operators ? KernelDb.ForPostgresRootOperatorReplies(configuration) : new(configuration);
        KernelDb Open() => Context(options);
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch)
            { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var stop = budget.Token;
        string baseSchema;
        await using (var db = Open())
        {
            Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the inbox fixture starts with a fresh database");
            var schema = await db.Database.SqlQueryRaw<string>("""SELECT current_schema() AS "Value" """).SingleAsync(stop);
            using var quoting = new NpgsqlCommandBuilder();
            baseSchema = quoting.QuoteIdentifier(schema);
        }
        var otherSource = "inbox_other_" + Guid.NewGuid().ToString("N");
        var otherIssuer = "inbox_issuer_" + Guid.NewGuid().ToString("N");
        var store = Store();
        await store.CreateAsync(1);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await using (var db = Open())
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync($"""
                    CREATE SCHEMA {otherSource};
                    ALTER TABLE "Messages" SET SCHEMA {otherSource};
                    """, stop);
                var mixed = new NpgsqlConnectionStringBuilder(connection) { SearchPath = $"{baseSchema},{otherSource}" };
                var mixedOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(mixed.ConnectionString).Options;
                try
                {
                    await new PostgresRootAdmission(() => Context(mixedOptions), TimeSpan.FromHours(1)).InstallAsync(stop);
                    throw new InvalidOperationException("Installation accepted a different inbox source namespace.");
                }
                catch (StoreFormatException error) when (error.Message.Contains("relations in the active schema", StringComparison.Ordinal)) { }
                Check(!await db.Database.SqlQuery<bool>($"""
                    SELECT to_regclass({otherSource + ".\"IX_FoundationRootInbox_Pending\""}) IS NOT NULL AS "Value"
                    """).SingleAsync(stop), "installation refuses the split source before adding its index");
            }
            finally
            {
                await Restore($"""
                    ALTER TABLE IF EXISTS {otherSource}."Messages" SET SCHEMA {baseSchema};
                    DROP SCHEMA IF EXISTS {otherSource};
                    """);
            }
        }
        await admission.InstallAsync(stop);
        var blue = EmbeddedTenant.Bind("inbox-blue");
        var red = ContractScopes.Bind("inbox-red");
        var probe = new Probe();
        var read = await store.OpenPreviewRootReadsAsync(1, stop);
        var ingress = operators
            ? await store.OpenSelectedRootIngressAsync(probe.Type, TimeSpan.FromHours(1), (scope, _) => scope == blue, stop,
                (scope, _) => scope == blue)
            : await store.OpenPreviewRootIngressAsync(probe.Type, TimeSpan.FromHours(1), stop);
        var actor = await Birth(blue, "subject");
        var sibling = await Birth(blue, "sibling");
        var foreign = await Birth(red, "foreign");
        var beforeBirth = await read.InboxAsync(blue, sibling, cancellationToken: stop);
        var future = beforeBirth.AsOf + 120_000;
        Check(await Send(blue, sibling, "sibling-input", "work", null, future) is { Outcome: Contract.AdmissionOutcome.Accepted } &&
            (await read.InboxAsync(blue, sibling, cancellationToken: stop)).Items.Count == 0,
            "an admitted direct input stays invisible until its creator commits a checkpoint");

        long firstEpoch;
        Contract.Page<Contract.DeliveryView> first;
        const string kind = "escaped\0kind";
        await using (var owner = await Own("inbox-original"))
        {
            await Drive(owner, async () => await read.GetAsync(blue, actor, stop) is not null &&
                await read.GetAsync(blue, sibling, stop) is not null && await read.GetAsync(red, foreign, stop) is not null);
            firstEpoch = owner.Epoch;
            var databaseTime = (await read.InboxAsync(blue, actor, cancellationToken: stop)).AsOf;
            Check(await Send(blue, actor, "first", kind, "body", 0) is { Outcome: Contract.AdmissionOutcome.Accepted } &&
                await Send(blue, actor, "later", "timer", new string('p', 500_000) + "\0🌐", future) is { Outcome: Contract.AdmissionOutcome.Accepted } &&
                await Send(red, foreign, "foreign-input", "work", null, future) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "the subject and foreign root admit their queued inputs");
            first = await read.InboxAsync(blue, actor, cancellationToken: stop);
            Check(first is { Items: [{ Id: "first", Kind: kind, Due: 0, Sender: null, State: Contract.DeliveryState.Pending, Attempts: 0 }],
                    Next: not null } && first.Items[0].Handle.Length == 65 && first.Items[0].Size > 4 && first.AsOf >= databaseTime,
                "a pending page reports the public id, escaped kind, explicit zero due and database time");
            Check((await read.InboxAsync(red, actor, cancellationToken: stop)).Items.Count == 0 &&
                (await read.InboxAsync(blue, foreign, cancellationToken: stop)).Items.Count == 0 &&
                (await read.InboxAsync(blue, sibling, cancellationToken: stop)).Items is [{ Id: "sibling-input" }] &&
                (await read.InboxAsync(blue, new("unissued"), cancellationToken: stop)) is { Items.Count: 0, AsOf: > 0 },
                "unissued and foreign references return empty pages and sibling queues stay separate");
            var saved = first.Next ?? throw new InvalidOperationException("The first inbox page did not issue a continuation.");
            var altered = (saved.Value[0] == 'A' ? 'B' : 'A') + saved.Value[1..];
            Check(await RejectToken(red, actor, saved) && await RejectToken(blue, sibling, saved) &&
                await RejectToken(blue, actor, new(altered)),
                "a cursor cannot continue a different tenant, actor or altered page");
            var later = await read.InboxAsync(blue, actor, first.Next, stop);
            Check(later is { Items: [{ Id: "later", Kind: "timer", State: Contract.DeliveryState.Pending }], Next: null } &&
                later.Items[0].Due == future && later.Items[0].Size >= 500_000 && later.Items[0].Handle != first.Items[0].Handle,
                "the next page returns future-message metadata and the size of its stored envelope");
            await using var db = Open();
            var physical = (await store.ReadFirstRootAsync(blue.Tenant, actor.Id, stop))!.Actor.Id;
            var rows = await db.Messages.AsNoTracking().Where(row => row.Target == physical && !row.Done)
                .OrderBy(row => row.Seq).ToArrayAsync(stop);
            if (rows.Length != 2) throw new InvalidOperationException("The tied-page fixture requires two stored occurrences.");
            var moved = db.Messages.Where(row => row.Target == physical && row.Id == rows[1].Id);
            Check(await moved.ExecuteUpdateAsync(set => set.SetProperty(row => row.Seq, rows[0].Seq), stop) == 1,
                "the pagination fixture gives both occurrences the same delivery order");
            try
            {
                var tied = await read.InboxAsync(blue, actor, cancellationToken: stop);
                var next = await read.InboxAsync(blue, actor, tied.Next, stop);
                var expected = StringComparer.Ordinal.Compare(rows[0].Id, rows[1].Id) < 0 ? new[] { "first", "later" } : ["later", "first"];
                Check(tied.Next is not null && next.Next is null && tied.Items.Concat(next.Items).Select(row => row.Id).SequenceEqual(expected),
                    "the secondary occurrence key keeps tied positions deterministic and lossless");
            }
            finally { await moved.ExecuteUpdateAsync(set => set.SetProperty(row => row.Seq, rows[1].Seq), stop); }
        }

        var reopened = await Store().OpenPreviewRootReadsAsync(1, stop);
        Check((await reopened.InboxAsync(blue, actor, cancellationToken: stop)).Items[0].Handle == first.Items[0].Handle &&
            (await reopened.InboxAsync(blue, actor, first.Next, stop)).Items is [{ Id: "later" }],
            "a new reader opens the saved page and stable handle from the stored issuer");
        await using (var successor = await Own("inbox-successor"))
        {
            Check(successor.Epoch > firstEpoch, "the real Worker takes a new epoch");
            await Drive(successor, async () => (await reopened.InboxAsync(blue, actor, cancellationToken: stop)).Items is [{ Id: "later" }]);
            await using var db = Open();
            var image = (await store.ReadFirstRootAsync(blue.Tenant, actor.Id, stop))!;
            Check(image.Revision == 2 && await db.Messages.AsNoTracking().AnyAsync(row => row.Target == image.Actor.Id && row.Done, stop) &&
                (await reopened.InboxAsync(blue, actor, first.Next, stop)).Items is [{ Id: "later" }],
                "a committed input is absent from inspection while its deduplication row remains");

            if (operators)
            {
                var selected = (await reopened.GetAsync(blue, actor, stop))!;
                var operation = new Contract.OperatorCommand(selected.Selection, ThenDelete: true, ExpectedTarget: actor);
                var applied = await ingress.SendAsync(blue, new Contract.Target.Ref(actor), "retire", Contract.Kinds.Terminate,
                    JsonSerializer.Serialize(operation), cancellationToken: stop);
                var current = await reopened.InboxAsync(blue, actor, cancellationToken: stop);
                Check(applied is { Outcome: Contract.AdmissionOutcome.Accepted } && current.Next is not null &&
                    (await reopened.InboxAsync(blue, actor, current.Next, stop)).Items is [{ Id: "retire", Kind: Contract.Kinds.Terminate }],
                    "the inbox exposes the admitted operator id and kind without exposing its internal bound selection");
                var futureRow = await db.Messages.AsNoTracking().SingleAsync(row => row.Target == image.Actor.Id && row.Due == future, stop);
                await Drive(successor, async () => await reopened.GetAsync(blue, actor, stop) is null);
                Check((await reopened.InboxAsync(blue, actor, cancellationToken: stop)).Items.Count == 0 &&
                    (await reopened.InboxAsync(blue, actor, current.Next, stop)).Items.Count == 0 &&
                    (await reopened.InboxAsync(blue, sibling, cancellationToken: stop)).Items is [{ Id: "sibling-input" }],
                    "retirement hides the exact root and all its pending occurrences without touching a sibling");

                var stale = db.Messages.Where(row => row.Target == futureRow.Target && row.Id == futureRow.Id);
                try
                {
                    Check(await stale.ExecuteUpdateAsync(set => set.SetProperty(row => row.Done, false)
                        .SetProperty(row => row.Start, futureRow.Start).SetProperty(row => row.Payload, futureRow.Payload), stop) == 1 &&
                        (await reopened.InboxAsync(blue, actor, cancellationToken: stop)).Items.Count == 0,
                        "a physically pending row for a retired root still cannot enter the read model");
                }
                finally
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await using var cleanupDb = Open();
                    await cleanupDb.Messages.Where(row => row.Target == futureRow.Target && row.Id == futureRow.Id)
                        .ExecuteUpdateAsync(set => set.SetProperty(row => row.Done, true)
                            .SetProperty(row => row.Start, (string?)null).SetProperty(row => row.Payload, (string?)null), cleanup.Token);
                }
            }
        }

        await using (var db = Open())
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" RENAME COLUMN "State" TO "UnavailableState" """, stop);
                Check((await reopened.InboxAsync(blue, sibling, cancellationToken: stop)).Items is [{ Id: "sibling-input" }],
                    "a cold inbox read uses bounded metadata with the actor state column unavailable");
            }
            finally
            {
                await Restore("""
                    DO $restore$ BEGIN
                        IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Instances"'::regclass
                            AND attname = 'UnavailableState' AND NOT attisdropped) THEN
                            ALTER TABLE "Instances" RENAME COLUMN "UnavailableState" TO "State";
                        END IF;
                    END $restore$;
                    """);
            }
        }

        await using (var db = Open())
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync($"""
                    CREATE SCHEMA {otherIssuer};
                    CREATE TABLE {otherIssuer}."FoundationRootIssuer" (LIKE "FoundationRootIssuer" INCLUDING ALL);
                    INSERT INTO {otherIssuer}."FoundationRootIssuer" ("Singleton", "Key", "Partitions")
                        VALUES (true, decode(repeat('ab', 32), 'hex'), 1);
                    """, stop);
                var shadow = new NpgsqlConnectionStringBuilder(connection) { SearchPath = $"{otherIssuer},{baseSchema}" };
                var shadowOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(shadow.ConnectionString).Options;
                var mixed = new EfStore(() => Context(shadowOptions), postgres: true);
                await mixed.OpenPreviewRootReadsAsync(1, stop);
                throw new InvalidOperationException("The reader accepted a different issuer namespace.");
            }
            catch (StoreFormatException)
            {
                Console.WriteLine("ok a reader refuses an issuer alone from a different schema");
            }
            finally
            {
                await Restore($"""
                    DROP TABLE IF EXISTS {otherIssuer}."FoundationRootIssuer";
                    DROP SCHEMA IF EXISTS {otherIssuer};
                    """);
            }
        }

        await using (var db = Open())
        {
            var protocol = db.RequiredFormat.MessageProtocol;
            var incompatible = checked(protocol + 100);
            try
            {
                Check(await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "KernelStoreVersions" SET "MessageProtocol" = {incompatible} WHERE "MessageProtocol" = {protocol}
                    """, stop) == 1, "the format fixture advances the expected message protocol");
                await ExpectFormatRefusal("the existing reader refuses a changed format in the page statement");
                await db.Database.ExecuteSqlRawAsync("""
                    ALTER TABLE "Messages" RENAME COLUMN "Kind" TO "OriginalKind";
                    ALTER TABLE "Messages" ADD COLUMN "Kind" integer NOT NULL DEFAULT 0;
                    """, stop);
                await ExpectFormatRefusal("the existing reader refuses the changed format after statement analysis fails");
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var cleanupDb = Open();
                await cleanupDb.Database.ExecuteSqlRawAsync("""
                    DO $restore$ BEGIN
                        IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Messages"'::regclass
                            AND attname = 'OriginalKind' AND NOT attisdropped) THEN
                            ALTER TABLE "Messages" DROP COLUMN IF EXISTS "Kind";
                            ALTER TABLE "Messages" RENAME COLUMN "OriginalKind" TO "Kind";
                        END IF;
                    END $restore$;
                    """, cleanup.Token);
                await cleanupDb.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "KernelStoreVersions" SET "MessageProtocol" = {protocol} WHERE "MessageProtocol" = {incompatible}
                    """, cleanup.Token);
            }
        }
        Check((await reopened.InboxAsync(blue, sibling, cancellationToken: stop)).Items is [{ Id: "sibling-input" }],
            "the restored marker and columns let the original reader serve again");
        return 0;

        async Task Restore(string sql)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var db = Open();
            await db.Database.ExecuteSqlRawAsync(sql, cleanup.Token);
        }

        async Task ExpectFormatRefusal(string description)
        {
            try { await reopened.InboxAsync(blue, sibling, cancellationToken: stop); }
            catch (StoreFormatException) { Console.WriteLine($"ok {description}"); return; }
            throw new InvalidOperationException(description);
        }

        async Task<Contract.ActorRef> Birth(Contract.BoundScope scope, string id)
        {
            var accepted = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "{}")), id,
                Contract.Kinds.Start, cancellationToken: stop);
            return accepted.Actor ?? throw new InvalidOperationException("The fixture birth was refused.");
        }

        ValueTask<Contract.Admission> Send(Contract.BoundScope scope, Contract.ActorRef target, string id, string messageKind, string? payload, long due) =>
            ingress.SendAsync(scope, new Contract.Target.Ref(target), id, messageKind, payload, new(DueAt: due), stop);

        async Task<Worker> Own(string node)
        {
            var ownerStore = Store();
            var roots = (await ownerStore.BindWorkerPartitionAsync(0, stop)).Scope;
            var worker = new Worker(node, 0, ownerStore, new ActorAdapter(new EfStore.RootHeaders(roots), probe));
            var host = new DurableHost([worker]);
            await host.HeartbeatAsync(Defaults.Lease, stop);
            return (Worker?)await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("The fixture could not take ownership.");
        }

        async Task Drive(Worker worker, Func<Task<bool>> finished)
        {
            while (!await finished())
            {
                await worker.WokenAsync(null, stop);
                await worker.RunAsync(stop);
                stop.ThrowIfCancellationRequested();
            }
        }

        async Task<bool> RejectToken(Contract.BoundScope scope, Contract.ActorRef target, Contract.PageToken token)
        {
            try { await read.InboxAsync(scope, target, token, stop); }
            catch (ArgumentException error) when (error.ParamName == "from") { return true; }
            return false;
        }
    }

    internal static async Task RejectRouteAsync(Func<KernelDb> open, Contract.IActorReads reads, Contract.BoundScope scope,
        Contract.ActorRef actor, Message admitted, string rejected, CancellationToken stop)
    {
        var original = admitted.ReplyTo ?? throw new InvalidOperationException("The fixture requires a held request.");
        try
        {
            if (await Replace(original, rejected, stop) != 1)
                throw new InvalidOperationException("The route control did not find the admitted occurrence.");
            try { await reads.InboxAsync(scope, actor, cancellationToken: stop); }
            catch (StoreFormatException)
            {
                Console.WriteLine("ok the inbox rejects an invalid held route without returning its address");
                return;
            }
            throw new InvalidOperationException("The inbox presented an invalid held route as pending.");
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await Replace(rejected, original, cleanup.Token);
        }

        async Task<int> Replace(string expected, string value, CancellationToken token)
        {
            await using var db = open();
            return await db.Messages.Where(row => row.Target == admitted.Target.Id && row.Id == admitted.InboxKey.Id &&
                    row.InboxScope == admitted.InboxKey.Scope && !row.Done && row.ReplyTo == expected)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.ReplyTo, value), token);
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "postgres-inbox-probe";
        public bool Withholds(Contract.ActorHeader header) => false;
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            turn.State = "{}";
            return default;
        }
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }

    private static void Check(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
