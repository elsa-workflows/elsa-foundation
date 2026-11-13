using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.EntityFramework;

public sealed class PartitionEntity
{
    public int Id { get; set; }
    public string? Owner { get; set; }
    public long Epoch { get; set; }
    public long LeaseUntil { get; set; }
    public long Position { get; set; }
    public bool Died { get; set; }          // the last take found another owner's lease expired
}

public sealed class NodeEntity
{
    public string Id { get; set; } = "";
    public long SeenAt { get; set; }        // database time of the last heartbeat
}

public sealed class InstanceEntity
{
    public string Id { get; set; } = "";
    public int Partition { get; set; }
    public string Model { get; set; } = "";
    public string Definition { get; set; } = "";
    public InstanceStatus Status { get; set; }
    public string State { get; set; } = "";
    public string? Jobs { get; set; }
    public long Revision { get; set; }
    public string? Parent { get; set; }
    public string? Fault { get; set; }
    public History History { get; set; }
    public bool Suspended { get; set; }
    public string? Children { get; set; }
    public int StateVersion { get; set; }
}

public sealed class ProjectionEntity
{
    public int Partition { get; set; }
    public long Position { get; set; }
}

public sealed class ViewEntity
{
    public string Instance { get; set; } = "";
    public int Partition { get; set; }
    public long Revision { get; set; }
    public int Commits { get; set; }
}

public sealed class MessageEntity
{
    public string Id { get; set; } = "";
    public long Seq { get; set; }
    public int Partition { get; set; }
    public string Target { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Payload { get; set; }
    public string? Sender { get; set; }
    public string? Start { get; set; }
    public long Due { get; set; }           // 0 when the message is due at once
    public long Seen { get; set; }
    public int Attempts { get; set; }
    public long Trial { get; set; }         // the epoch at which it was last put on trial; 0 when never
    public long? KeepUntil { get; set; }
    public bool Done { get; set; }
    public string? ReplyTo { get; set; }    // a caller waiting on another node for what the turn that consumes it emits
}

public sealed class SubscriptionEntity
{
    public string Key { get; set; } = "";
    public string Instance { get; set; } = "";
    public string Wait { get; set; } = "";
    public int Partition { get; set; }
    public string? Start { get; set; }      // a subscription of kind start: the spec of the instance an event creates
}

public sealed class EventEntity
{
    public string Key { get; set; } = "";
    public string Id { get; set; } = "";
    public string? Payload { get; set; }
    public long KeepUntil { get; set; }
}

public sealed class JournalEntity
{
    public int Partition { get; set; }
    public long Position { get; set; }
    public string Instance { get; set; } = "";
    public long Revision { get; set; }
    public string Entries { get; set; } = "";
}

public sealed class KernelDb(DbContextOptions<KernelDb> options) : DbContext(options)
{
    public DbSet<PartitionEntity> Partitions => Set<PartitionEntity>();
    public DbSet<NodeEntity> Nodes => Set<NodeEntity>();
    public DbSet<InstanceEntity> Instances => Set<InstanceEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<SubscriptionEntity> Subscriptions => Set<SubscriptionEntity>();
    public DbSet<EventEntity> Events => Set<EventEntity>();
    public DbSet<JournalEntity> Journal => Set<JournalEntity>();
    public DbSet<ProjectionEntity> Projections => Set<ProjectionEntity>();
    public DbSet<ViewEntity> Views => Set<ViewEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<PartitionEntity>(e =>
        {
            e.ToTable("Partitions");
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Epoch).IsConcurrencyToken();      // the fence, when it rides in the batch
            e.Property(p => p.Position).IsConcurrencyToken();
        });
        model.Entity<NodeEntity>(e => e.ToTable("Nodes"));
        model.Entity<InstanceEntity>(e =>
        {
            e.ToTable("Instances");
            e.Property(i => i.Revision).IsConcurrencyToken();   // every update is WHERE revision = r
        });
        model.Entity<MessageEntity>(e =>
        {
            e.ToTable("Messages");
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.Partition, m.Seq });
            e.Property(m => m.Done).IsConcurrencyToken();       // a consumed row must still be there and not done: the count check
            // seq: an identity column on PostgreSQL. SQLite has one writer, so the store assigns max + 1 under the write lock.
            if (Database.IsNpgsql()) e.Property(m => m.Seq).UseIdentityByDefaultColumn();
            else e.Property(m => m.Seq).ValueGeneratedNever();
        });
        model.Entity<SubscriptionEntity>(e =>
        {
            e.ToTable("Subscriptions");
            e.HasKey(s => new { s.Key, s.Instance, s.Wait });
            // Merge with round 3, as ENGINES.md E8: a terminate deletes every wait of its instance. Without an index that
            // statement scans the table, and on InnoDB under REPEATABLE READ locks every row and gap of it.
            e.HasIndex(s => s.Instance);
        });
        model.Entity<EventEntity>(e =>
        {
            e.ToTable("Events");
            e.HasKey(x => new { x.Key, x.Id });
        });
        model.Entity<JournalEntity>(e =>
        {
            e.ToTable("Journal");
            e.HasKey(j => new { j.Partition, j.Position });
            e.HasIndex(j => new { j.Partition, j.Instance });     // a delete removes its instance's rows: E8, as above
        });
        model.Entity<ProjectionEntity>(e =>
        {
            e.ToTable("Projections");
            e.HasKey(p => p.Partition);
            e.Property(p => p.Partition).ValueGeneratedNever();
        });
        model.Entity<ViewEntity>(e =>
        {
            e.ToTable("Views");
            e.HasKey(v => v.Instance);
        });
        Engine.Model(Database, model);      // SQL Server and MySQL: Engines.cs
    }
}

