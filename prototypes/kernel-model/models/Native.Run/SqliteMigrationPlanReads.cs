using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;
using Microsoft.Data.Sqlite;

namespace Kernel.Native.Run;

/// <summary>The SQLite adapter for the operator's plan outcomes read model.</summary>
internal sealed class SqliteMigrationPlanReads(string file, FoundationReadCursor cursor) : IMigrationPlanReads
{
    internal static async Task EnableAsync(string file, CancellationToken stop)
    {
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadWrite }.ConnectionString);
        await db.OpenAsync(stop);
        using var transaction = db.BeginTransaction(deferred: false);
        using var tables = db.CreateCommand();
        tables.Transaction = transaction;
        tables.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name IN ('plan_cursors', 'plan_outcomes')";
        if (await tables.ExecuteScalarAsync(stop) is not 2L)
            throw new InvalidOperationException("The database does not contain the SQLite plan projection.");
        await EnsureKeyAsync(db, transaction, stop);
        await transaction.CommitAsync(stop);
    }

    internal static async Task EnsureKeyAsync(SqliteConnection db, SqliteTransaction transaction, CancellationToken stop)
    {
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS plan_read_keys (
                singleton INTEGER PRIMARY KEY CHECK (singleton = 1), cursor_key BLOB NOT NULL CHECK (length(cursor_key) = 32)
            );
            INSERT INTO plan_read_keys VALUES (1, $cursor_key) ON CONFLICT (singleton) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$cursor_key", RandomNumberGenerator.GetBytes(32));
        await command.ExecuteNonQueryAsync(stop);
        command.Parameters.Clear();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'plan_result_cursors'";
        if (await command.ExecuteScalarAsync(stop) is 1L) return;
        command.CommandText = """
            CREATE TABLE plan_result_cursors (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tenant TEXT NOT NULL, plan TEXT NOT NULL, actor TEXT NOT NULL, UNIQUE (tenant, plan, actor)
            );
            INSERT INTO plan_result_cursors (tenant, plan, actor) SELECT tenant, plan, actor FROM plan_outcomes;
            CREATE TRIGGER index_plan_result_cursor AFTER INSERT ON plan_outcomes BEGIN
                INSERT INTO plan_result_cursors (tenant, plan, actor) VALUES (NEW.tenant, NEW.plan, NEW.actor);
            END;
            CREATE TRIGGER delete_plan_result_cursor AFTER DELETE ON plan_outcomes BEGIN
                DELETE FROM plan_result_cursors WHERE tenant = OLD.tenant AND plan = OLD.plan AND actor = OLD.actor;
            END;
            """;
        await command.ExecuteNonQueryAsync(stop);
    }

    internal static async Task<SqliteMigrationPlanReads> OpenAsync(string file, CancellationToken stop)
    {
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly }.ConnectionString);
        await db.OpenAsync(stop);
        using var command = db.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name IN ('plan_read_keys', 'plan_result_cursors')";
        if (await command.ExecuteScalarAsync(stop) is not 2L)
            throw new InvalidOperationException("Run sqlite-plan-projection enable-paging on this existing view first.");
        command.CommandText = "SELECT cursor_key FROM plan_read_keys WHERE singleton = 1";
        if (await command.ExecuteScalarAsync(stop) is not byte[] { Length: 32 } key)
            throw new InvalidOperationException("The read model does not have an initialized continuation key.");
        return new(file, new(key));
    }

    public async ValueTask<MigrationPlanResultPage> ListAsync(BoundScope scope, ActorRef plan, int pageSize,
        PageToken? from = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrEmpty(plan.Id, nameof(plan));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 500);
        var context = JsonSerializer.SerializeToUtf8Bytes(new { Read = "migration-plan-results", scope.Tenant, Plan = plan.Id, Size = pageSize });
        long? cursorId = from is { } token ? Unseal(context, token) : null;
        var page = await new SqlitePlanProjection(file, scope, plan).ReadOperatorPageAsync(cursorId, pageSize, cancellationToken);
        PageToken? next = page.CursorId is { } last ? Seal(context, last) : null;
        return new(page.Closed, new(page.Rows.Select(row => new MigrationPlanResult(row.Actor, row.Result)).ToArray(), next, page.AsOf));
    }

    private PageToken Seal(ReadOnlySpan<byte> context, long row)
    {
        Span<byte> plain = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(plain, row);
        return new(cursor.Seal(context, plain));
    }

    private long Unseal(ReadOnlySpan<byte> context, PageToken token)
    {
        Span<byte> plain = stackalloc byte[sizeof(long)];
        if (!cursor.TryOpen(context, token.Value, plain))
            throw new ArgumentException("The continuation does not match this plan results request.", "from");
        return BinaryPrimitives.ReadInt64LittleEndian(plain);
    }
}
