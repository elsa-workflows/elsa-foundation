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
    public string? Runs { get; set; }
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
    public string? ReplyTo { get; set; }    // a caller waiting on another node for what the step that consumes it emits
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
    }
}

/// <summary>
/// The storage interface on Entity Framework Core, for PostgreSQL and SQLite. A commit is: the fence, checked; then
/// one SaveChanges with everything else; then COMMIT. With <paramref name="fenceInBatch"/> the fence rides in the
/// SaveChanges as concurrency tokens on epoch and position, which saves a round trip and takes the lease time from
/// this machine's clock instead of the database's.
/// </summary>
public sealed class EfStore(Func<KernelDb> create, bool postgres, bool fenceInBatch = false) : IKernelStore
{
    /// <summary>MUTATION, for the stress test only: the fence statement loses its epoch and position conditions.</summary>
    public bool Unfenced { get; init; }

    private sealed class TakeRow
    {
        public long Epoch { get; set; }
        public long Position { get; set; }
        public long Now { get; set; }
        public bool Died { get; set; }
    }

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
    private string Now => postgres ? "(extract(epoch from clock_timestamp()) * 1000)::bigint" : "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
    private string False => postgres ? "false" : "0";

    public async Task CreateAsync(int partitions)
    {
        await using var db = create();
        await db.Database.EnsureCreatedAsync();
        if (await db.Partitions.AnyAsync()) return;
        db.Partitions.AddRange(Enumerable.Range(0, partitions).Select(id => new PartitionEntity { Id = id }));
        db.Projections.AddRange(Enumerable.Range(0, partitions).Select(id => new ProjectionEntity { Partition = id }));
        await db.SaveChangesAsync();
    }

    public async ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await db.Database.SqlQueryRaw<TakeRow>(
            $"""UPDATE "Partitions" SET "Died" = ("Owner" IS NOT NULL AND "Owner" <> {"{0}"}), "Owner" = {"{0}"}, "Epoch" = "Epoch" + 1, "LeaseUntil" = {Now} + {"{1}"} WHERE "Id" = {"{2}"} AND ("Owner" IS NULL OR "LeaseUntil" < {Now} OR "Owner" = {"{0}"}) RETURNING "Epoch", "Position", {Now} AS "Now", "Died" """,
            node, (long)lease.TotalMilliseconds, partition).ToListAsync(cancellationToken);
        if (rows.Count != 1) return null;
        // Taken from another owner, so its lease had expired: one more attempt for what it had on trial.
        if (rows[0].Died)
            await db.Database.ExecuteSqlRawAsync($"""UPDATE "Messages" SET "Attempts" = "Attempts" + 1 WHERE "Partition" = {"{0}"} AND "Trial" = {"{1}"} AND "Done" = {False}""", [partition, rows[0].Epoch - 1], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new Lease(rows[0].Epoch, rows[0].Position, rows[0].Now);
    }

