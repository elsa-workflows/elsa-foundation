using Kernel.EntityFramework;
using Kernel.Store.Conformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kernel.Ef.Conformance;

internal static class JobGenerationFormatChecks
{
    private static Task<string> ReadGuard(KernelDb db) => db.Database.SqlQueryRaw<string>(db.Database.IsSqlite()
        ? "SELECT sql AS Value FROM sqlite_schema WHERE type='trigger' AND name='kernel_job_generation'"
        : "SELECT ACTION_STATEMENT AS Value FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE() AND TRIGGER_NAME='kernel_job_generation'").SingleAsync();

    private static Task InstallInertGuard(KernelDb db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite()
        ? "CREATE TRIGGER kernel_job_generation BEFORE UPDATE ON Instances BEGIN SELECT 1; END"
        : "CREATE TRIGGER kernel_job_generation BEFORE UPDATE ON Instances FOR EACH ROW SET NEW.Revision = NEW.Revision");

    internal static async Task Run(Func<KernelDb> create, ProvidedStore fixture, Func<EfStore> restart)
    {
        const string legacy = "0/legacy", actor = "0/job-owner";
        var store = (EfStore)fixture.Store;
        await using (var db = create())
        {
            db.Instances.Add(new InstanceEntity { Id = legacy, State = "{}", Revision = 1 });
            await db.SaveChangesAsync();
        }
        await store.AddAsync(Make.M("old-input", 0));
        await using (var db = create())
        {
            // Recreate the preceding schema-2 table definitions and keep the seeded rows.
            var guardDrop = db.Database.IsNpgsql()
                ? """DROP TRIGGER kernel_job_generation ON "Instances" """ : "DROP TRIGGER kernel_job_generation";
            await db.Database.ExecuteSqlRawAsync(guardDrop);
            if (db.Database.IsSqlite() || db.Database.ProviderName == "MySql.EntityFrameworkCore")
            {
                await InstallInertGuard(db);
                var inert = await ReadGuard(db);
                await VersionTakeChecks.Refuses(() => restart().CreateAsync(2), "trigger has an incompatible definition", "startup on an inert guard");
                Context.Equal(inert, await ReadGuard(db), "startup preserved the incompatible trigger");
                await db.Database.ExecuteSqlRawAsync(guardDrop);
            }
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlRawAsync("""
                    DECLARE @name sysname = (SELECT name FROM sys.default_constraints
                       WHERE parent_object_id = OBJECT_ID('Messages') AND COL_NAME(parent_object_id, parent_column_id) = 'JobGeneration');
                    IF @name IS NOT NULL BEGIN
                        DECLARE @sql nvarchar(500) = N'ALTER TABLE Messages DROP CONSTRAINT ' + QUOTENAME(@name);
                        EXEC(@sql);
                    END;
                    """);
            var sql = db.GetService<ISqlGenerationHelper>();
            foreach (var (table, column) in new[] { ("Instances", "NextJobGeneration"), ("Messages", "JobGeneration") })
                await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {sql.DelimitIdentifier(table)} DROP COLUMN {sql.DelimitIdentifier(column)}");
            await db.StoreVersions.ExecuteUpdateAsync(set => set.SetProperty(x => x.StoreSchema, 2).SetProperty(x => x.MessageProtocol, 1));
            db.Nodes.Add(new NodeEntity { Id = "old-owner", LeaseUntil = long.MaxValue,
                ReadableStoreSchema = "2", ReadableMessageProtocol = "1", ReadableJournalCodec = "1" });
            await db.SaveChangesAsync();
        }
        await VersionTakeChecks.Refuses(() => restart().CreateAsync(2), "schema=2, message protocol=1", "old store before migration");
        await VersionTakeChecks.Refuses(() => store.UpgradeJobGenerationFormatAsync(), "Live node 'old-owner'", "incompatible live node");
        await using (var db = create())
        {
            await db.Nodes.ExecuteUpdateAsync(set => set.SetProperty(node => node.LeaseUntil, 0));
            if (db.Database.ProviderName == "MySql.EntityFrameworkCore")
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Instances ADD NextJobGeneration BIGINT GENERATED ALWAYS AS (Revision) STORED");
                await VersionTakeChecks.Refuses(() => store.UpgradeJobGenerationFormatAsync(), "Instances.NextJobGeneration column has an incompatible shape", "generated counter column");
                Context.Equal(2, (await db.StoreVersions.SingleAsync()).StoreSchema, "generated column refused before publishing");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Instances DROP COLUMN NextJobGeneration");
                // Simulate process loss after the first MySQL DDL auto-commit.
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Instances ADD NextJobGeneration BIGINT NULL");
                if (store.FenceInBatch)
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE Messages ADD JobGeneration BIGINT NOT NULL DEFAULT 0");
            }
            if (db.Database.IsSqlite() || db.Database.ProviderName == "MySql.EntityFrameworkCore")
            {
                await InstallInertGuard(db);
                var inert = await ReadGuard(db);
                await VersionTakeChecks.Refuses(() => store.UpgradeJobGenerationFormatAsync(), "trigger has an incompatible definition", "migration on an inert guard");
                Context.Equal(inert, await ReadGuard(db), "migration preserved the incompatible trigger");
                Context.Equal(2, (await db.StoreVersions.AsNoTracking().SingleAsync()).StoreSchema, "guard refused before publishing");
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER kernel_job_generation");
            }
        }
        Context.That(await store.UpgradeJobGenerationFormatAsync(), "a stopped schema-2, protocol-1 store upgrades");
        Context.That(!await store.UpgradeJobGenerationFormatAsync(), "the completed migration is idempotent");
        await using (var db = create())
            if (db.Database.IsNpgsql())
                Context.That(await db.Database.SqlQueryRaw<bool>("""
                    SELECT to_regclass('"FoundationActorList"') IS NULL AS "Value"
                    """).SingleAsync(), "migration leaves an ordinary store without a generated-root projection");
        var cold = restart();
        await cold.CreateAsync(2);
        Context.That((await cold.LoadAsync([legacy])).Single() is { NextJobGeneration: null, State: "{}" }, "pre-counter row");
        Context.Equal(0L, (await cold.PollAsync(0, 1, TimeSpan.Zero, 20)).Messages.Single().Message.JobGeneration, "legacy inbox row");
        Console.WriteLine("ok old-format refusal, live-owner guard, coordinated migration, restart and legacy null/zero defaults");

