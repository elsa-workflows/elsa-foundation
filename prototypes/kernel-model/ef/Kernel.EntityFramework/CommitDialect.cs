using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kernel.EntityFramework;

/// <summary>Which code writes the commit: Entity Framework's SaveChanges, or the store's own SQL (round 5 B, COMMIT-SQL.md).</summary>
public enum CommitSql { EntityFramework, Owned }

/// <summary>One fence row of a commit: the virtual partition, what the owner believes, and where the position moves.</summary>
public sealed record FenceRow(int Id, long Epoch, long Position, long NewPosition);

/// <summary>What one result set of an owned command must say: nothing, a row count, the fence's count and time, or seqs.</summary>
internal enum Expect { Skip, Count, Fence, Seq }

/// <summary>
/// One command of the owned commit: statements, parameters and what each result set must say. A dialect appends to it;
/// <see cref="EfStore"/> runs it through EF's relational command (so the store's interceptors and the transaction apply)
/// and reads the result sets in order.
/// </summary>
internal sealed class OwnedCommand(CommitDialect dialect)
{
    public readonly System.Text.StringBuilder Text = new();
    public readonly List<(string Name, object? Value, RelationalTypeMapping Mapping)> Parameters = [];
    public readonly List<(Expect Kind, int Count)> Results = [];
    public bool HasFence;

    /// <summary>A parameter typed as the column it is compared with or written to.</summary>
    public string P(IProperty column, object? value)
    {
        var name = "@p" + Parameters.Count;
        Parameters.Add((name, value, column.GetRelationalTypeMapping()));
        return name;
    }

    /// <summary>A statement that yields no result set the reader must step over.</summary>
    public void Statement(string sql) => Text.Append(dialect.Quote(sql)).Append(";\n");

    /// <summary>A statement that yields one result set, read as <paramref name="expect"/> says (<see cref="Expect.Skip"/>: stepped over).</summary>
    public void Statement(string sql, Expect expect, int count = 0)
    {
        Statement(sql);
        Results.Add((expect, count));
    }
}

/// <summary>
/// One engine's dialect of the owned commit: how a statement reports its affected rows, how a sent message reports its
/// seq, and the SQL that differs. The shape of the commit is <see cref="EfStore"/>'s (EfStore.Owned.cs).
/// </summary>
internal abstract class CommitDialect
{
    /// <summary>Database time in milliseconds, as an SQL expression.</summary>
    public abstract string Now { get; }
    public virtual string Quote(string sql) => sql;
    public abstract string True { get; }
    public abstract string False { get; }

    /// <summary>Parameters one command may carry; the store starts a new command before it is passed (SQL Server: 2,100).</summary>
    public virtual int ParameterLimit => int.MaxValue;

    /// <summary>Rows one multi-row statement may carry (SQL Server's VALUES: 1,000).</summary>
    public virtual int RowsPerStatement => 1000;

    /// <summary>Messages one insert may carry: as the rows, unless the engine reports one seq per insert (MySQL).</summary>
    public virtual int MessageRowsPerStatement => RowsPerStatement;

    /// <summary>The seq of row <paramref name="ordinal"/> (1-based, in send order) of a message insert, where the store assigns it (SQLite); null where the database does.</summary>
    public virtual string? Seq(int ordinal) => null;

    /// <summary>Statements that open every command.</summary>
    public virtual string Prelude => "";

    /// <summary>Whether an exception says the commit did not land (a server-side count check): answered as the fence, not as a failure.</summary>
    public virtual bool NotLanded(Exception e) => false;

    /// <summary>For a <see cref="NotLanded"/> exception, whether the fence had held (a later count failed); null when the exception does not say.</summary>
    public virtual bool? FenceHeldAfter(Exception e) => null;

