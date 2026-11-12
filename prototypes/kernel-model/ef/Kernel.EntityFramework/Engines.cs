using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using MySql.Data.MySqlClient;

namespace Kernel.EntityFramework;

/// <summary>
/// What a database engine other than PostgreSQL and SQLite does differently: the statements the store writes by
/// hand, and what its errors mean. <see cref="EfStore"/> keeps the protocol; everything here is one engine's dialect.
/// Findings are in ENGINES.md and named E1, E2, ... in the comments.
/// </summary>
public abstract class Engine
{
    /// <summary>Database time in milliseconds, as an SQL expression.</summary>
    public abstract string Now { get; }

    /// <summary>The store's SQL is written with "double quotes" around names.</summary>
    public virtual string Sql(string sql) => sql;

    /// <summary>The poll, with {0} partition, {1} epoch, {2} horizon in milliseconds and {3} limit.</summary>
    public abstract string Poll { get; }

    /// <summary>The take's UPDATE, inside the store's transaction: epoch, position, database time and whether another owner's lease expired; null when not taken.</summary>
    public abstract ValueTask<TakeRow?> TakeAsync(KernelDb db, int partition, string node, long lease, CancellationToken cancellationToken);

    /// <summary>The isolation level of the kernel's transactions; null is the server's default.</summary>
    public System.Data.IsolationLevel? Isolation { get; init; }

    /// <summary>
    /// ENGINES.md E4. Entity Framework sets a savepoint before every SaveChanges inside a transaction, and releases it
    /// after where the engine has a release. Each is a round trip of its own (counted on the TCP stream: one per commit
    /// on SQL Server, two on PostgreSQL), for nothing: a commit that fails is rolled back whole. Off unless asked for.
    /// <see cref="EfStore"/> turns it off for PostgreSQL and SQLite too.
    /// </summary>
    public bool Savepoints { get; init; }

    /// <summary>Begins the commit's transaction.</summary>
    public virtual Task<IDbContextTransaction> BeginAsync(KernelDb db, CancellationToken cancellationToken)
    {
        db.Database.AutoSavepointsEnabled = Savepoints;
        return Isolation is { } level ? db.Database.BeginTransactionAsync(level, cancellationToken) : db.Database.BeginTransactionAsync(cancellationToken);
    }

    /// <summary>Begins the take's transaction: by default as the commit's.</summary>
    public virtual Task<IDbContextTransaction> BeginTakeAsync(KernelDb db, CancellationToken cancellationToken) => BeginAsync(db, cancellationToken);