    public async ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        var now = await db.Database.SqlQueryRaw<long>(
            $"""INSERT INTO "Nodes" ("Id", "SeenAt") VALUES ({"{0}"}, {Now}) ON CONFLICT ("Id") DO UPDATE SET "SeenAt" = excluded."SeenAt" RETURNING "SeenAt" AS "Value" """,
            node).ToListAsync(cancellationToken);
        var nodes = await db.Nodes.CountAsync(n => n.SeenAt >= now[0] - (long)window.TotalMilliseconds, cancellationToken);
        var free = await db.Partitions.Where(p => p.Owner == null || p.LeaseUntil < now[0]).Select(p => p.Id).ToListAsync(cancellationToken);
        return new Cluster(nodes, free, now[0]);
    }

    public async ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        await db.Database.ExecuteSqlRawAsync("""UPDATE "Partitions" SET "Owner" = NULL WHERE "Id" = {0} AND "Epoch" = {1}""", [partition, epoch], cancellationToken);
    }

    public async ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = create();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = 0L;
            if (fenceInBatch)
            {
                var fence = db.Attach(new PartitionEntity { Id = batch.Partition, Epoch = batch.Epoch, Position = batch.Position }).Entity;
                fence.Position = batch.Position + batch.Positions;
                fence.LeaseUntil = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)batch.Lease.TotalMilliseconds;
                db.Entry(fence).Property(p => p.LeaseUntil).IsModified = true;     // also when nothing else changes: the fence must always be sent
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

            var sends = batch.Steps.Sum(s => s.Sent.Count);
            var seq = postgres || sends == 0 ? 0 : await db.Database.SqlQueryRaw<long>("""SELECT COALESCE(MAX("Seq"), 0) AS "Value" FROM "Messages" """).SingleAsync(cancellationToken);

            var (position, sent) = (batch.Position, new List<MessageEntity>());
            foreach (var step in batch.Steps)
            {
                if (step.Row is { } row)
                {
                    var entity = new InstanceEntity
                    {
                        Id = row.Id, Partition = batch.Partition, Model = row.Model, Definition = row.Definition, Status = row.Status,
                        State = row.State, Runs = row.Runs, Revision = row.Revision, Parent = row.Parent, Fault = row.Fault, History = row.History, Suspended = row.Suspended, Children = row.Children, StateVersion = row.StateVersion,
                    };
                    if (step.Expected is not { } expected) db.Add(entity);
                    else db.Update(entity).Property(i => i.Revision).OriginalValue = expected;
                }
                foreach (var id in step.Consumed) db.Remove(new MessageEntity { Id = id });
                foreach (var id in step.Done) db.Attach(new MessageEntity { Id = id }).Entity.Done = true;
                foreach (var id in step.Held ?? [])     // its step ran: no longer on trial
                    db.Attach(new MessageEntity { Id = id, Trial = 0 }).Property(m => m.Trial).IsModified = true;
                foreach (var message in step.Sent)
                {
                    sent.Add(ToEntity(message, batch.HandOver && message.Target.Partition == batch.Partition ? batch.Epoch : 0, postgres ? 0 : ++seq));
                    db.Add(sent[^1]);
                }
                foreach (var s in step.Subscribed) db.Add(new SubscriptionEntity { Key = s.Key, Instance = s.Instance, Wait = s.Wait, Partition = batch.Partition });
                foreach (var s in step.Unsubscribed) db.Remove(new SubscriptionEntity { Key = s.Key, Instance = s.Instance, Wait = s.Wait });
                if (step.Unsubscribe)       // ended by force: its waits go, whatever they are
                    await db.Database.ExecuteSqlRawAsync("""DELETE FROM "Subscriptions" WHERE "Instance" = {0}""", [step.Instance], cancellationToken);
                if (step.Journal is not null)
                    db.Add(new JournalEntity { Partition = batch.Partition, Position = ++position, Instance = step.Instance, Revision = step.Row?.Revision ?? 0, Entries = step.Journal });
            }
            foreach (var id in batch.Seen)
                db.Attach(new MessageEntity { Id = id, Seen = batch.Epoch }).Property(m => m.Seen).IsModified = true;
            foreach (var id in batch.Trial)
            {
                var tried = db.Attach(new MessageEntity { Id = id, Seen = batch.Epoch, Trial = batch.Epoch });
                (tried.Property(m => m.Seen).IsModified, tried.Property(m => m.Trial).IsModified) = (true, true);
            }

            await db.SaveChangesAsync(cancellationToken);       // one batch; EF checks every affected-row count
            await transaction.CommitAsync(cancellationToken);
            return new Committed(true, now, sent.Select(m => m.Seq).ToList());
        }
        catch (DbUpdateConcurrencyException)
        {
            return new Committed(false, 0, []);     // the fence (in the batch) or a count did not match: either way, reset
        }
        catch (Exception e)
        {
            throw Classify(e);
        }
    }

    public async ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        // A plain read. The left join returns database time even when no message does.
        var rows = await db.Database.SqlQueryRaw<PollRow>(
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
        return rows.Select(i => new InstanceRow(i.Id, i.Model, i.Definition, i.Status, i.State, i.Revision, i.Parent, i.Fault, i.Runs, i.History, i.Suspended, i.Children, i.StateVersion)).ToList();
    }

    public async ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default)
    {
        await using var db = create();
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
        await db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "Subscriptions" ("Key", "Instance", "Wait", "Partition", "Start") VALUES ({0}, {1}, {2}, 0, CAST({3} AS TEXT)) ON CONFLICT DO NOTHING""",
            [subscription.Key, subscription.Instance, subscription.Wait, Json.Write(subscription.Start)], cancellationToken);
    }

    public async ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        var rows = await db.Events.FromSqlRaw($"""SELECT * FROM "Events" WHERE "KeepUntil" >= {Now}""").AsNoTracking().Where(e => keys.Contains(e.Key)).ToListAsync(cancellationToken);
        return rows.Select(e => new EventRow(e.Key, e.Id, e.Payload, e.KeepUntil)).ToList();
    }

    public async ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        var rows = await db.Journal.AsNoTracking().Where(j => j.Partition == partition && j.Position > after).OrderBy(j => j.Position).Take(take).ToListAsync(cancellationToken);
        return rows.Select(j => new JournalRow(j.Partition, j.Position, j.Instance, j.Revision, j.Entries)).ToList();
    }

    public async ValueTask<long> ProjectedAsync(int partition, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        return await db.Projections.Where(p => p.Partition == partition).Select(p => p.Position).SingleAsync(cancellationToken);
    }

    public async ValueTask<bool> ProjectAsync(int partition, long from, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var moved = await db.Database.ExecuteSqlRawAsync("""UPDATE "Projections" SET "Position" = {0} WHERE "Partition" = {1} AND "Position" = {2}""", [rows[^1].Position, partition, from], cancellationToken);
        if (moved != 1) return false;
        foreach (var view in rows.Where(r => r.Revision > 0).GroupBy(r => r.Instance))
            await db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "Views" ("Instance", "Partition", "Revision", "Commits") VALUES ({0}, {1}, {2}, {3}) ON CONFLICT ("Instance") DO UPDATE SET "Revision" = excluded."Revision", "Commits" = "Views"."Commits" + excluded."Commits" """,
                [view.Key, partition, view.Max(r => r.Revision), view.Count()], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async ValueTask<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        await using var db = create();
        return await db.Database.ExecuteSqlRawAsync($"""DELETE FROM "Messages" WHERE "Done" <> {False} AND "KeepUntil" < {Now}; DELETE FROM "Events" WHERE "KeepUntil" < {Now}""", cancellationToken);
    }

    private static MessageEntity ToEntity(Message m, long seen, long seq) => new()
    {
        Id = m.Id, Seq = seq, Partition = m.Target.Partition, Target = m.Target.Id, Kind = m.Kind, Payload = m.Payload, Sender = m.Sender,
        Start = m.Start is null ? null : Json.Write(m.Start), Due = m.Due ?? 0, Seen = seen, KeepUntil = m.KeepUntil, ReplyTo = m.ReplyTo,
    };

    /// <summary>What a failure means: it did not happen and may be retried, it did not happen and never will, or nobody knows.</summary>
    private static StoreException Classify(Exception e)
    {
        for (var inner = e; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException pg)
                return new StoreException(pg.SqlState is "40001" or "40P01" ? Failure.Transient : pg.SqlState.StartsWith("22") || pg.SqlState.StartsWith("23") ? Failure.Definite : Failure.Unknown, pg.MessageText, e);
            if (inner is SqliteException lite)
                return new StoreException(lite.SqliteErrorCode is 5 or 6 ? Failure.Transient : lite.SqliteErrorCode == 19 ? Failure.Definite : Failure.Unknown, lite.Message, e);     // BUSY is retry, not reset
        }
        return new StoreException(Failure.Unknown, e.Message, e);
    }
}