    /// <summary>
    /// The fence, for k rows in ascending Id order: Position moves where
    /// (Id, Epoch, Position) is what the owner believes. The result says how many rows matched (must be k) and the time.
    /// <paramref name="ignoreCount"/> is the FenceIgnoresCount mutation: a dialect whose server checks the count skips that check too.
    /// </summary>
    public abstract void Fence(OwnedCommand c, IReadOnlyList<FenceRow> rows, CommitWindow? window, bool unfenced, bool ignoreCount = false);

    /// <summary>A statement that must affect exactly <paramref name="count"/> rows, else the commit did not land.</summary>
    public abstract void Checked(OwnedCommand c, string dml, int count);

    /// <summary>A statement whose count does not matter (idempotent).</summary>
    public abstract void Free(OwnedCommand c, string dml);

    /// <summary>A multi-row insert of <paramref name="count"/> messages without their seq column; the result yields their seqs in the order sent.</summary>
    public abstract void InsertMessages(OwnedCommand c, string insert, int count);

    /// <summary>
    /// A delete or mark (<paramref name="action"/>) of the message rows with these keys (Id, InboxScope and, on the exact-key
    /// engines, IssuedWire), none of them an event delivery (EventMatch is null) and none done; every key must match.
    /// </summary>
    /// <paramref name="where"/> is a further predicate (" AND ..."), or empty: an owned set's bound to its fenced partitions.
    public abstract void KeyedMessages(OwnedCommand c, string action, IReadOnlyList<string[]> keys, bool exact, string where = "");

    /// <summary>After an aliased insert (SQL Server, MySQL): the row the database holds must be equal to both spellings.</summary>
    public virtual void AliasCheck(OwnedCommand c, string rowId, string actor) => throw new NotSupportedException("This engine has no actor aliases.");

    /// <summary>A wait that may be there already: insert it once.</summary>
    public abstract string SubscribeIfAbsent(string key, string instance, string wait, int partition);

    /// <summary>Reads one result set as the dialect wrote it: the row count it stands for, and the values it carries (the fence's time, or seqs).</summary>
    public abstract Task<(long Count, List<long> Values)> ReadAsync(System.Data.Common.DbDataReader reader, Expect kind, int count, CancellationToken cancellationToken);

    /// <summary>Locks the fence rows with these ids before the fence's UPDATE; the statement counts them (a missing row: the fence missed).</summary>
    protected abstract void Lock(OwnedCommand c, IReadOnlyList<int> ids);

    /// <summary>
    /// The k fence rows, locked before the fence's UPDATE so that two owners over overlapping rows cannot wait for each other
    /// (K1, ENGINES.md E9): in ascending Id, one statement. Rows in another order come only from the UnorderedFenceLocks
    /// mutation, and are locked one statement per row in that order.
    /// </summary>
    protected void LockRows(OwnedCommand c, IReadOnlyList<FenceRow> rows)
    {
        if (rows.Zip(rows.Skip(1)).All(p => p.First.Id < p.Second.Id)) Lock(c, [.. rows.Select(r => r.Id)]);
        else foreach (var r in rows) Lock(c, [r.Id]);
    }

    protected static string Ids(IEnumerable<int> ids) => string.Join(", ", ids);

    protected static string Values(IReadOnlyList<FenceRow> rows) =>
        string.Join(", ", rows.Select(r => FormattableString.Invariant($"({r.Id}, {r.Epoch}, {r.Position}, {r.NewPosition})")));

    protected static string KeyList(IReadOnlyList<string[]> keys) => string.Join(", ", keys.Select(k => "(" + string.Join(", ", k) + ")"));

    protected static string KeyColumns(bool exact) => exact ? "(\"Id\", \"InboxScope\", \"IssuedWire\")" : "(\"Id\", \"InboxScope\")";
}

/// <summary>
/// PostgreSQL and SQLite: every statement ends in RETURNING, so every statement yields one result set and the rows it
/// returns are the rows it affected. A fence row returns the time; a sent message returns its seq.
/// </summary>
internal abstract class ReturningDialect(string now, string @true, string @false) : CommitDialect
{
    public override string Now => now;
    public override string True => @true;
    public override string False => @false;