/// <summary>
/// The storage interface on Entity Framework Core, for PostgreSQL and SQLite, and with an <see cref="Engine"/> for SQL Server and MySQL. A commit is: the fence, checked; then
/// one SaveChanges with everything else; then COMMIT. With the fence in the batch it rides in the SaveChanges as
/// concurrency tokens on epoch and position, which saves a round trip and takes the lease time from this machine's
/// clock instead of the database's. <paramref name="fenceInBatch"/> chooses; null takes the engine's default
/// (<see cref="FenceInBatch"/>).
/// </summary>
public sealed class EfStore(Func<KernelDb> create, bool postgres, bool? fenceInBatch = null) : IKernelStore
{
    /// <summary>
    /// Whether the fence rides in the batch. The default (decided 12 and 13 November) is in the batch on every engine
    /// but SQL Server, whose default is the checked fence: there the fence in the batch deadlocks takes (ENGINES.md E9).
    /// </summary>
    public bool FenceInBatch => fenceInBatch ?? Engine?.FenceInBatchByDefault ?? true;

    /// <summary>MUTATION, for the stress test only: the fence statement loses its epoch and position conditions.</summary>
    public bool Unfenced { get; init; }

    /// <summary>MUTATION, to reproduce ENGINES.md E1 (MONGO.md M5): a definite error is reported as definite even when the fence in the batch did not match.</summary>
    public bool DefiniteBeatsFence { get; init; }

    /// <summary>How often E1 turned a key violation into "the fence matched nothing". For the stress test's report.</summary>
    public int KeyViolationsAsFence => _keyViolationsAsFence;
    private int _keyViolationsAsFence;

    /// <summary>Set for SQL Server and MySQL (with postgres: true, as the database assigns seq there too); null for PostgreSQL and SQLite.</summary>
    public Engine? Engine { get; init; }

    private sealed class PollRow
    {
        public long Now { get; set; }
        public string? Id { get; set; }
        public long? Seq { get; set; }
        public string? Target { get; set; }
        public string? Kind { get; set; }
        public string? Payload { get; set; }
        public string? Sender { get; set; }
        public string? Start { get; set; }
        public long? Due { get; set; }
        public long? Seen { get; set; }
        public int? Attempts { get; set; }
        public long? Trial { get; set; }
        public long? KeepUntil { get; set; }
        public string? ReplyTo { get; set; }
    }

