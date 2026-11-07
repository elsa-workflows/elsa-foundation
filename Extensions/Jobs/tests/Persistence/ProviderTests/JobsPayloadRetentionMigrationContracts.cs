using System.Data;
using System.Globalization;
using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Jobs.Persistence.ProviderTests;

public sealed partial class JobsProviderContracts
{
    [SkippableTheory]
    [InlineData("Sqlite", false)]
    [InlineData("PostgreSql", false)]
    [InlineData("Sqlite", true)]
    [InlineData("PostgreSql", true)]
    public async Task Retention_migration_downgrade_and_upgrade_preserve_unexpired_payload_bytes(string provider, bool finiteLifetime)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(PayloadRetentionEpoch);
        var lifetime = finiteLifetime ? PayloadLifetime : (TimeSpan?)null;
        var store = PayloadStore(database, clock, lifetime, lifetime);
        var completed = await CompleteForPayloadRetention(store, "rollback-present-" + finiteLifetime);
        if (finiteLifetime) clock.Set(PayloadRetentionEpoch + PayloadLifetime.Ticks - 1);
        Assert.Equal(0, (await store.ExpirePayloadsAsync(new(Scope))).ExpiredInputs);
        var bytes = await RollbackPayloadBytes(database);
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            AssertMigrationSchema(provider, db);
            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            var baseline = db.Database.GetMigrations().Single(name => name.EndsWith("_RetainOperationAudit", StringComparison.Ordinal));
            await db.GetService<IMigrator>().MigrateAsync(baseline);
            Assert.DoesNotContain(await db.Database.GetAppliedMigrationsAsync(), name => name.EndsWith("_RetainTerminalPayloads", StringComparison.Ordinal));
            await db.GetService<IMigrator>().MigrateAsync();
            Assert.Equal(applied, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        }
        Assert.Equal(bytes, await RollbackPayloadBytes(database));
        var retained = (await store.FindAsync(Scope, completed.Acceptance.JobId))!;
        Assert.NotNull(retained.Request); Assert.NotNull(retained.Result);
        Assert.Equal(JobPayloadState.Available, retained.InputPayloadState);
        Assert.Equal(JobPayloadState.Available, retained.ResultPayloadState);
        Assert.Null(retained.PayloadRetention!.InputExpiredAt); Assert.Null(retained.PayloadRetention.ResultExpiredAt);
    }

    [SkippableTheory]
    [InlineData("Sqlite", "queued-input-expired")]
    [InlineData("PostgreSql", "queued-input-expired")]
    [InlineData("Sqlite", "queued-result-expired")]
    [InlineData("PostgreSql", "queued-result-expired")]
    [InlineData("Sqlite", "receipt-expired")]
    [InlineData("PostgreSql", "receipt-expired")]
    [InlineData("Sqlite", "missing-request")]
    [InlineData("PostgreSql", "missing-request")]
    [InlineData("Sqlite", "missing-result")]
    [InlineData("PostgreSql", "missing-result")]
    [InlineData("Sqlite", "missing-receipt")]
    [InlineData("PostgreSql", "missing-receipt")]
    [InlineData("Sqlite", "input-marker-with-bytes")]
    [InlineData("PostgreSql", "input-marker-with-bytes")]
    [InlineData("Sqlite", "result-marker-with-bytes")]
    [InlineData("PostgreSql", "result-marker-with-bytes")]
    [InlineData("Sqlite", "receipt-marker-with-bytes")]
    [InlineData("PostgreSql", "receipt-marker-with-bytes")]
    public async Task Retention_migration_refuses_destructive_downgrade_before_schema_history_or_proof_changes(string provider, string unsafeState)
    {
        await using var database = await Database(provider);
        await PrepareUnsafeRetentionRollback(database, unsafeState);
        var bytes = await RetentionBytes(database);
        string schema;
        string[] history;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            AssertMigrationSchema(provider, db);
            await db.Database.OpenConnectionAsync();
            schema = await RetentionSchemaBytes(db, provider);
            history = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            var baseline = db.Database.GetMigrations().Single(name => name.EndsWith("_RetainOperationAudit", StringComparison.Ordinal));
            var refused = await Assert.ThrowsAnyAsync<Exception>(() => db.GetService<IMigrator>().MigrateAsync(baseline));
            Assert.Contains("CK_elsa_jobs_payload_down_", refused.ToString(), StringComparison.Ordinal);
            Assert.Equal(schema, await RetentionSchemaBytes(db, provider));
            Assert.Equal(history, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        }
        // The original open connection also observes SQLite TEMP state. Fresh contexts independently
        // verify retained columns, indexes, constraints and migration history after the refusal.
        await using (var check = await database.Contexts.CreateDbContextAsync())
        {
            Assert.Equal(history, (await check.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Equal(schema, await RetentionSchemaBytes(check, provider));
            await check.GetService<IMigrator>().MigrateAsync();
        }
        Assert.Equal(bytes, await RetentionBytes(database));
    }

    private static void AssertMigrationSchema(string provider, JobsDbContext db)
    {
        if (provider == "PostgreSql") Assert.StartsWith("jobs_", db.Model.GetDefaultSchema()!);
        else Assert.Null(db.Model.GetDefaultSchema());
    }

    private static async Task PrepareUnsafeRetentionRollback(JobsProviderDatabase database, string state)
    {
        var clock = new ControlledProviderClock(PayloadRetentionEpoch);
        var inputExpiry = state is "queued-input-expired" or "input-marker-with-bytes";
        var resultExpiry = state is "queued-result-expired" or "result-marker-with-bytes" or "receipt-expired" or "receipt-marker-with-bytes";
        var store = PayloadStore(database, clock, inputExpiry ? PayloadLifetime : null, resultExpiry ? PayloadLifetime : null);
        if (inputExpiry || state is "queued-result-expired" or "result-marker-with-bytes")
        {
            var accepted = (await store.AdmitAsync(RetentionAdmission("rollback-queued"))).Acceptance!;
            await store.CancelAsync(new(Scope, accepted.JobId, Op()));
            string? input, result;
            await using (var before = await database.Contexts.CreateDbContextAsync())
            {
                var row = await before.Jobs.SingleAsync(); input = row.RequestJson; result = row.ResultJson;
                Assert.Empty(await before.Receipts.ToArrayAsync());
            }
            clock.Set(PayloadRetentionEpoch + PayloadLifetime.Ticks);
            var expired = await store.ExpirePayloadsAsync(new(Scope));
            Assert.Equal(inputExpiry ? 1 : 0, expired.ExpiredInputs);
            Assert.Equal(inputExpiry ? 0 : 1, expired.ExpiredResults);
            if (state.EndsWith("marker-with-bytes", StringComparison.Ordinal))
            {
                await using var restoreBytes = await database.Contexts.CreateDbContextAsync();
                var row = await restoreBytes.Jobs.SingleAsync();
                if (inputExpiry) { Assert.NotNull(row.InputExpiredAt); row.RequestJson = input; }
                else { Assert.NotNull(row.ResultExpiredAt); row.ResultJson = result; }
                await restoreBytes.SaveChangesAsync();
            }
            return;
        }
        if (state == "missing-request")
        {
            await store.AdmitAsync(RetentionAdmission("rollback-missing-request"));
            await using var db = await database.Contexts.CreateDbContextAsync();
            var row = await db.Jobs.SingleAsync(); Assert.Null(row.InputExpiredAt); row.RequestJson = null;
            await db.SaveChangesAsync(); return;
        }
        await CompleteForPayloadRetention(store, "rollback-completed");
        if (resultExpiry)
        {
            string result, receipt;
            await using (var before = await database.Contexts.CreateDbContextAsync())
            { result = (await before.Jobs.SingleAsync()).ResultJson!; receipt = (await before.Receipts.SingleAsync()).ReceiptJson!; }
            clock.Set(PayloadRetentionEpoch + PayloadLifetime.Ticks);
            Assert.Equal(1, (await store.ExpirePayloadsAsync(new(Scope))).ExpiredResults);
            await using var restoreJob = await database.Contexts.CreateDbContextAsync();
            var job = await restoreJob.Jobs.SingleAsync(); job.ResultJson = result; job.ResultExpiredAt = null;
            var physical = await restoreJob.Receipts.SingleAsync(); Assert.NotNull(physical.PayloadExpiredAt);
            if (state == "receipt-marker-with-bytes") physical.ReceiptJson = receipt;
            else Assert.Null(physical.ReceiptJson);
            // Isolate the receipt guard: the job itself again satisfies every downgrade predicate.
            Assert.NotNull(job.RequestJson); Assert.Null(job.InputExpiredAt);
            await restoreJob.SaveChangesAsync(); return;
        }
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            if (state == "missing-result")
            {
                var row = await db.Jobs.SingleAsync(); Assert.NotNull(row.ResultMetadataJson); Assert.Null(row.ResultExpiredAt); row.ResultJson = null;
            }
            else if (state == "missing-receipt")
            {
                var row = await db.Receipts.SingleAsync(); Assert.Null(row.PayloadExpiredAt); row.ReceiptJson = null;
            }
            else throw new ArgumentOutOfRangeException(nameof(state));
            await db.SaveChangesAsync();
        }
    }

    private static async Task<string> RollbackPayloadBytes(JobsProviderDatabase database)
    {
        await using var db = await database.Contexts.CreateDbContextAsync();
        return JsonSerializer.Serialize(new
        {
            Jobs = await db.Jobs.AsNoTracking().OrderBy(row => row.Key).Select(row => new
            { row.Key, row.JobId, row.RequestSha256, row.RequestJson, row.ResultJson, row.ProgressJson, row.State, row.Revision }).ToArrayAsync(),
            Receipts = await db.Receipts.AsNoTracking().OrderBy(row => row.Key).Select(row => new
            { row.Key, row.JobKey, row.AttemptKey, row.ReceiptJson, row.ReceiptSha256 }).ToArrayAsync()
        });
    }

    private static async Task<string> RetentionSchemaBytes(JobsDbContext db, string provider)
    {
        var statements = provider == "Sqlite" ? new[]
        {
            "SELECT type,name,tbl_name,sql FROM sqlite_master ORDER BY type,name",
            "SELECT type,name,tbl_name,sql FROM sqlite_temp_master ORDER BY type,name"
        } : new[]
        {
            "SELECT table_name,column_name,data_type,is_nullable,column_default FROM information_schema.columns WHERE table_schema=@schema ORDER BY table_name,ordinal_position",
            "SELECT tablename,indexname,indexdef FROM pg_indexes WHERE schemaname=@schema ORDER BY tablename,indexname",
            "SELECT c.relname,k.conname,pg_get_constraintdef(k.oid) FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema ORDER BY c.relname,k.conname"
        };
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await db.Database.OpenConnectionAsync();
        var rows = new List<string[]>();
        try
        {
            foreach (var sql in statements)
            {
                await using var command = connection.CreateCommand(); command.CommandText = sql;
                if (provider == "PostgreSql")
                { var parameter = command.CreateParameter(); parameter.ParameterName = "schema"; parameter.Value = db.Model.GetDefaultSchema()!; command.Parameters.Add(parameter); }
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? "<NULL>" : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture)!).ToArray());
            }
        }
        finally { if (opened) await db.Database.CloseConnectionAsync(); }
        return JsonSerializer.Serialize(rows);
    }
}