    public override void Fence(OwnedCommand c, IReadOnlyList<FenceRow> rows, CommitWindow? window, bool unfenced, bool ignoreCount = false)
    {
        var columns = window is { } w ? FormattableString.Invariant($", \"WindowStart\" = {w.FromInclusive}, \"WindowEnd\" = {w.UntilExclusive}") : "";
        if (rows.Count == 1)
        {
            var r = rows[0];
            c.Statement(FormattableString.Invariant($"""UPDATE "Partitions" SET "Position" = {r.NewPosition}{columns} WHERE "Id" = {r.Id} AND ({(unfenced ? "1 = 1" : FormattableString.Invariant($"\"Epoch\" = {r.Epoch} AND \"Position\" = {r.Position}"))}) RETURNING {Now} AS "Value" """), Expect.Fence, 1);
            return;
        }
        // k rows: locked in ascending Id order first, then one UPDATE.
        LockRows(c, rows);
        c.Statement($"""UPDATE "Partitions" AS p SET "Position" = f.np{columns} FROM (VALUES {Values(rows)}) AS f(id, epoch, pos, np) WHERE p."Id" = f.id AND ({(unfenced ? "1 = 1" : "p.\"Epoch\" = f.epoch AND p.\"Position\" = f.pos")}) RETURNING {Now} AS "Value" """, Expect.Fence, rows.Count);
    }

    public override void Checked(OwnedCommand c, string dml, int count) => c.Statement(dml + " RETURNING 1", Expect.Count, count);
    public override void Free(OwnedCommand c, string dml) => c.Statement(dml + " RETURNING 1", Expect.Skip);
    public override void InsertMessages(OwnedCommand c, string insert, int count) => c.Statement(insert + " RETURNING \"Seq\"", Expect.Seq, count);
    /// <summary>A row-value IN over a VALUES list: PostgreSQL and SQLite both take it as a subquery.</summary>
    public override void KeyedMessages(OwnedCommand c, string action, IReadOnlyList<string[]> keys, bool exact, string where = "") =>
        c.Statement($"""{action} WHERE "Done" = {False} AND "EventMatch" IS NULL AND {KeyColumns(exact)} IN (VALUES {KeyList(keys)}){where} RETURNING 1""", Expect.Count, keys.Count);
    /// <summary>PostgreSQL: the locking read sorts first, then locks (LockRows above Sort). SQLite has one writer and no lock statement.</summary>
    protected override void Lock(OwnedCommand c, IReadOnlyList<int> ids) =>
        c.Statement($"""SELECT "Id" FROM "Partitions" WHERE "Id" IN ({Ids(ids)}) ORDER BY "Id" FOR UPDATE""", Expect.Count, ids.Count);
    public override string SubscribeIfAbsent(string key, string instance, string wait, int partition) =>
        FormattableString.Invariant($"""INSERT INTO "Subscriptions" ("Key", "Instance", "Wait", "Partition") VALUES ({key}, {instance}, {wait}, {partition}) ON CONFLICT DO NOTHING""");

    public override async Task<(long Count, List<long> Values)> ReadAsync(System.Data.Common.DbDataReader reader, Expect kind, int count, CancellationToken cancellationToken)
    {
        var (rows, values) = (0L, new List<long>());
        while (await reader.ReadAsync(cancellationToken))
            if (rows++ == 0 && kind is Expect.Fence || kind is Expect.Seq) values.Add(reader.GetInt64(0));
        if (kind is Expect.Seq) values.Sort();     // RETURNING rows come in VALUES order in practice; seqs are assigned in it by construction
        return (rows, values);
    }
}

