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

    public void Statement(string sql, Expect expect = Expect.Skip, int count = 0)
    {
        Text.Append(dialect.Quote(sql)).Append(";\n");
        if (expect != Expect.Skip || dialect.ResultPerStatement) Results.Add((expect, count));
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

    /// <summary>Whether a statement that asks for no count still yields a result set the reader must step over.</summary>
    public abstract bool ResultPerStatement { get; }

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

    /// <summary>
    /// The fence, for k rows in ascending Id order: Position moves and the lease is renewed from database time where
    /// (Id, Epoch, Position) is what the owner believes. The result says how many rows matched (must be k) and the time.
    /// </summary>
    public abstract void Fence(OwnedCommand c, IReadOnlyList<FenceRow> rows, long leaseMs, CommitWindow? window, bool unfenced);

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
    public abstract void KeyedMessages(OwnedCommand c, string action, IReadOnlyList<string[]> keys, bool exact);

    /// <summary>After an aliased insert (SQL Server, MySQL): the row the database holds must be equal to both spellings.</summary>
    public virtual void AliasCheck(OwnedCommand c, string rowId, string actor) => throw new NotSupportedException("This engine has no actor aliases.");

    /// <summary>A wait that may be there already: insert it once.</summary>
    public abstract string SubscribeIfAbsent(string key, string instance, string wait, int partition);

    /// <summary>Reads one result set as the dialect wrote it: the row count it stands for, and the values it carries (the fence's time, or seqs).</summary>
    public abstract Task<(long Count, List<long> Values)> ReadAsync(System.Data.Common.DbDataReader reader, Expect kind, int count, CancellationToken cancellationToken);

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
    public override bool ResultPerStatement => true;

    public override void Fence(OwnedCommand c, IReadOnlyList<FenceRow> rows, long leaseMs, CommitWindow? window, bool unfenced)
    {
        var lease = FormattableString.Invariant($"\"LeaseUntil\" = {Now} + {leaseMs}");
        var columns = window is { } w ? FormattableString.Invariant($", \"WindowStart\" = {w.FromInclusive}, \"WindowEnd\" = {w.UntilExclusive}") : "";
        if (rows.Count == 1)
        {
            var r = rows[0];
            c.Statement(FormattableString.Invariant($"""UPDATE "Partitions" SET "Position" = {r.NewPosition}, {lease}{columns} WHERE "Id" = {r.Id} AND ({(unfenced ? "1 = 1" : FormattableString.Invariant($"\"Epoch\" = {r.Epoch} AND \"Position\" = {r.Position}"))}) RETURNING {Now} AS "Value" """), Expect.Fence, 1);
            return;
        }
        // k rows: locked in ascending Id order first, so two owners over overlapping rows cannot wait for each other, then one UPDATE.
        c.Statement($"""SELECT "Id" FROM "Partitions" WHERE "Id" IN ({string.Join(", ", rows.Select(r => r.Id))}) ORDER BY "Id" FOR UPDATE""", Expect.Count, rows.Count);
        c.Statement($"""UPDATE "Partitions" AS p SET "Position" = f.np, {lease}{columns} FROM (VALUES {Values(rows)}) AS f(id, epoch, pos, np) WHERE p."Id" = f.id AND ({(unfenced ? "1 = 1" : "p.\"Epoch\" = f.epoch AND p.\"Position\" = f.pos")}) RETURNING {Now} AS "Value" """, Expect.Fence, rows.Count);
    }

    public override void Checked(OwnedCommand c, string dml, int count) => c.Statement(dml + " RETURNING 1", Expect.Count, count);
    public override void Free(OwnedCommand c, string dml) => c.Statement(dml + " RETURNING 1");
    public override void InsertMessages(OwnedCommand c, string insert, int count) => c.Statement(insert + " RETURNING \"Seq\"", Expect.Seq, count);
    /// <summary>A row-value IN over a VALUES list: PostgreSQL and SQLite both take it as a subquery.</summary>
    public override void KeyedMessages(OwnedCommand c, string action, IReadOnlyList<string[]> keys, bool exact) =>
        c.Statement($"""{action} WHERE "Done" = {False} AND "EventMatch" IS NULL AND {KeyColumns(exact)} IN (VALUES {KeyList(keys)}) RETURNING 1""", Expect.Count, keys.Count);
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
    /// <summary>The engine's dialect, or null where the store still commits through EF (MySQL: round 5 B4).</summary>
    public static CommitDialect? For(Engine? engine, bool postgres) => engine switch
    {
        null when postgres => new PgCommitDialect(),
        null => new SqliteCommitDialect(),
        SqlServerEngine => new SqlServerCommitDialect(),
        _ => null,
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
    public override bool ResultPerStatement => false;
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

    private static string Throw(int number, string what) => $"THROW {number}, '{what}', 1";

    public override void Fence(OwnedCommand c, IReadOnlyList<FenceRow> rows, long leaseMs, CommitWindow? window, bool unfenced)
    {
        if (window is not null) throw new NotSupportedException("Commit windows are PostgreSQL's.");
        var lease = FormattableString.Invariant($"\"LeaseUntil\" = {Now} + {leaseMs}");
        string matched;
        if (rows.Count == 1)
        {
            var r = rows[0];
            matched = FormattableString.Invariant($"""UPDATE "Partitions" SET "Position" = {r.NewPosition}, {lease} WHERE "Id" = {r.Id} AND ({(unfenced ? "1 = 1" : FormattableString.Invariant($"\"Epoch\" = {r.Epoch} AND \"Position\" = {r.Position}"))})""");
        }
        else
        {
            // k rows: an update lock on each in ascending Id order first (the clustered key, as the take's range takes them), then one UPDATE.
            c.Statement($"""IF (SELECT COUNT(*) FROM "Partitions" WITH (UPDLOCK, ROWLOCK) WHERE "Id" IN ({string.Join(", ", rows.Select(r => r.Id))})) <> {rows.Count} {Throw(FenceMissed, "kernel fence: a partition row is missing")}""");
            matched = $"""UPDATE p SET "Position" = f.np, {lease} FROM "Partitions" p JOIN (VALUES {Values(rows)}) AS f(id, epoch, pos, np) ON p."Id" = f.id WHERE {(unfenced ? "1 = 1" : "p.\"Epoch\" = f.epoch AND p.\"Position\" = f.pos")}""";
        }
        // The fence's count is checked twice: by the server, which stops the batch, and by the client, which reads it back (the FenceIgnoresCount mutation skips the client's).
        c.Statement(matched);
        c.Statement($"DECLARE @kernel_fenced int = @@ROWCOUNT; IF @kernel_fenced <> {rows.Count} {Throw(FenceMissed, "kernel fence: the partition is no longer held by this owner at this position")}");
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
    public override void KeyedMessages(OwnedCommand c, string action, IReadOnlyList<string[]> keys, bool exact) =>
        Checked(c, $"""{action} WHERE "Done" = 0 AND "EventMatch" IS NULL AND EXISTS (SELECT 1 FROM (VALUES {KeyList(keys)}) AS k("Id", "InboxScope"{(exact ? ", \"IssuedWire\"" : "")}) WHERE k."Id" = "Messages"."Id" AND k."InboxScope" = "Messages"."InboxScope"{(exact ? " AND k.\"IssuedWire\" = \"Messages\".\"IssuedWire\"" : "")})""", keys.Count);

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