    /// <summary>Runs once, after the tables are created: database settings the engine needs.</summary>
    public virtual Task CreatedAsync(KernelDb db, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Statement 1 of the commit. Database time when it changed exactly one row, null when it matched nothing.</summary>
    public abstract ValueTask<long?> FenceAsync(KernelDb db, CommitBatch batch, bool unfenced, CancellationToken cancellationToken);

    /// <summary>False when the id exists already.</summary>
    public abstract ValueTask<bool> AddAsync(KernelDb db, MessageEntity message, CancellationToken cancellationToken);

    public abstract ValueTask PublishAsync(KernelDb db, EventRow @event, CancellationToken cancellationToken);

    /// <summary>What one of this engine's own exceptions means; null when the exception is not this engine's.</summary>
    public abstract Failure? Classify(Exception exception);

    /// <summary>The engine's part of the model: how <c>seq</c> is assigned, and the indexes the locking engines need.</summary>
    public static void Model(DatabaseFacade database, ModelBuilder model)
    {
        if (database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
            model.Entity<MessageEntity>().Property(m => m.Seq).ValueGeneratedOnAdd().UseIdentityColumn();
        if (database.ProviderName == "MySql.EntityFrameworkCore")
            MySqlEngine.Messages(model);
        // ENGINES.md E8. The take's "attempts + 1 WHERE partition = p AND trial = e" had no index to use: on InnoDB it
        // scanned the clustered index and, under REPEATABLE READ, locked every row and gap of the whole table (4,622 row
        // locks), and deadlocked with another partition's commit inserting a message; on SQL Server it took an update
        // lock on every row it passed and waited behind every partition's uncommitted inserts. With this index it
        // touches the rows it changes.
        if (database.ProviderName is "Microsoft.EntityFrameworkCore.SqlServer" or "MySql.EntityFrameworkCore")
            model.Entity<MessageEntity>().HasIndex(m => new { m.Partition, m.Trial });
    }

    protected static object Value(object? value) => value ?? DBNull.Value;

    public sealed class TakeRow
    {
        public long Epoch { get; set; }
        public long Position { get; set; }
        public long Now { get; set; }
        public bool Died { get; set; }
    }

    public sealed class FenceRow
    {
        public long Matched { get; set; }
        public long Now { get; set; }
    }
}

/// <summary>SQL Server: OUTPUT in place of RETURNING, TOP in place of LIMIT, no ON CONFLICT.</summary>
public sealed class SqlServerEngine : Engine
{
    /// <summary>Adds WITH (READPAST) to the poll. For the experiment only: it breaks the order rule (ENGINES.md E6).</summary>
    public bool ReadPast { get; init; }

    /// <summary>
    /// ENGINES.md E5. Under locking READ COMMITTED the poll waits behind any uncommitted insert, mark or delete it reaches,
    /// and keeps its shared locks on rows with nvarchar(max) values until the statement ends, so a commit deadlocks with
    /// it and waits for SQL Server's deadlock monitor (rounds of up to 5 s; 11 s seen). With READ_COMMITTED_SNAPSHOT the
    /// poll reads the last committed rows and takes no locks. On unless switched off.
    /// </summary>
    public bool ReadCommittedSnapshot { get; init; } = true;

    public override async Task CreatedAsync(KernelDb db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync($"ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT {(ReadCommittedSnapshot ? "ON" : "OFF")} WITH ROLLBACK IMMEDIATE", cancellationToken);
        SqlConnection.ClearAllPools();
    }

    public override string Now => "DATEDIFF_BIG(millisecond, '1970-01-01', SYSUTCDATETIME())";

    public override string Poll =>
        $"""
         SELECT TOP ({"{3}"}) t.now AS "Now", m."Id", m."Seq", m."Target", m."Kind", m."Payload", m."Sender", m."Start", m."Due", m."Seen", m."Attempts", m."Trial", m."KeepUntil"
         FROM (SELECT {Now} AS now) t
         LEFT JOIN "Messages" m{(ReadPast ? " WITH (READPAST)" : "")} ON m."Partition" = {"{0}"} AND m."Seen" < {"{1}"} AND m."Done" = 0 AND m."Due" <= t.now + {"{2}"}
         ORDER BY m."Seq"
         """;

    public override async ValueTask<TakeRow?> TakeAsync(KernelDb db, int partition, string node, long lease, CancellationToken cancellationToken)
    {
        // T-SQL has no boolean expressions: CASE. Every SET expression reads the row as it was before the UPDATE.
        var rows = await db.Database.SqlQueryRaw<TakeRow>(
            $"""
             UPDATE "Partitions" SET "Died" = CASE WHEN "Owner" IS NOT NULL AND "Owner" <> {"{0}"} THEN 1 ELSE 0 END, "Owner" = {"{0}"}, "Epoch" = "Epoch" + 1, "LeaseUntil" = {Now} + {"{1}"}
             OUTPUT inserted."Epoch" AS "Epoch", inserted."Position" AS "Position", {Now} AS "Now", inserted."Died" AS "Died"
             WHERE "Id" = {"{2}"} AND ("Owner" IS NULL OR "LeaseUntil" < {Now} OR "Owner" = {"{0}"})
             """, node, lease, partition).ToListAsync(cancellationToken);
        return rows.Count == 1 ? rows[0] : null;
    }

    public override async ValueTask<long?> FenceAsync(KernelDb db, CommitBatch batch, bool unfenced, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQueryRaw<long>(
            $"""
             UPDATE "Partitions" SET "Position" = {"{0}"}, "LeaseUntil" = {Now} + {"{1}"}
             OUTPUT {Now} AS "Value"
             WHERE "Id" = {"{2}"} AND ("Epoch" = {"{3}"} AND "Position" = {"{4}"} OR {(unfenced ? "1 = 1" : "1 = 0")})
             """, batch.Position + batch.Positions, (long)batch.Lease.TotalMilliseconds, batch.Partition, batch.Epoch, batch.Position).ToListAsync(cancellationToken);
        return rows.Count == 1 ? rows[0] : null;
    }

    public override async ValueTask<bool> AddAsync(KernelDb db, MessageEntity m, CancellationToken cancellationToken)
    {
        // There is no ON CONFLICT DO NOTHING. A plain insert whose key violation is the answer "it exists already":
        // outside a transaction error 2627 ends the statement and nothing else.
        try
        {
            return await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Messages" ("Id", "Partition", "Target", "Kind", "Payload", "Sender", "Start", "Due", "Seen", "Attempts", "Trial", "KeepUntil", "Done")
                VALUES (@id, @partition, @target, @kind, @payload, @sender, @start, @due, 0, 0, 0, @keep, 0)
                """,
                [
                    new SqlParameter("@id", m.Id), new SqlParameter("@partition", m.Partition), new SqlParameter("@target", m.Target), new SqlParameter("@kind", m.Kind),
                    Text("@payload", m.Payload), Text("@sender", m.Sender), Text("@start", m.Start), new SqlParameter("@due", m.Due),
                    new SqlParameter("@keep", System.Data.SqlDbType.BigInt) { Value = Value(m.KeepUntil) },
                ], cancellationToken) == 1;
        }
        catch (SqlException e) when (e.Number is 2627 or 2601) { return false; }
    }

    public override async ValueTask PublishAsync(KernelDb db, EventRow e, CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "Events" ("Key", "Id", "Payload", "KeepUntil") VALUES (@key, @id, @payload, @keep)""",
                [new SqlParameter("@key", e.Key), new SqlParameter("@id", e.Id), Text("@payload", e.Payload), new SqlParameter("@keep", e.KeepUntil)], cancellationToken);
        }
        catch (SqlException x) when (x.Number is 2627 or 2601) { }
    }

    private static SqlParameter Text(string name, string? value) => new(name, System.Data.SqlDbType.NVarChar, -1) { Value = Value(value) };

    public override Failure? Classify(Exception exception) => exception is not SqlException e ? null : e.Number switch
    {
        1205 => Failure.Transient,                                  // deadlock victim: the whole transaction was rolled back
        1222 => Failure.Transient,                                  // lock request time-out: the statement did not happen, and the store rolls back
        2627 or 2601 or 547 or 515 or 8152 or 2628 => Failure.Definite,    // key violation, constraint, null, value too long
        _ => Failure.Unknown,                                       // a killed session, a broken connection, a time-out
    };
}

/// <summary>MySQL 8 (InnoDB), on Oracle's provider and Connector/NET: no RETURNING, no ON CONFLICT, backticks around names.</summary>
public sealed class MySqlEngine : Engine
{
    // Isolation has to be passed at BEGIN: Connector/NET sends SET TRANSACTION ISOLATION LEVEL before every BEGIN
    // (REPEATABLE READ when none is given), so a level set on the session alone is overwritten.

