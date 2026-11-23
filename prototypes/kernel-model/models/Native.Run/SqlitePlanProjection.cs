using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;
using Microsoft.Data.Sqlite;

namespace Kernel.Native.Run;

/// <summary>A physical read model for one exact plan, updated independently from the actor's commit.</summary>
internal sealed class SqlitePlanProjection(string file, BoundScope scope, ActorRef plan)
{
    internal sealed record Outcome(ActorRef Actor, MigrationPlanOutcome? Result);
    internal sealed record OutcomePage(bool Closed, long AsOf, IReadOnlyList<Outcome> Rows, ActorRef? After, long? CursorId = null);

    private SqliteConnection Open(SqliteOpenMode mode = SqliteOpenMode.ReadWrite) =>
        new(new SqliteConnectionStringBuilder { DataSource = file, Mode = mode }.ConnectionString);

    internal async Task InitializeAsync(CancellationToken stop)
    {
        await using var db = Open(SqliteOpenMode.ReadWriteCreate);
        await db.OpenAsync(stop);
        using var transaction = db.BeginTransaction(deferred: false);
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS plan_cursors (
                tenant TEXT NOT NULL, plan TEXT NOT NULL, bookmark TEXT, closed INTEGER NOT NULL, as_of INTEGER NOT NULL,
                PRIMARY KEY (tenant, plan)
            ) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS plan_outcomes (
                tenant TEXT NOT NULL, plan TEXT NOT NULL, actor TEXT NOT NULL, result INTEGER CHECK (result IN (0, 1, 2)),
                PRIMARY KEY (tenant, plan, actor)
            ) WITHOUT ROWID;
            """;
        await command.ExecuteNonQueryAsync(stop);
        await SqliteMigrationPlanReads.EnsureKeyAsync(db, transaction, stop);
        await transaction.CommitAsync(stop);
    }

    internal async Task<PageToken?> BookmarkAsync(CancellationToken stop)
    {
        await using var db = Open(SqliteOpenMode.ReadOnly);
        await db.OpenAsync(stop);
        using var command = Command(db, null, "SELECT bookmark FROM plan_cursors WHERE tenant = $tenant AND plan = $plan");
        return await command.ExecuteScalarAsync(stop) is string value ? new PageToken(value) : null;
    }

    internal async Task<bool> ProjectNextAsync(IActorReads source, CancellationToken stop)
    {
        while (true)
        {
            var before = await BookmarkAsync(stop);
            var page = await source.TailAsync(scope, plan, before, stop);
            if (page.Items.Count == 0)
            {
                if (before is null || await TryApplyAsync(before, before.Value, page.AsOf, [], stop)) return false;
                continue;
            }
            if (page.Next is not { } next || next == before)
                throw new InvalidOperationException("A nonempty plan journal page needs an advancing bookmark.");
            var records = page.Items.Select(item => MigrationPlanJournal.Read(item.Entry)).ToArray();
            if (await TryApplyAsync(before, next, page.AsOf, records, stop)) return true;
        }
    }

    private async Task<bool> TryApplyAsync(PageToken? before, PageToken next, long asOf,
        IReadOnlyList<MigrationPlanJournal?> records, CancellationToken stop)
    {
        await using var db = Open();
        await db.OpenAsync(stop);
        using var write = db.BeginTransaction(deferred: false);
        using (var ensure = Command(db, write, """
            INSERT INTO plan_cursors VALUES ($tenant, $plan, NULL, 0, 0) ON CONFLICT (tenant, plan) DO NOTHING
            """))
            await ensure.ExecuteNonQueryAsync(stop);
        bool closed;
        using (var advance = Command(db, write, """
            UPDATE plan_cursors SET bookmark = $next, as_of = MAX(as_of, $as_of)
            WHERE tenant = $tenant AND plan = $plan AND bookmark IS $before
            RETURNING closed
            """))
        {
            advance.Parameters.AddWithValue("$before", (object?)before?.Value ?? DBNull.Value);
            advance.Parameters.AddWithValue("$next", next.Value);
            advance.Parameters.AddWithValue("$as_of", asOf);
            if (await advance.ExecuteScalarAsync(stop) is not long prior) return false;
            closed = prior != 0;
        }
        foreach (var record in records)
        {
            switch (record)
            {
                case MigrationPlanJournal.Selected batch when !closed:
                    using (var insert = Command(db, write, """
                        INSERT INTO plan_outcomes VALUES ($tenant, $plan, $actor, NULL)
                        ON CONFLICT (tenant, plan, actor) DO NOTHING
                        """))
                    {
                        var actor = insert.Parameters.Add("$actor", SqliteType.Text);
                        foreach (var selected in batch.Actors)
                        {
                            actor.Value = selected.Id;
                            await insert.ExecuteNonQueryAsync(stop);
                        }
                    }
                    break;
                case MigrationPlanJournal.Answered answer:
                    using (var update = Command(db, write, """
                        UPDATE plan_outcomes SET result = $result
                        WHERE tenant = $tenant AND plan = $plan AND actor = $actor AND (result IS NULL OR result = $provisional)
                        """))
                    {
                        update.Parameters.AddWithValue("$actor", answer.Actor.Id);
                        update.Parameters.AddWithValue("$result", (int)answer.Outcome);
                        update.Parameters.AddWithValue("$provisional", (int)MigrationPlanOutcome.NotAtSource);
                        await update.ExecuteNonQueryAsync(stop);
                    }
                    break;
                case MigrationPlanJournal.Closed when !closed:
                    using (var close = Command(db, write, "UPDATE plan_cursors SET closed = 1 WHERE tenant = $tenant AND plan = $plan"))
                        await close.ExecuteNonQueryAsync(stop);
                    closed = true;
                    break;
            }
        }
        stop.ThrowIfCancellationRequested();
        await write.CommitAsync(stop);
        return true;
    }

    internal Task<OutcomePage> ReadPageAsync(ActorRef? after, int size, CancellationToken stop) =>
        ReadPageAsync(after, null, false, size, stop);

    internal Task<OutcomePage> ReadOperatorPageAsync(long? cursorId, int size, CancellationToken stop) =>
        ReadPageAsync(null, cursorId, true, size, stop);

    private async Task<OutcomePage> ReadPageAsync(ActorRef? after, long? cursorId, bool includeCursor, int size, CancellationToken stop)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 500);
        await using var db = Open(SqliteOpenMode.ReadOnly);
        await db.OpenAsync(stop);
        using var snapshot = db.BeginTransaction(deferred: true);
        if (cursorId is { } prior)
        {
            using var boundary = Command(db, snapshot, "SELECT actor FROM plan_result_cursors WHERE tenant = $tenant AND plan = $plan AND id = $id");
            boundary.Parameters.AddWithValue("$id", prior);
            if (await boundary.ExecuteScalarAsync(stop) is not string actor)
                throw new ArgumentException("The continuation no longer names a result of this plan.", "from");
            after = new(actor);
        }
        bool closed;
        long asOf;
        using (var metadata = Command(db, snapshot, "SELECT closed, as_of FROM plan_cursors WHERE tenant = $tenant AND plan = $plan"))
        await using (var row = await metadata.ExecuteReaderAsync(stop))
        {
            if (!await row.ReadAsync(stop)) return new(false, 0, [], null);
            closed = row.GetBoolean(0);
            asOf = row.GetInt64(1);
        }
        using var query = Command(db, snapshot, """
            SELECT actor, result FROM plan_outcomes
            WHERE tenant = $tenant AND plan = $plan AND actor > $after ORDER BY actor LIMIT $limit
            """);
        query.Parameters.AddWithValue("$after", after?.Id ?? string.Empty);
        query.Parameters.AddWithValue("$limit", size + 1);
        var rows = new List<Outcome>();
        await using (var reader = await query.ExecuteReaderAsync(stop))
            while (await reader.ReadAsync(stop))
                rows.Add(new(new(reader.GetString(0)), reader.IsDBNull(1) ? null : (MigrationPlanOutcome)reader.GetInt32(1)));
        ActorRef? next = null;
        if (rows.Count > size)
        {
            rows.RemoveAt(size);
            next = rows[^1].Actor;
        }
        long? nextId = null;
        if (includeCursor && next is { } last)
        {
            using var boundary = Command(db, snapshot, "SELECT id FROM plan_result_cursors WHERE tenant = $tenant AND plan = $plan AND actor = $actor");
            boundary.Parameters.AddWithValue("$actor", last.Id);
            if (await boundary.ExecuteScalarAsync(stop) is not long id)
                throw new InvalidOperationException("The scoped result is missing its continuation row.");
            nextId = id;
        }
        return new(closed, asOf, rows, next, nextId);
    }

    private SqliteCommand Command(SqliteConnection db, SqliteTransaction? transaction, string sql)
    {
        var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$tenant", scope.Tenant);
        command.Parameters.AddWithValue("$plan", plan.Id);
        return command;
    }
}
