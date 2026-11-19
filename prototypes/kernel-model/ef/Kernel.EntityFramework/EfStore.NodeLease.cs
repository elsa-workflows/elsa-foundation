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
    /// The take's transaction. NL-C1: READ COMMITTED on every engine, whatever the store's configured isolation; SQLite
    /// has one writer, so its take cannot hold a read lock while it waits. Its lock waits are bounded by the lease: the
    /// engine's lock timeout (whole milliseconds; MySQL's in whole seconds, at least one) and the command timeout (whole
    /// seconds, at least one), which is also how long SQLite's provider retries a busy database. A timeout fails the
    /// statement; disposing the transaction rolls the take back, and the caller takes again at its next round.
    /// </summary>
    private async Task<IDbContextTransaction> BeginNodeTakeAsync(KernelDb db, TimeSpan lease, CancellationToken cancellationToken)
    {
        db.Database.SetCommandTimeout(Math.Max(1, (int)lease.TotalSeconds));
        var transaction = await BeginReadCommittedAsync(db, cancellationToken);
        var ms = Math.Max(1, (long)lease.TotalMilliseconds);
        var bound = Engine switch
        {
            SqlServerEngine => $"SET LOCK_TIMEOUT {ms}",
            MySqlEngine => $"SET SESSION innodb_lock_wait_timeout = {Math.Max(1, ms / 1000)}",
            null when postgres => $"SET LOCAL lock_timeout = {ms}",
            _ => null,
        };
        if (bound is not null) await db.Database.ExecuteSqlRawAsync(bound, cancellationToken);
        return transaction;
    }

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
            $"""INSERT INTO "Nodes" ("Id", "SeenAt", "LeaseUntil") VALUES ({"{0}"}, {Now}, {Now} + {"{1}"}) ON CONFLICT ("Id") DO UPDATE SET "SeenAt" = excluded."SeenAt", "LeaseUntil" = excluded."LeaseUntil" RETURNING "SeenAt" AS "Value" """,
            node, (long)lease.TotalMilliseconds).ToListAsync(cancellationToken))[0];

    /// <summary>
    /// The node heartbeat: the node record's write and the read of this node's partitions with their epochs, one
    /// transaction at READ COMMITTED under a timeout of a third of the lease. The membership reads run after it, outside,
    /// so a heartbeat never reads another node record while it holds its own.
    /// </summary>
    public async ValueTask<NodeBeat> NodeHeartbeatAsync(string node, TimeSpan lease, TimeSpan window, CancellationToken cancellationToken = default)
    {
        if (!NodeLease) throw new NotSupportedException("The node lease is off in this store.");
        long now;
        List<OwnedEpoch> owned;
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(lease / 3);
            try
            {
                await using var db = create();
                await using var transaction = await BeginReadCommittedAsync(db, deadline.Token);
                now = await RenewAsync(db, node, lease, deadline.Token);
                owned = await db.Partitions.AsNoTracking().Where(p => p.Owner == node).OrderBy(p => p.Id).Select(p => new OwnedEpoch(p.Id, p.Epoch)).ToListAsync(deadline.Token);
                await transaction.CommitAsync(deadline.Token);
            }
            catch (Exception e) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new StoreException(Failure.Unknown, "The heartbeat outlasted a third of the lease.", e);
            }
        }
        await using var read = create();
        return new NodeBeat(owned, now, await MembershipAsync(read, now, window, cancellationToken));
    }

    /// <summary>
    /// The retake in one transaction: under the node lease the taker's record is written first, also when the retake is
    /// refused; with <paramref name="countDeaths"/> (#4837, a pending partition) a successful retake adds one attempt to
    /// every unfinished message on trial at the previous epoch, as a take from an expired owner does.
    /// </summary>
    private async ValueTask<Lease?> RetakeInTransactionAsync(int partition, string node, long epoch, TimeSpan lease, bool countDeaths, CancellationToken cancellationToken)
    {
        await using var db = create();
        await using var transaction = await (NodeLease ? BeginNodeTakeAsync(db, lease, cancellationToken) : Engine?.BeginTakeAsync(db, cancellationToken) ?? db.Database.BeginTransactionAsync(cancellationToken));
        if (NodeLease) await ReadAndRenewAsync(db, null, node, lease, cancellationToken);
        var update = $"""UPDATE "Partitions" SET "Epoch" = "Epoch" + 1{(NodeLease ? "" : $", \"LeaseUntil\" = {Now} + {"{3}"}")}""";
        const string mine = """ "Id" = {0} AND "Owner" = {1} AND "Epoch" = {2}""";
        object[] parameters = NodeLease ? [partition, node, epoch] : [partition, node, epoch, (long)lease.TotalMilliseconds];
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