    public override string Now => "CAST(UNIX_TIMESTAMP(SYSDATE(3)) * 1000 AS SIGNED)";

    /// <summary>
    /// ENGINES.md E8. The take runs at READ COMMITTED whatever <see cref="Engine.Isolation"/> says: under REPEATABLE READ
    /// even the indexed "attempts + 1" takes next-key locks on the gap before the first row it changes, and a message
    /// another partition inserts into that gap waits for the take. Null keeps the commit's level (the experiment).
    /// </summary>
    public System.Data.IsolationLevel? TakeIsolation { get; init; } = System.Data.IsolationLevel.ReadCommitted;

    public override Task<IDbContextTransaction> BeginTakeAsync(KernelDb db, CancellationToken cancellationToken) =>
        TakeIsolation is { } level ? db.Database.BeginTransactionAsync(level, cancellationToken) : BeginAsync(db, cancellationToken);

    public override string Sql(string sql) => sql.Replace('"', '`');

    public override string Poll => Sql(
        $"""
         SELECT t.now AS "Now", m."Id", m."Seq", m."Target", m."Kind", m."Payload", m."Sender", m."Start", m."Due", m."Seen", m."Attempts", m."Trial", m."KeepUntil"
         FROM (SELECT {Now} AS now) t
         LEFT JOIN "Messages" m ON m."Partition" = {"{0}"} AND m."Seen" < {"{1}"} AND m."Done" = 0 AND m."Due" <= t.now + {"{2}"}
         ORDER BY m."Seq" LIMIT {"{3}"}
         """);

    public static void Messages(ModelBuilder model) => model.Entity<MessageEntity>(e =>
    {
        // InnoDB wants the AUTO_INCREMENT column to lead a key of its own, in the CREATE TABLE.
        e.Property(m => m.Seq).ValueGeneratedOnAdd();
        e.HasAlternateKey(m => m.Seq);
    });