/// <summary>PostgreSQL (Npgsql): one command of many statements is one round trip; identity assigns seq; RETURNING rows come in VALUES order.</summary>
internal sealed class PgCommitDialect() : ReturningDialect("(extract(epoch from clock_timestamp()) * 1000)::bigint", "true", "false")
{
    /// <summary>The engine's dialect of the owned commit (COMMIT-SQL.md); every engine has one since round 5 B4.</summary>
    public static CommitDialect For(Engine? engine, bool postgres) => engine switch
    {
        null when postgres => new PgCommitDialect(),
        null => new SqliteCommitDialect(),
        SqlServerEngine => new SqlServerCommitDialect(),
        MySqlEngine => new MySqlCommitDialect(),
        _ => throw new NotSupportedException($"No owned commit dialect for {engine.GetType().Name}."),
    };
}

/// <summary>
/// SQL Server (SqlClient): one T-SQL batch per round trip. The server checks every count itself (IF @@ROWCOUNT <> n
/// THROW), so a stale owner's batch stops at the fence before it touches a message row (E9: the partition row is the
/// first lock, as in the take; E1 cannot arise), and the client reads only the fence's time and the seqs. Error 51010 is
/// the fence, 51011 a count, 51029 an actor alias that names another row; each is answered as "not landed".
/// </summary>
internal sealed class SqlServerCommitDialect : CommitDialect
{
    public const int FenceMissed = 51010, CountMismatch = 51011, AliasMismatch = 51029;

    public override string Now => "DATEDIFF_BIG(millisecond, '1970-01-01', SYSUTCDATETIME())";
    public override string True => "1";
    public override string False => "0";
    /// <summary>2,100 parameters per request; the fence and the prelude need none, the rows at most 16 each.</summary>
    public override int ParameterLimit => 2000;
    /// <summary>XACT_ABORT: any error, THROW included, ends the batch and rolls the transaction back; the client never sends COMMIT after one.</summary>
    public override string Prelude => "SET NOCOUNT ON; SET XACT_ABORT ON;\n";

    public override bool NotLanded(Exception e)
    {
        for (var inner = e; inner is not null; inner = inner.InnerException)
            if (inner is Microsoft.Data.SqlClient.SqlException { Number: FenceMissed or CountMismatch or AliasMismatch }) return true;
        return false;
    }

    public override bool? FenceHeldAfter(Exception e)
    {
        for (var inner = e; inner is not null; inner = inner.InnerException)
            if (inner is Microsoft.Data.SqlClient.SqlException { Number: FenceMissed or CountMismatch or AliasMismatch } sql) return sql.Number != FenceMissed;
        return null;
    }

    private static string Throw(int number, string what) => $"THROW {number}, '{what}', 1";

    /// <summary>An update lock on each row; an IN list over the clustered key is one seek in ascending Id, as the take's.</summary>
    protected override void Lock(OwnedCommand c, IReadOnlyList<int> ids) =>
        c.Statement($"""IF (SELECT COUNT(*) FROM "Partitions" WITH (UPDLOCK, ROWLOCK) WHERE "Id" IN ({Ids(ids)})) <> {ids.Count} {Throw(FenceMissed, "kernel fence: a partition row is missing")}""");

