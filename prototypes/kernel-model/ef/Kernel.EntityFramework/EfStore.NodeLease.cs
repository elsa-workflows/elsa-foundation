using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kernel.EntityFramework;

/// <summary>
/// K1b, the node lease on the EF store (ownership-and-fencing.md, "The partition record" and "The node's heartbeat"):
/// the take's order and bounds, the node heartbeat, and the retake that writes the taker's record and may count deaths.
/// </summary>
public sealed partial class EfStore
{
    /// <summary>
    /// MUTATION, for NL-9: the take reads the owners' node records as a subquery of its update, after it has locked
    /// partition rows; under SQL Server's locking READ COMMITTED, and on MySQL, whose subquery in an update is a locking read.
    /// </summary>
    public bool NodeReadInUpdate { get; init; }

    /// <summary>MUTATION, for NL-9: the take's transaction runs at REPEATABLE READ, so its read of a node record holds until it ends.</summary>
    public bool RepeatableReadTake { get; init; }

    /// <summary>MUTATION, for NL-9: the heartbeat reads membership (the node count and the free list) inside its transaction, while it holds its own record.</summary>
    public bool MembershipInHeartbeat { get; init; }

    /// <summary>
    /// The take's transaction. NL-C1: READ COMMITTED on every engine, whatever the store's configured isolation; SQLite
    /// has one writer, so its take cannot hold a read lock while it waits. Its lock waits are bounded by the lease: the
    /// engine's lock timeout (whole milliseconds; MySQL's in whole seconds, at least one) and the command timeout (whole
    /// seconds, at least one), which is also how long SQLite's provider retries a busy database. A timeout fails the
    /// statement; disposing the transaction rolls the take back, and the caller takes again at its next round. SQL Server's
    /// pool resets the session's LOCK_TIMEOUT when the connection is next taken; MySQL's session keeps its setting, so the
    /// take puts it back (<see cref="Reset"/>). Both read back by <c>x locktimeout</c> (slice 6a).
    /// </summary>
    private async Task<(IDbContextTransaction Transaction, IAsyncDisposable? Reset)> BeginNodeTakeAsync(KernelDb db, TimeSpan lease, CancellationToken cancellationToken)
    {
        db.Database.SetCommandTimeout(Math.Max(1, (int)lease.TotalSeconds));
        var transaction = await (RepeatableReadTake && (Engine is not null || postgres) ? db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken) : BeginReadCommittedAsync(db, cancellationToken));
        var ms = Math.Max(1, (long)lease.TotalMilliseconds);
        var bound = Engine switch
        {
            SqlServerEngine => $"SET LOCK_TIMEOUT {ms}",
            MySqlEngine => $"SET SESSION innodb_lock_wait_timeout = {Math.Max(1, ms / 1000)}",
            null when postgres => $"SET LOCAL lock_timeout = {ms}",
            _ => null,
        };
        var reset = Engine is MySqlEngine ? new Reset(db) : null;
        try
        {
            if (bound is not null) await db.Database.ExecuteSqlRawAsync(bound, cancellationToken);
            return (transaction, reset);
        }
        catch
        {
            try { if (reset is not null) await reset.DisposeAsync(); }
            finally { await transaction.DisposeAsync(); }
            throw;
        }
    }

    /// <summary>
    /// #4880 section 3, item 4: on MySQL the take's lock timeout stayed on the pooled session after the take (read back 9
    /// for a 9 s lease, against the default 50). The take sets it back to the default as its last step, before its
    /// transaction ends whether it committed or not; a reset that fails is on a broken session, which leaves the pool.
    /// </summary>
    private sealed class Reset(KernelDb db) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await db.Database.ExecuteSqlRawAsync("SET SESSION innodb_lock_wait_timeout = DEFAULT"); }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// The store's commit-step deadline under the node lease: when the monotonic time since the batch was sent has passed
    /// the lease (<see cref="CommitBatch.Lease"/>), the owner's guard has expired too, so the store rolls back instead of
    /// sending COMMIT and answers "not landed". This covers a client that pauses between the batch's answer and COMMIT.
    /// </summary>
    private bool PastCommitStep(long sent, CommitBatch batch) => System.Diagnostics.Stopwatch.GetElapsedTime(sent) > batch.Lease;

    private Task<IDbContextTransaction> BeginReadCommittedAsync(KernelDb db, CancellationToken cancellationToken) =>
        Engine is null && !postgres ? db.Database.BeginTransactionAsync(cancellationToken) : db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

    /// <summary>
    /// Steps 1 and 2 of a take, under one deadline of a third of the lease (the client's cancel; K4 qualifies the server
    /// side). 1: the owners of <paramref name="ids"/> other than this node, then their node records: two plain reads,
    /// neither a locking read nor a subquery of the update, so the read of a node record holds no partition row. A missing
    /// record counts as expired. 2: this node's own record (<c>SeenAt</c>, <c>LeaseUntil</c>), whatever the take gets.
    /// Returns the expired owners, which step 3 may take from.
    /// </summary>
    private async Task<List<string>> ReadAndRenewAsync(KernelDb db, string? ids, string node, TimeSpan lease, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(lease / 3);
        try
        {
            var expired = new List<string>();
            if (ids is not null)
            {
                var owners = await db.Database.SqlQueryRaw<string>(Q($"""SELECT DISTINCT "Owner" AS "Value" FROM "Partitions" WHERE "Id" IN ({ids}) AND "Owner" IS NOT NULL AND "Owner" <> {"{0}"}"""), node).ToListAsync(deadline.Token);
                if (owners.Count > 0)
                {
                    var slots = string.Join(", ", owners.Select((_, i) => $"{{{i}}}"));
                    var live = await db.Database.SqlQueryRaw<string>(Q($"""SELECT "Id" AS "Value" FROM "Nodes" WHERE "Id" IN ({slots}) AND "LeaseUntil" >= {Now}"""), [.. owners]).ToListAsync(deadline.Token);
                    expired = [.. owners.Except(live, StringComparer.Ordinal)];
                }
            }
            await RenewAsync(db, node, lease, deadline.Token);
            return expired;
        }
        catch (Exception e) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new StoreException(Failure.Transient, "The take's read of the node records and its own record's write outlasted a third of the lease.", e);
        }
    }

    /// <summary>The node record's write: <c>SeenAt</c> at database time, <c>LeaseUntil</c> at that time plus the lease. Returns the time.</summary>
    private async Task<long> RenewAsync(KernelDb db, string node, TimeSpan lease, CancellationToken cancellationToken) =>
        Engine is not null ? await Engine.HeartbeatAsync(db, node, cancellationToken, (long)lease.TotalMilliseconds) : (await db.Database.SqlQueryRaw<long>(
            // Database time read once, so LeaseUntil is exactly SeenAt plus the lease (as SQL Server's MERGE). SQLite needs
            // the WHERE before ON CONFLICT after a SELECT.
            $"""INSERT INTO "Nodes" ("Id", "SeenAt", "LeaseUntil") SELECT {"{0}"}, t.now, t.now + {"{1}"} FROM (SELECT {Now} AS now) t WHERE true ON CONFLICT ("Id") DO UPDATE SET "SeenAt" = excluded."SeenAt", "LeaseUntil" = excluded."LeaseUntil" RETURNING "SeenAt" AS "Value" """,
            node, (long)lease.TotalMilliseconds).ToListAsync(cancellationToken))[0];

    /// <summary>
    /// The node heartbeat: the node record's write and the read of this node's partitions with their epochs, one
    /// transaction at READ COMMITTED under a timeout of a third of the lease. The membership reads run after it, outside,
    /// so a heartbeat never reads another node record while it holds its own.
    /// </summary>
    public async ValueTask<NodeBeat> NodeHeartbeatAsync(string node, TimeSpan lease, TimeSpan window, CancellationToken cancellationToken = default)
    {
        long now;
        List<OwnedEpoch> owned;
        Cluster? inside = null;
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(lease / 3);
            try
            {
                await using var db = create();
                await using var transaction = await BeginReadCommittedAsync(db, deadline.Token);
                // PostgreSQL bounds the heartbeat on the server too (NL-10's bound rests on it); SET LOCAL ends with the transaction.
                if (Engine is null && postgres) await db.Database.ExecuteSqlRawAsync(FormattableString.Invariant($"SET LOCAL statement_timeout = {Math.Max(1, (long)(lease / 3).TotalMilliseconds)}"), deadline.Token);
                now = await RenewAsync(db, node, lease, deadline.Token);
                owned = await db.Partitions.AsNoTracking().Where(p => p.Owner == node).OrderBy(p => p.Id).Select(p => new OwnedEpoch(p.Id, p.Epoch)).ToListAsync(deadline.Token);
                if (MembershipInHeartbeat) inside = await MembershipAsync(db, now, window, deadline.Token);     // MUTATION
                await transaction.CommitAsync(deadline.Token);
            }
            catch (Exception e) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new StoreException(Failure.Unknown, "The heartbeat outlasted a third of the lease.", e);
            }
        }
        if (inside is not null) return new NodeBeat(owned, now, inside);
        await using var read = create();
        return new NodeBeat(owned, now, await MembershipAsync(read, now, window, cancellationToken));
    }

    /// <summary>
    /// Under the node lease, a take that is a deadlock victim, a serialization failure, timed out on a lock or met a busy
    /// database (SQL Server 1205 and 1222, MySQL 1213 and 1205, PostgreSQL 40P01, 40001 and 55P03, SQLite 5 and 6) failed
    /// as a transient error, as on the commit path: its transaction rolled back, and it is taken again at the next round.
    /// Null for anything else, which propagates as it is.
    /// </summary>
    private StoreException? TransientTake(Exception e) =>
        e is not StoreException && Classify(e) is { Failure: Failure.Transient } transient ? transient : null;

    /// <summary>
    /// The retake in one transaction: under the node lease the taker's record is written first, also when the retake is
    /// refused; with <paramref name="countDeaths"/> (#4837, a pending partition) a successful retake adds one attempt to
    /// every unfinished message on trial at the previous epoch, as a take from an expired owner does.
    /// </summary>
    private async ValueTask<Lease?> RetakeInTransactionAsync(int partition, string node, long epoch, TimeSpan lease, bool countDeaths, CancellationToken cancellationToken)
    {
        try { return await RetakeInOneTransactionAsync(partition, node, epoch, lease, countDeaths, cancellationToken); }
        catch (Exception e) when (TransientTake(e) is { } transient) { throw transient; }
    }

    private async ValueTask<Lease?> RetakeInOneTransactionAsync(int partition, string node, long epoch, TimeSpan lease, bool countDeaths, CancellationToken cancellationToken)
    {
        await using var db = create();
        var begun = await BeginNodeTakeAsync(db, lease, cancellationToken);
        await using var transaction = begun.Transaction;
        await using var reset = begun.Reset;
        await ReadAndRenewAsync(db, null, node, lease, cancellationToken);
        var update = $"""UPDATE "Partitions" SET "Epoch" = "Epoch" + 1""";
        const string mine = """ "Id" = {0} AND "Owner" = {1} AND "Epoch" = {2}""";
        object[] parameters = [partition, node, epoch];
        List<Engine.TakeRow> rows;
        if (Engine is MySqlEngine)
            rows = await db.Database.ExecuteSqlRawAsync(Q($"{update} WHERE{mine}"), parameters, cancellationToken) != 1 ? []
                : await db.Database.SqlQueryRaw<Engine.TakeRow>(Q($"""SELECT "Epoch", "Position", {Now} AS "Now", "Died" FROM "Partitions" WHERE "Id" = {"{0}"}"""), partition).ToListAsync(cancellationToken);
        else rows = await db.Database.SqlQueryRaw<Engine.TakeRow>(Engine is SqlServerEngine
                ? $"""{update} OUTPUT inserted."Epoch", inserted."Position", {Now} AS "Now", inserted."Died" WHERE{mine}"""
                : $"""{update} WHERE{mine} RETURNING "Epoch", "Position", {Now} AS "Now", "Died" """, parameters).ToListAsync(cancellationToken);
        if (rows is [var row] && countDeaths)
            await db.Database.ExecuteSqlRawAsync(Q($"""UPDATE "Messages" SET "Attempts" = "Attempts" + 1 WHERE "Partition" = {"{0}"} AND "Trial" = {"{1}"} AND "Done" = {False}"""), [partition, row.Epoch - 1], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows is [var taken] ? new Lease(taken.Epoch, taken.Position, taken.Now) : null;
    }
}