    // Database time in milliseconds.
    private string Now => Engine is not null ? Engine.Now : postgres ? "(extract(epoch from clock_timestamp()) * 1000)::bigint" : "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
    private string False => postgres && Engine is null ? "false" : "0";
    private string Q(string sql) => Engine?.Sql(sql) ?? sql;

    /// <summary>
    /// Creates the tables and the partition rows. The number of partitions is fixed here; by default (the owner's of 12
    /// November) 64 on a server database and 1 on SQLite.
    /// </summary>
    public async Task CreateAsync(int? partitions = null)
    {
        var count = partitions ?? (postgres ? Defaults.ServerPartitions : Defaults.SqlitePartitions);
        await using var db = create();
        await db.Database.EnsureCreatedAsync();
        if (Engine is not null) await Engine.CreatedAsync(db, default);
        if (await db.Partitions.AnyAsync()) return;
        db.Partitions.AddRange(Enumerable.Range(0, count).Select(id => new PartitionEntity { Id = id }));
        db.Projections.AddRange(Enumerable.Range(0, count).Select(id => new ProjectionEntity { Partition = id }));
        await db.SaveChangesAsync();
    }

    public async ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        await using var transaction = await (Engine?.BeginTakeAsync(db, cancellationToken) ?? db.Database.BeginTransactionAsync(cancellationToken));
        Engine.TakeRow? taken;
        if (Engine is not null) taken = await Engine.TakeAsync(db, partition, node, (long)lease.TotalMilliseconds, cancellationToken);
        else
        {
            var rows = await db.Database.SqlQueryRaw<Engine.TakeRow>(
                $"""UPDATE "Partitions" SET "Died" = ("Owner" IS NOT NULL AND "Owner" <> {"{0}"}), "Owner" = {"{0}"}, "Epoch" = "Epoch" + 1, "LeaseUntil" = {Now} + {"{1}"} WHERE "Id" = {"{2}"} AND ("Owner" IS NULL OR "LeaseUntil" < {Now} OR "Owner" = {"{0}"}) RETURNING "Epoch", "Position", {Now} AS "Now", "Died" """,
                node, (long)lease.TotalMilliseconds, partition).ToListAsync(cancellationToken);
            taken = rows.Count == 1 ? rows[0] : null;
        }
        if (taken is null) return null;
        // Taken from another owner, so its lease had expired: one more attempt for what it had on trial.
        if (taken.Died)
            await db.Database.ExecuteSqlRawAsync(Q($"""UPDATE "Messages" SET "Attempts" = "Attempts" + 1 WHERE "Partition" = {"{0}"} AND "Trial" = {"{1}"} AND "Done" = {False}"""), [partition, taken.Epoch - 1], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new Lease(taken.Epoch, taken.Position, taken.Now);
    }