    public override void Fence(OwnedCommand c, IReadOnlyList<FenceRow> rows, CommitWindow? window, bool unfenced, bool ignoreCount = false)
    {
        if (window is not null) throw new NotSupportedException("Commit windows are PostgreSQL's.");
        string matched;
        if (rows.Count == 1)
        {
            var r = rows[0];
            matched = FormattableString.Invariant($"""UPDATE "Partitions" SET "Position" = {r.NewPosition} WHERE "Id" = {r.Id} AND ({(unfenced ? "1 = 1" : FormattableString.Invariant($"\"Epoch\" = {r.Epoch} AND \"Position\" = {r.Position}"))})""");
        }
        else
        {
            // k rows: an update lock on each in ascending Id order first, then one UPDATE.
            LockRows(c, rows);
            matched = $"""UPDATE p SET "Position" = f.np FROM "Partitions" p JOIN (VALUES {Values(rows)}) AS f(id, epoch, pos, np) ON p."Id" = f.id WHERE {(unfenced ? "1 = 1" : "p.\"Epoch\" = f.epoch AND p.\"Position\" = f.pos")}""";
        }
        // The fence's count is checked twice: by the server, which stops the batch, and by the client, which reads it back. The
        // FenceIgnoresCount mutation skips both, else the server's check alone would mask it (COMMIT-SQL.md, the pins).
        c.Statement(matched);
        c.Statement($"DECLARE @kernel_fenced int = @@ROWCOUNT;" + (ignoreCount ? "" : $" IF @kernel_fenced <> {rows.Count} {Throw(FenceMissed, "kernel fence: the partition is no longer held by this owner at this position")}"));
        c.Statement($"""SELECT @kernel_fenced AS "Matched", {Now} AS "Now" """, Expect.Fence, rows.Count);
    }

    public override void Checked(OwnedCommand c, string dml, int count) => c.Statement($"{dml}; IF @@ROWCOUNT <> {count} {Throw(CountMismatch, "kernel commit: a row count did not match")}");
    public override void Free(OwnedCommand c, string dml) => c.Statement(dml);

    /// <summary>OUTPUT yields the identity values; sorted ascending they are in VALUES order (identity is assigned row by row). Messages has no trigger, so OUTPUT needs no INTO.</summary>
    public override void InsertMessages(OwnedCommand c, string insert, int count)
    {
        var at = insert.IndexOf(" VALUES ", StringComparison.Ordinal);
        c.Statement(insert[..at] + " OUTPUT inserted.\"Seq\"" + insert[at..], Expect.Seq, count);
        c.Statement($"IF @@ROWCOUNT <> {count} {Throw(CountMismatch, "kernel commit: a message insert did not match")}");
    }

    /// <summary>No row-value IN on T-SQL: the key list is a VALUES table the row must exist in.</summary>
    public override void KeyedMessages(OwnedCommand c, string action, IReadOnlyList<string[]> keys, bool exact, string where = "") =>
        Checked(c, $"""{action} WHERE "Done" = 0 AND "EventMatch" IS NULL AND EXISTS (SELECT 1 FROM (VALUES {KeyList(keys)}) AS k("Id", "InboxScope"{(exact ? ", \"IssuedWire\"" : "")}) WHERE k."Id" = "Messages"."Id" AND k."InboxScope" = "Messages"."InboxScope"{(exact ? " AND k.\"IssuedWire\" = \"Messages\".\"IssuedWire\"" : "")}){where}""", keys.Count);

    public override void AliasCheck(OwnedCommand c, string rowId, string actor) =>
        c.Statement($"""IF NOT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {rowId} AND "Id" = {actor}) {Throw(AliasMismatch, "kernel actor alias mismatch")}""");

    public override string SubscribeIfAbsent(string key, string instance, string wait, int partition) =>
        FormattableString.Invariant($"""INSERT INTO "Subscriptions" ("Key", "Instance", "Wait", "Partition") SELECT {key}, {instance}, {wait}, {partition} WHERE NOT EXISTS (SELECT 1 FROM "Subscriptions" WHERE "Key" = {key} AND "Instance" = {instance} AND "Wait" = {wait})""");

    public override async Task<(long Count, List<long> Values)> ReadAsync(System.Data.Common.DbDataReader reader, Expect kind, int count, CancellationToken cancellationToken)
    {
        var values = new List<long>();
        if (kind == Expect.Fence)
        {
            if (!await reader.ReadAsync(cancellationToken)) return (0, values);
            values.Add(reader.GetInt64(1));
            return (reader.GetInt32(0), values);
        }
        while (await reader.ReadAsync(cancellationToken)) values.Add(reader.GetInt64(0));
        values.Sort();
        return (values.Count, values);
    }
}