        var c = new Context(fixture);
        var lease = await c.Take(0);
        var prior = Make.Row(actor, 1) with { Creation = 1, NextJobGeneration = 18 };
        await using var worker = new Worker("compose", 0, new InMemoryStore(1, () => 1000), new WorkflowInstance([]));
        await using var activation = (await new DurableHost([worker]).ActivateAsync(0))!;
        var input = new TurnInput(worker, new Slot { Row = prior, Loaded = true }, actor, [], 0, null, 0);
        var turn = new DurableTurn(input, foundation: true);
        turn.StartJob("reused", 37);
        var result = turn.Seal(turn.Outcome(InstanceStatus.Running), prior with { Revision = 2 }, null, "[]");
        var issued = result.Sent.Single();
        Context.Equal(18L, issued.JobGeneration, "Foundation ticket");
        Context.Equal(19L, result.Row!.NextJobGeneration, "Foundation counter");
        try
        {
            await c.Commit(lease, 0, [Make.Turn(actor, result.Row, 1, sent: [issued])]);
            throw new ConformanceException("a qualified Foundation turn was admitted by the legacy store");
        }
        catch (StoreException failure) when (failure.Failure == Failure.Definite && failure.Message.Contains("no qualified creation identity", StringComparison.Ordinal)) { }
        Console.WriteLine("ok actual qualified Foundation StartJob remains outside the legacy writer");

        // Only the shape of the two stored fields is being qualified. The ordinary carrier
        // exercises the real fenced writer without granting it a qualified creation or job.
        var row = Make.Row(actor, 1) with { NextJobGeneration = result.Row.NextJobGeneration };
        var carrier = Make.M("generation-carrier", 0) with { JobGeneration = issued.JobGeneration };
        await c.Lands(() => c.Commit(lease, 0, [Make.Turn(actor, row, sent: [carrier])]));
        cold = restart();
        Context.Equal(19L, (await cold.LoadAsync([actor])).Single().NextJobGeneration, "inserted counter after restart");
        var mail = (await cold.PollAsync(0, lease.Epoch, TimeSpan.Zero, 20)).Messages.Single(m => m.Message.Id == carrier.Id);
        Context.Equal(18L, mail.Message.JobGeneration, "committed envelope after restart");
        await c.Lands(() => c.Commit(0, lease.Epoch, 1, [Make.Turn(actor, row with { Revision = 2, NextJobGeneration = 20 }, 1)]));
        await c.Refused(() => c.Commit(0, lease.Epoch, 2, [Make.Turn(actor, row with { Revision = 2, NextJobGeneration = 19 }, 1)]));
        foreach (var rewind in new long?[] { 19, null })
        {
            var rejected = Make.Turn(actor, row with { Revision = 3, NextJobGeneration = rewind }, 2);
            Context.Equal("definite", await c.Refused(() => c.Commit(0, lease.Epoch, 2, [rejected])), "current fence classifies the rejected counter change");
            Context.Equal("not landed", await c.Refused(() => c.Commit(0, lease.Epoch, 1, [rejected])), "stale fence takes precedence");
        }
        Context.Equal(20L, (await restart().LoadAsync([actor])).Single().NextJobGeneration, "updated counter survives stale revision");
        Console.WriteLine("ok fenced field insert and update; survived restart, exact poll, stale revision, and refused rewind or removal");
    }
}