    public async ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        var now = Engine is not null ? await Engine.HeartbeatAsync(db, node, cancellationToken) : (await db.Database.SqlQueryRaw<long>(
            $"""INSERT INTO "Nodes" ("Id", "SeenAt") VALUES ({"{0}"}, {Now}) ON CONFLICT ("Id") DO UPDATE SET "SeenAt" = excluded."SeenAt" RETURNING "SeenAt" AS "Value" """,
            node).ToListAsync(cancellationToken))[0];
        var nodes = await db.Nodes.CountAsync(n => n.SeenAt >= now - (long)window.TotalMilliseconds, cancellationToken);
        var free = await db.Partitions.Where(p => p.Owner == null || p.LeaseUntil < now).Select(p => p.Id).ToListAsync(cancellationToken);
        return new Cluster(nodes, free, now);
    }

    public async ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        await db.Database.ExecuteSqlRawAsync(Q("""UPDATE "Partitions" SET "Owner" = NULL WHERE "Id" = {0} AND "Epoch" = {1}"""), [partition, epoch], cancellationToken);
    }

    public async ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        // FINDINGS 25.2 (M6): subscriptions are idempotent. The change tracker writes them as rows, so subscribing what is
        // there is a key violation and unsubscribing what is not is a count that does not match. A commit that fails in
        // one of those ways is sent once more with its subscriptions as statements that tolerate both: round trips of
        // their own, on that path only.
        var waits = Waits(batch);
        var careful = false;
        while (true)
        {
            try { return await CommitOnceAsync(batch, waits, careful, cancellationToken); }
            catch (DbUpdateConcurrencyException e) when (!careful && e.Entries.Any(x => x.Entity is SubscriptionEntity))
            {
                careful = true;     // the first count that did not match was an unsubscribe; the batch was rolled back whole
            }
            catch (DbUpdateConcurrencyException)
            {
                return new Committed(false, 0, []);     // the fence (in the batch) or a count did not match: either way, reset
            }
            catch (Exception e)
            {
                var failure = Classify(e);
                // ENGINES.md E1, MONGO.md M5. With the fence in the batch the rows are sent before the fence is checked, so an
                // owner that is behind collides with its successor's rows first and is told "duplicate key" (PK_Journal, or a
                // derived message id). A fence that did not match wins over every other error of the batch: a failure that
                // says the batch did not happen (definite, or a deadlock victim) is reported as such only while the fence
                // still holds, and is "not landed" otherwise. Epoch and position never return to old values, so a fence
                // that holds now held when the batch ran. An unknown outcome stays unknown: the batch may have landed and
                // moved the position itself.
                if (FenceInBatch && !DefiniteBeatsFence && failure.Failure is Failure.Definite or Failure.Transient)
                {
                    bool holds;
                    try { holds = await HoldsAsync(batch, cancellationToken); }
                    catch (Exception check) { throw new StoreException(Failure.Unknown, "the fence could not be read after a failed commit: " + check.Message, check); }
                    if (!holds)
                    {
                        Interlocked.Increment(ref _keyViolationsAsFence);
                        return new Committed(false, 0, []);
                    }
                }
                // A key violation may be a subscription that is there already; the batch was rolled back whole.
                if (!careful && failure.Failure is Failure.Definite && waits.ContainsValue(true)) { careful = true; continue; }
                throw failure;
            }
        }
    }

    /// <summary>
    /// What the batch leaves of each subscription it names, in the order the in-memory store applies them (a turn's
    /// subscribes, then its unsubscribes): true when it is to be there. So each is written once, however often it is named.
    /// </summary>
    private static Dictionary<(string Key, string Instance, string Wait), bool> Waits(CommitBatch batch)
    {
        var waits = new Dictionary<(string, string, string), bool>();
        foreach (var turn in batch.Turns)
        {
            foreach (var s in turn.Subscribed) waits[(s.Key, s.Instance, s.Wait)] = true;
            foreach (var s in turn.Unsubscribed) waits[(s.Key, s.Instance, s.Wait)] = false;
        }
        return waits;
    }

    private async Task<Committed> CommitOnceAsync(CommitBatch batch, Dictionary<(string Key, string Instance, string Wait), bool> waits, bool careful, CancellationToken cancellationToken)
    {
        await using var db = create();
        db.Database.AutoSavepointsEnabled = false;      // ENGINES.md E4: SAVEPOINT and RELEASE were two more round trips per commit on PostgreSQL
        await using var transaction = await (Engine?.BeginAsync(db, cancellationToken) ?? db.Database.BeginTransactionAsync(cancellationToken));
        var now = 0L;
        if (FenceInBatch)
        {
            // MUTATION (Unfenced): the tokens are what the row says now, not what this owner believes, so the check always passes.
            var (tokenEpoch, tokenPosition) = (batch.Epoch, batch.Position);
            if (Unfenced)
            {
                var current = await db.Partitions.AsNoTracking().SingleAsync(p => p.Id == batch.Partition, cancellationToken);
                (tokenEpoch, tokenPosition) = (current.Epoch, current.Position);
            }
            var fence = db.Attach(new PartitionEntity { Id = batch.Partition, Epoch = tokenEpoch, Position = tokenPosition }).Entity;
            fence.Position = batch.Position + batch.Positions;
            fence.LeaseUntil = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)batch.Lease.TotalMilliseconds;
            db.Entry(fence).Property(p => p.LeaseUntil).IsModified = true;     // also when nothing else changes: the fence must always be sent
        }
        else if (Engine is not null)
        {
            if (await Engine.FenceAsync(db, batch, Unfenced, cancellationToken) is not { } at) return new Committed(false, 0, []);
            now = at;
        }
        else
        {
            // Statement 1, and the check that it changed exactly one row before anything else is sent.
            var fence = await db.Database.SqlQueryRaw<long>(
                $"""UPDATE "Partitions" SET "Position" = {"{0}"}, "LeaseUntil" = {Now} + {"{1}"} WHERE "Id" = {"{2}"} AND ("Epoch" = {"{3}"} AND "Position" = {"{4}"} OR {(Unfenced ? "1 = 1" : "1 = 0")}) RETURNING {Now} AS "Value" """,
                batch.Position + batch.Positions, (long)batch.Lease.TotalMilliseconds, batch.Partition, batch.Epoch, batch.Position).ToListAsync(cancellationToken);
            if (fence.Count != 1) return new Committed(false, 0, []);       // disposing the transaction rolls it back
            now = fence[0];
        }

        var sends = batch.Turns.Sum(s => s.Sent.Count);
        var seq = postgres || sends == 0 ? 0 : await db.Database.SqlQueryRaw<long>("""SELECT COALESCE(MAX("Seq"), 0) AS "Value" FROM "Messages" """).SingleAsync(cancellationToken);

        var (position, sent) = (batch.Position, new List<MessageEntity>());
        foreach (var turn in batch.Turns)
        {
            // An ended instance: its row at the expected revision, in the batch, so a revision that does not match is a
            // count that does not match (not landed), as for an update. Its journal rows go after the batch, below.
            if (turn.Delete) db.Attach(new InstanceEntity { Id = turn.Instance, Revision = turn.Expected!.Value }).State = EntityState.Deleted;
            if (turn.Row is { } row)
            {
                var entity = new InstanceEntity
                {
                    Id = row.Id, Partition = batch.Partition, Model = row.Model, Definition = row.Definition, Status = row.Status,
                    State = row.State, Jobs = row.Jobs, Revision = row.Revision, Parent = row.Parent, Fault = row.Fault, History = row.History, Suspended = row.Suspended, Children = row.Children, StateVersion = row.StateVersion,
                };
                if (turn.Expected is not { } expected) db.Add(entity);
                else db.Update(entity).Property(i => i.Revision).OriginalValue = expected;
            }
            foreach (var id in turn.Consumed) db.Remove(new MessageEntity { Id = id });
            foreach (var id in turn.Done) db.Attach(new MessageEntity { Id = id }).Entity.Done = true;
            foreach (var id in turn.Held ?? [])     // its turn ran: no longer on trial
                db.Attach(new MessageEntity { Id = id, Trial = 0 }).Property(m => m.Trial).IsModified = true;
            foreach (var message in turn.Sent)
            {
                sent.Add(ToEntity(message, batch.HandOver && message.Target.Partition == batch.Partition ? batch.Epoch : 0, postgres ? 0 : ++seq));
                db.Add(sent[^1]);
            }
            if (turn.Journal is not null)
                db.Add(new JournalEntity { Partition = batch.Partition, Position = ++position, Instance = turn.Instance, Revision = turn.Delete ? -1 : turn.Row?.Revision ?? 0, Entries = turn.Journal });
        }
        if (!careful)
            foreach (var ((key, instance, wait), there) in waits)
                if (there) db.Add(new SubscriptionEntity { Key = key, Instance = instance, Wait = wait, Partition = batch.Partition });
                else db.Remove(new SubscriptionEntity { Key = key, Instance = instance, Wait = wait });
        foreach (var id in batch.Seen)
            db.Attach(new MessageEntity { Id = id, Seen = batch.Epoch }).Property(m => m.Seen).IsModified = true;
        foreach (var id in batch.Trial)
        {
            var tried = db.Attach(new MessageEntity { Id = id, Seen = batch.Epoch, Trial = batch.Epoch });
            (tried.Property(m => m.Seen).IsModified, tried.Property(m => m.Trial).IsModified) = (true, true);
        }

        await db.SaveChangesAsync(cancellationToken);       // one batch; EF checks every affected-row count

        // The careful path: each wait as a statement of its own, after the batch, for the reason the deletes below are.
        if (careful)
            foreach (var ((key, instance, wait), there) in waits)
                await db.Database.ExecuteSqlRawAsync(there ? Engine?.SubscribeIfAbsent ?? SubscribeIfAbsent : Q(Unsubscribing), [key, instance, wait, batch.Partition], cancellationToken);

        // Merge with round 3: the set-based deletes of a terminate and a delete have no keys to track, so they are
        // statements of their own, one round trip each and only in a commit that has them. They come after the batch,
        // so that with the fence in the batch they run only once the fence has held (E1: a stale owner is told "not
        // landed" before it touches anything else), and with either fence they lock after the partition row (E9).
        foreach (var turn in batch.Turns)
        {
            if (turn.Unsubscribe)       // ended by force: its waits go, whatever they are
                await db.Database.ExecuteSqlRawAsync(Q("""DELETE FROM "Subscriptions" WHERE "Instance" = {0}"""), [turn.Instance], cancellationToken);
            if (turn.Delete)
                await db.Database.ExecuteSqlRawAsync(Q("""DELETE FROM "Journal" WHERE "Partition" = {0} AND "Instance" = {1} AND "Revision" > 0"""), [batch.Partition, turn.Instance], cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new Committed(true, now, sent.Select(m => m.Seq).ToList());
    }

    // The careful path's statements on PostgreSQL and SQLite ({0} key, {1} instance, {2} wait, {3} partition). Only the
    // partition's owner writes its instances' waits, under the fence, so nothing else writes these rows meanwhile.
    private const string SubscribeIfAbsent = """INSERT INTO "Subscriptions" ("Key", "Instance", "Wait", "Partition") VALUES ({0}, {1}, {2}, {3}) ON CONFLICT DO NOTHING""";
    private const string Unsubscribing = """DELETE FROM "Subscriptions" WHERE "Key" = {0} AND "Instance" = {1} AND "Wait" = {2} AND "Partition" = {3}""";

    private async Task<bool> HoldsAsync(CommitBatch batch, CancellationToken cancellationToken)
    {
        await using var db = create();
        return await db.Partitions.AsNoTracking().AnyAsync(p => p.Id == batch.Partition && p.Epoch == batch.Epoch && p.Position == batch.Position, cancellationToken);
    }

    public async ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        // A plain read. The left join returns database time even when no message does.
        var rows = await db.Database.SqlQueryRaw<PollRow>(Engine?.Poll ??
            $"""
             SELECT t.now AS "Now", m."Id", m."Seq", m."Target", m."Kind", m."Payload", m."Sender", m."Start", m."Due", m."Seen", m."Attempts", m."Trial", m."KeepUntil", m."ReplyTo"
             FROM (SELECT {Now} AS now) t
             LEFT JOIN "Messages" m ON m."Partition" = {"{0}"} AND m."Seen" < {"{1}"} AND m."Done" = {False} AND m."Due" <= t.now + {"{2}"}
             ORDER BY m."Seq" LIMIT {"{3}"}
             """, partition, epoch, (long)horizon.TotalMilliseconds, limit).ToListAsync(cancellationToken);
        var messages = rows.Where(r => r.Id is not null).Select(r => new Stored(
            new Message(r.Id!, new InstanceRef(r.Target!), r.Kind!, r.Payload, r.Sender, r.Start is null ? null : Json.Read<StartSpec>(r.Start), r.Due == 0 ? null : r.Due, r.KeepUntil, r.ReplyTo),
            r.Seq!.Value, r.Seen!.Value, r.Attempts!.Value, r.Trial!.Value)).ToList();
        return new Polled(messages, rows[0].Now);
    }

    public async ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        var m = ToEntity(message, 0, 0);
        if (Engine is not null) return await Engine.AddAsync(db, m, cancellationToken);
        var seq = postgres ? ("", "") : (""", "Seq" """, """, (SELECT COALESCE(MAX("Seq"), 0) + 1 FROM "Messages")""");
        return await db.Database.ExecuteSqlRawAsync(
            $$"""
              INSERT INTO "Messages" ("Id", "Partition", "Target", "Kind", "Payload", "Sender", "Start", "Due", "Seen", "Attempts", "Trial", "KeepUntil", "Done", "ReplyTo"{{seq.Item1}})
              VALUES ({0}, {1}, {2}, {3}, CAST({4} AS TEXT), CAST({5} AS TEXT), CAST({6} AS TEXT), {7}, 0, 0, 0, CAST({8} AS BIGINT), {{False}}, CAST({9} AS TEXT){{seq.Item2}})
              ON CONFLICT ("Id") DO NOTHING
              """,
            [m.Id, m.Partition, m.Target, m.Kind, m.Payload!, m.Sender!, m.Start!, m.Due, m.KeepUntil!, m.ReplyTo!], cancellationToken) == 1;
    }

    public async ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        var rows = await db.Instances.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);
        return rows.Select(i => new InstanceRow(i.Id, i.Model, i.Definition, i.Status, i.State, i.Revision, i.Parent, i.Fault, i.Jobs, i.History, i.Suspended, i.Children, i.StateVersion)).ToList();
    }

    public async ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        if (Engine is not null) { await Engine.PublishAsync(db, @event, cancellationToken); return; }
        await db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "Events" ("Key", "Id", "Payload", "KeepUntil") VALUES ({0}, {1}, CAST({2} AS TEXT), {3}) ON CONFLICT DO NOTHING""",
            [@event.Key, @event.Id, @event.Payload!, @event.KeepUntil], cancellationToken);
    }

    public async ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        var rows = await db.Subscriptions.AsNoTracking().Where(s => s.Key == key).ToListAsync(cancellationToken);
        return rows.Select(s => new SubscriptionRow(s.Key, s.Instance, s.Wait, s.Start is null ? null : Json.Read<StartSpec>(s.Start))).ToList();
    }

    public async ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        if (Engine is not null) { await Engine.SubscribeAsync(db, subscription, cancellationToken); return; }
        await db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "Subscriptions" ("Key", "Instance", "Wait", "Partition", "Start") VALUES ({0}, {1}, {2}, 0, CAST({3} AS TEXT)) ON CONFLICT DO NOTHING""",
            [subscription.Key, subscription.Instance, subscription.Wait, Json.Write(subscription.Start)], cancellationToken);
    }

    public async ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        var rows = await db.Events.FromSqlRaw(Q($"""SELECT * FROM "Events" WHERE "KeepUntil" >= {Now}""")).AsNoTracking().Where(e => keys.Contains(e.Key)).ToListAsync(cancellationToken);
        return rows.Select(e => new EventRow(e.Key, e.Id, e.Payload, e.KeepUntil)).ToList();
    }

    public async ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        var rows = await db.Journal.AsNoTracking().Where(j => j.Partition == partition && j.Position > after).OrderBy(j => j.Position).Take(take).ToListAsync(cancellationToken);
        return rows.Select(j => new JournalRow(j.Partition, j.Position, j.Instance, j.Revision, j.Entries)).ToList();
    }

    private sealed class ProjectionRow
    {
        public long Position { get; set; }
        public long Written { get; set; }
    }

    public async ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        var rows = await db.Database.SqlQueryRaw<ProjectionRow>(
            Q("""SELECT (SELECT "Position" FROM "Projections" WHERE "Partition" = {0}) AS "Position", (SELECT "Position" FROM "Partitions" WHERE "Id" = {0}) AS "Written" """),
            partition).ToListAsync(cancellationToken);
        return new Projection(rows[0].Position, rows[0].Written);
    }

    public async ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        await using var transaction = await (Engine?.BeginAsync(db, cancellationToken) ?? db.Database.BeginTransactionAsync(cancellationToken));
        var moved = await db.Database.ExecuteSqlRawAsync(Q("""UPDATE "Projections" SET "Position" = {0} WHERE "Partition" = {1} AND "Position" = {2}"""), [to, partition, from], cancellationToken);
        if (moved != 1) return false;
        foreach (var view in rows.Where(r => r.Revision > 0).GroupBy(r => r.Instance))
        {
            if (Engine is not null) { await Engine.ViewAsync(db, view.Key, partition, view.Max(r => r.Revision), view.Count(), cancellationToken); continue; }
            await db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "Views" ("Instance", "Partition", "Revision", "Commits") VALUES ({0}, {1}, {2}, {3}) ON CONFLICT ("Instance") DO UPDATE SET "Revision" = excluded."Revision", "Commits" = "Views"."Commits" + excluded."Commits" """,
                [view.Key, partition, view.Max(r => r.Revision), view.Count()], cancellationToken);
        }
        foreach (var deleted in rows.Where(r => r.Revision < 0))
            await db.Database.ExecuteSqlRawAsync(Q("""DELETE FROM "Views" WHERE "Instance" = {0}"""), [deleted.Instance], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        var rows = await db.Views.AsNoTracking().Where(v => instances.Contains(v.Instance)).ToListAsync(cancellationToken);
        return rows.Select(v => new ViewRow(v.Instance, v.Revision, v.Commits)).ToList();
    }

    public async ValueTask<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        await using var db = create();
        return await db.Database.ExecuteSqlRawAsync(Q($"""DELETE FROM "Messages" WHERE "Done" <> {False} AND "KeepUntil" < {Now}; DELETE FROM "Events" WHERE "KeepUntil" < {Now}"""), cancellationToken);
    }

    private static MessageEntity ToEntity(Message m, long seen, long seq) => new()
    {
        Id = m.Id, Seq = seq, Partition = m.Target.Partition, Target = m.Target.Id, Kind = m.Kind, Payload = m.Payload, Sender = m.Sender,
        Start = m.Start is null ? null : Json.Write(m.Start), Due = m.Due ?? 0, Seen = seen, KeepUntil = m.KeepUntil, ReplyTo = m.ReplyTo,
    };

    /// <summary>What a failure means: it did not happen and may be retried, it did not happen and never will, or nobody knows.</summary>
    private StoreException Classify(Exception e)
    {
        for (var inner = e; inner is not null; inner = inner.InnerException)
        {
            if (Engine?.Classify(inner) is { } failure) return new StoreException(failure, inner.Message, e);
            if (inner is PostgresException pg)
                return new StoreException(pg.SqlState is "40001" or "40P01" ? Failure.Transient : pg.SqlState.StartsWith("22") || pg.SqlState.StartsWith("23") ? Failure.Definite : Failure.Unknown, pg.MessageText, e);
            if (inner is SqliteException lite)
                return new StoreException(lite.SqliteErrorCode is 5 or 6 ? Failure.Transient : lite.SqliteErrorCode == 19 ? Failure.Definite : Failure.Unknown, lite.Message, e);     // BUSY is retry, not reset
        }
        return new StoreException(Failure.Unknown, e.Message, e);
    }
}