    public override async ValueTask<TakeRow?> TakeAsync(KernelDb db, int partition, string node, long lease, CancellationToken cancellationToken)
    {
        // There is no RETURNING. The take and the read of what it wrote are in the store's one transaction, so the row
        // lock of the UPDATE is still held when the SELECT reads epoch and position: nobody else's take can be read by
        // mistake. MySQL evaluates SET assignments left to right, so "Died" must come before "Owner" changes.
        var taken = await db.Database.ExecuteSqlRawAsync(Sql(
            $"""UPDATE "Partitions" SET "Died" = ("Owner" IS NOT NULL AND "Owner" <> {"{0}"}), "Owner" = {"{0}"}, "Epoch" = "Epoch" + 1, "LeaseUntil" = {Now} + {"{1}"} WHERE "Id" = {"{2}"} AND ("Owner" IS NULL OR "LeaseUntil" < {Now} OR "Owner" = {"{0}"})"""),
            [node, lease, partition], cancellationToken);
        if (taken != 1) return null;
        var row = await db.Database.SqlQueryRaw<TakeRow>(Sql($"""SELECT "Epoch", "Position", {Now} AS "Now", "Died" FROM "Partitions" WHERE "Id" = {"{0}"}"""), partition).ToListAsync(cancellationToken);
        return row[0];
    }

    public override async ValueTask<long?> FenceAsync(KernelDb db, CommitBatch batch, bool unfenced, CancellationToken cancellationToken)
    {
        // One command, two statements: the fence, then how many rows it matched and the time. The connection must
        // count matched rows, not changed ones (UseAffectedRows=false, Connector/NET's default).
        var rows = await db.Database.SqlQueryRaw<FenceRow>(Sql(
            $"""
             UPDATE "Partitions" SET "Position" = {"{0}"}, "LeaseUntil" = {Now} + {"{1}"} WHERE "Id" = {"{2}"} AND ("Epoch" = {"{3}"} AND "Position" = {"{4}"} OR {(unfenced ? "1 = 1" : "1 = 0")});
             SELECT ROW_COUNT() AS "Matched", {Now} AS "Now"
             """), batch.Position + batch.Positions, (long)batch.Lease.TotalMilliseconds, batch.Partition, batch.Epoch, batch.Position).ToListAsync(cancellationToken);
        return rows.Count == 1 && rows[0].Matched == 1 ? rows[0].Now : null;
    }

    public override async ValueTask<bool> AddAsync(KernelDb db, MessageEntity m, CancellationToken cancellationToken) =>
        // INSERT IGNORE: a duplicate key is not an error and changes no row.
        await db.Database.ExecuteSqlRawAsync(Sql(
            """
            INSERT IGNORE INTO "Messages" ("Id", "Partition", "Target", "Kind", "Payload", "Sender", "Start", "Due", "Seen", "Attempts", "Trial", "KeepUntil", "Done")
            VALUES (@id, @partition, @target, @kind, @payload, @sender, @start, @due, 0, 0, 0, @keep, 0)
            """),
            [
                new MySqlParameter("@id", m.Id), new MySqlParameter("@partition", m.Partition), new MySqlParameter("@target", m.Target), new MySqlParameter("@kind", m.Kind),
                new MySqlParameter("@payload", Value(m.Payload)), new MySqlParameter("@sender", Value(m.Sender)), new MySqlParameter("@start", Value(m.Start)),
                new MySqlParameter("@due", m.Due), new MySqlParameter("@keep", Value(m.KeepUntil)),
            ], cancellationToken) == 1;

    public override async ValueTask PublishAsync(KernelDb db, EventRow e, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlRawAsync(Sql("""INSERT IGNORE INTO "Events" ("Key", "Id", "Payload", "KeepUntil") VALUES (@key, @id, @payload, @keep)"""),
            [new MySqlParameter("@key", e.Key), new MySqlParameter("@id", e.Id), new MySqlParameter("@payload", Value(e.Payload)), new MySqlParameter("@keep", e.KeepUntil)], cancellationToken);

    public override Failure? Classify(Exception exception) => exception is not MySqlException e ? null : e.Number switch
    {
        1213 => Failure.Transient,                          // deadlock victim: InnoDB rolled the whole transaction back
        1205 => Failure.Transient,                          // lock wait time-out: the statement did not happen, and the store rolls back
        1062 or 1406 or 1048 or 1264 => Failure.Definite,   // duplicate key, data too long, null, out of range
        _ => Failure.Unknown,                               // a killed connection, a lost connection
    };
}
