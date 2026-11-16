using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kernel.VpExperiment;

/// <summary>Which row carries the fence.</summary>
public enum Fence
{
    /// <summary>(i) One fence row per virtual partition: a flush that touches k virtual partitions fences k rows.</summary>
    PerVirtualPartition,
    /// <summary>(ii) One fence row per owned range, split and merged on rebalance; a position row per virtual partition.</summary>
    PerRange,
}

/// <summary>(ii) An owned range: the fence row. <c>Epoch</c> comes from one counter for all ranges, so (Lo, Epoch) is unique forever.</summary>
public sealed class RangeEntity
{
    public int Lo { get; set; }
    public int Hi { get; set; }
    public string? Owner { get; set; }
    public long Epoch { get; set; }
    public long LeaseUntil { get; set; }
}

/// <summary>(ii) A virtual partition's journal position, its own row apart from the fence.</summary>
public sealed class VpPositionEntity
{
    public int Vp { get; set; }
    public long Position { get; set; }
}

/// <summary>(ii) The one epoch counter.</summary>
public sealed class EpochCounterEntity
{
    public int Id { get; set; }
    public long Next { get; set; }
}

/// <summary>
/// The experiment's tables, mapped as <c>EfStore</c>'s <c>KernelDb</c> maps them (the same entity classes, keys and
/// concurrency tokens), plus the variant's own rows. Only what a flush, a take, a beat and a rebalance touch:
/// instances, messages, the journal and the fence rows.
/// </summary>
public sealed class VpDb(DbContextOptions<VpDb> options, Fence fence, bool postgres) : DbContext(options)
{
    public Fence Fence => fence;
    public DbSet<PartitionEntity> Partitions => Set<PartitionEntity>();
    public DbSet<RangeEntity> Ranges => Set<RangeEntity>();
    public DbSet<VpPositionEntity> VpPositions => Set<VpPositionEntity>();
    public DbSet<EpochCounterEntity> Epochs => Set<EpochCounterEntity>();
    public DbSet<InstanceEntity> Instances => Set<InstanceEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<JournalEntity> Journal => Set<JournalEntity>();

    // EF caches one model per context type; this context has one per fence variant and provider.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, VariantModelCacheKeyFactory>();

    private sealed class VariantModelCacheKeyFactory : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            context is VpDb db ? (typeof(VpDb), db.Fence, db.Database.ProviderName, designTime) : (object)(context.GetType(), designTime);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        if (fence == Fence.PerVirtualPartition)
            model.Entity<PartitionEntity>(e =>
            {
                e.ToTable("Partitions");
                e.Property(p => p.Id).ValueGeneratedNever();
                e.Property(p => p.Epoch).IsConcurrencyToken();
                e.Property(p => p.Position).IsConcurrencyToken();
            });
        else
        {
            model.Ignore<PartitionEntity>();
            model.Entity<RangeEntity>(e =>
            {
                e.ToTable("Ranges");
                e.HasKey(r => r.Lo);
                e.Property(r => r.Lo).ValueGeneratedNever();
                e.Property(r => r.Epoch).IsConcurrencyToken();
                e.Property(r => r.Hi).IsConcurrencyToken();      // a split moved the bound: the owner's picture of its range is stale
                e.HasIndex(r => r.Owner);
            });
            model.Entity<VpPositionEntity>(e =>
            {
                e.ToTable("VpPositions");
                e.HasKey(v => v.Vp);
                e.Property(v => v.Vp).ValueGeneratedNever();
                e.Property(v => v.Position).IsConcurrencyToken();
            });
            model.Entity<EpochCounterEntity>(e => { e.ToTable("Epochs"); e.Property(c => c.Id).ValueGeneratedNever(); });
        }
        if (fence == Fence.PerVirtualPartition) { model.Ignore<RangeEntity>(); model.Ignore<VpPositionEntity>(); model.Ignore<EpochCounterEntity>(); }
        model.Entity<InstanceEntity>(e =>
        {
            e.ToTable("Instances");
            e.Property(i => i.Revision).IsConcurrencyToken();
            var retired = e.Property(i => i.Retired).HasDefaultValue(false).ValueGeneratedNever();
            retired.Metadata.SetBeforeSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        });
        model.Entity<MessageEntity>(e =>
        {
            e.ToTable("Messages");
            e.HasKey(m => new { m.Id, m.InboxScope });
            e.Ignore(m => m.IssuedWire);
            e.Property(m => m.InboxScope).ValueGeneratedNever();
            e.HasIndex(m => new { m.Partition, m.Seq });
            e.Property(m => m.EventMatch).IsConcurrencyToken();
            e.Property(m => m.Done).IsConcurrencyToken();
            if (postgres) e.Property(m => m.Seq).UseIdentityByDefaultColumn(); else e.Property(m => m.Seq).ValueGeneratedNever();
        });
        model.Entity<JournalEntity>(e =>
        {
            e.ToTable("Journal");
            e.HasKey(j => new { j.Partition, j.Position });
            e.HasIndex(j => new { j.Partition, j.Instance });
        });
    }
}

/// <summary>One instance's part of a flush: its row (new or at <c>Revision - 1</c>), one consumed message, one journal row.</summary>
public sealed record Turn(int Vp, string Instance, long Revision, string? Consumed, string Journal, string State);

/// <summary>What a node holds of a range: the epochs it must present and the positions it believes.</summary>
public sealed class Ownership(string node, VpRange range)
{
    public string Node => node;
    public VpRange Range { get; set; } = range;
    /// <summary>(ii) The range row's epoch.</summary>
    public long Epoch { get; set; }
    /// <summary>(i) Each virtual partition's epoch; (ii) unused.</summary>
    public Dictionary<int, long> VpEpoch { get; } = [];
    public Dictionary<int, long> Position { get; } = [];
    public bool Owns(int vp) => Range.Contains(vp) && Position.ContainsKey(vp);
}

/// <summary>Counts commands and transactions, as the cost runs do (FINDINGS section 5).</summary>
public sealed class Counting : DbCommandInterceptor
{
    public sealed class Tx : DbTransactionInterceptor
    {
        public int Begun, Ended;
        public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result) { Begun++; return result; }
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default) { Begun++; return new(result); }
        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Ended++;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) { Ended++; return Task.CompletedTask; }
        public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Ended++;
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) { Ended++; return Task.CompletedTask; }
    }
    public readonly Tx Transactions = new();
    public int Commands;
    public void Reset() => (Commands, Transactions.Begun, Transactions.Ended) = (0, 0, 0);
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { Commands++; return result; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Commands++; return new(result); }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) { Commands++; return result; }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Commands++; return new(result); }
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) { Commands++; return result; }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) { Commands++; return new(result); }
}

/// <summary>
/// The two variants behind one surface: create, take a range, commit a flush, beat, release part of a range (a
/// cooperative rebalance: (i) frees the rows, (ii) splits the row), merge two adjacent ranges ((ii) only; (i) has
/// nothing to merge). The SQL is the shape <c>EfStore</c> uses today: the fence as concurrency tokens in the one
/// SaveChanges by default, or checked as its own statement first; takes, releases, splits and merges as conditional
/// statements with RETURNING, as the take is today. Nothing here is hand-tuned commit SQL: that is round 5 B.
/// </summary>
public sealed class RangeStore(Func<VpDb> create, Fence fence, bool postgres, bool fenceInBatch)
{
    public Fence Fence => fence;
    public bool FenceInBatch => fenceInBatch;
    private string Now => postgres ? "(extract(epoch from clock_timestamp()) * 1000)::bigint" : "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
    private long _seq;      // SQLite: the message order key, assigned by the one writer

    public async Task CreateAsync(int count)
    {
        await using var db = create();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        if (fence == Fence.PerVirtualPartition) db.Partitions.AddRange(Enumerable.Range(0, count).Select(vp => new PartitionEntity { Id = vp }));
        else
        {
            db.Ranges.Add(new RangeEntity { Lo = 0, Hi = count });
            db.VpPositions.AddRange(Enumerable.Range(0, count).Select(vp => new VpPositionEntity { Vp = vp }));
            db.Epochs.Add(new EpochCounterEntity { Id = 0, Next = 0 });
        }
        await db.SaveChangesAsync();
    }

    private sealed class TakenVp { public int Id { get; set; } public long Epoch { get; set; } public long Position { get; set; } }
    private sealed class TakenRange { public long Epoch { get; set; } public int Hi { get; set; } }
    private sealed class VpPos { public int Vp { get; set; } public long Position { get; set; } }

    /// <summary>
    /// Take a range if it is free, expired or mine. (i) One statement over the rows of the range, returning each
    /// virtual partition's epoch and position; a range only partly free is not taken (rolled back). (ii) The epoch
    /// counter, the range row, then the positions of its virtual partitions: three statements.
    /// </summary>
    public async Task<Ownership?> TakeAsync(VpRange range, string node, long leaseMs)
    {
        await using var db = create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var ownership = new Ownership(node, range);
        if (fence == Fence.PerVirtualPartition)
        {
            var rows = await db.Database.SqlQueryRaw<TakenVp>(
                $$"""UPDATE "Partitions" SET "Owner" = {0}, "Epoch" = "Epoch" + 1, "LeaseUntil" = {{Now}} + {1} WHERE "Id" >= {2} AND "Id" < {3} AND ("Owner" IS NULL OR "LeaseUntil" < {{Now}} OR "Owner" = {0}) RETURNING "Id", "Epoch", "Position" """,
                node, leaseMs, range.Lo, range.Hi).ToListAsync();
            if (rows.Count != range.Count) { await tx.RollbackAsync(); return null; }
            foreach (var row in rows) { ownership.VpEpoch[row.Id] = row.Epoch; ownership.Position[row.Id] = row.Position; }
        }
        else
        {
            var epoch = await NextEpochAsync(db);
            var rows = await db.Database.SqlQueryRaw<TakenRange>(
                $$"""UPDATE "Ranges" SET "Owner" = {0}, "Epoch" = {1}, "LeaseUntil" = {{Now}} + {2} WHERE "Lo" = {3} AND "Hi" = {4} AND ("Owner" IS NULL OR "LeaseUntil" < {{Now}} OR "Owner" = {0}) RETURNING "Epoch", "Hi" """,
                node, epoch, leaseMs, range.Lo, range.Hi).ToListAsync();
            if (rows.Count != 1) { await tx.RollbackAsync(); return null; }
            ownership.Epoch = rows[0].Epoch;
            foreach (var row in await db.Database.SqlQueryRaw<VpPos>("""SELECT "Vp", "Position" FROM "VpPositions" WHERE "Vp" >= {0} AND "Vp" < {1}""", range.Lo, range.Hi).ToListAsync())
                ownership.Position[row.Vp] = row.Position;
        }
        await tx.CommitAsync();
        return ownership;
    }

    private sealed class FreeRow { public int Lo { get; set; } public int Hi { get; set; } public long Epoch { get; set; } }

    /// <summary>
    /// (ii) Cut <paramref name="want"/> out of the free row that contains it, so that it can be taken: a split at each
    /// bound that is not already one, each a new epoch for the kept row and a new row for the rest. (i) Nothing: the
    /// rows are per virtual partition already. Returns the statements it took.
    /// </summary>
    public async Task<int> CarveAsync(VpRange want)
    {
        if (fence == Fence.PerVirtualPartition) return 0;
        await using var db = create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var statements = 1;
        var row = (await db.Database.SqlQueryRaw<FreeRow>("""SELECT "Lo", "Hi", "Epoch" FROM "Ranges" WHERE "Lo" <= {0} AND "Hi" >= {1} AND "Owner" IS NULL""", want.Lo, want.Hi).ToListAsync()).SingleOrDefault()
            ?? throw new InvalidOperationException($"no free range contains {want}");
        var (lo, hi, epoch) = (row.Lo, row.Hi, row.Epoch);
        foreach (var at in new[] { want.Lo, want.Hi })
        {
            if (at <= lo || at >= hi) continue;
            var (kept, rest) = (await NextEpochAsync(db), await NextEpochAsync(db));
            if (await db.Database.ExecuteSqlRawAsync("""UPDATE "Ranges" SET "Hi" = {0}, "Epoch" = {1} WHERE "Lo" = {2} AND "Hi" = {3} AND "Epoch" = {4}""", at, kept, lo, hi, epoch) != 1) throw new InvalidOperationException("the free row changed under the carve");
            await db.Database.ExecuteSqlRawAsync("""INSERT INTO "Ranges" ("Lo", "Hi", "Owner", "Epoch", "LeaseUntil") VALUES ({0}, {1}, NULL, {2}, 0)""", at, hi, rest);
            statements += 4;
            if (at == want.Lo) (lo, epoch) = (at, rest); else hi = at;
        }
        await tx.CommitAsync();
        return statements;
    }

    private static async Task<long> NextEpochAsync(VpDb db) =>
        (await db.Database.SqlQueryRaw<long>("""UPDATE "Epochs" SET "Next" = "Next" + 1 WHERE "Id" = 0 RETURNING "Next" AS "Value" """).ToListAsync())[0];

    /// <summary>
    /// The fenced commit of a flush. Returns false when a fence did not match (the batch was rolled back whole).
    /// In the batch: (i) k fence rows and (ii) one fence row plus k position rows ride as concurrency tokens in the one
    /// SaveChanges. Checked: the fence statements run first, (i) one per virtual partition, (ii) one.
    /// </summary>
    public async Task<bool> CommitAsync(Ownership o, IReadOnlyList<Turn> turns, long leaseMs)
    {
        var byVp = turns.GroupBy(t => t.Vp).ToDictionary(g => g.Key, g => g.Count());
        foreach (var vp in byVp.Keys) if (!o.Owns(vp)) throw new InvalidOperationException($"{o.Node} does not hold virtual partition {vp}");
        await using var db = create();
        db.Database.AutoSavepointsEnabled = false;      // ENGINES.md E4
        await using var tx = await db.Database.BeginTransactionAsync();
        var lease = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + leaseMs;
        try
        {
            if (fence == Fence.PerVirtualPartition)
            {
                foreach (var (vp, k) in byVp)
                {
                    if (fenceInBatch)
                    {
                        var row = db.Attach(new PartitionEntity { Id = vp, Epoch = o.VpEpoch[vp], Position = o.Position[vp] }).Entity;
                        row.Position = o.Position[vp] + k;
                        row.LeaseUntil = lease;
                        db.Entry(row).Property(p => p.LeaseUntil).IsModified = true;
                    }
                    else if (await db.Database.ExecuteSqlRawAsync(
                        $$"""UPDATE "Partitions" SET "Position" = "Position" + {0}, "LeaseUntil" = {{Now}} + {1} WHERE "Id" = {2} AND "Epoch" = {3} AND "Position" = {4}""",
                        k, leaseMs, vp, o.VpEpoch[vp], o.Position[vp]) != 1) { await tx.RollbackAsync(); return false; }
                }
            }
            else
            {
                if (fenceInBatch)
                {
                    var row = db.Attach(new RangeEntity { Lo = o.Range.Lo, Hi = o.Range.Hi, Epoch = o.Epoch }).Entity;
                    row.LeaseUntil = lease;
                    db.Entry(row).Property(r => r.LeaseUntil).IsModified = true;
                }
                else if (await db.Database.ExecuteSqlRawAsync(
                    $$"""UPDATE "Ranges" SET "LeaseUntil" = {{Now}} + {0} WHERE "Lo" = {1} AND "Hi" = {2} AND "Epoch" = {3}""",
                    leaseMs, o.Range.Lo, o.Range.Hi, o.Epoch) != 1) { await tx.RollbackAsync(); return false; }
                foreach (var (vp, k) in byVp)
                {
                    var row = db.Attach(new VpPositionEntity { Vp = vp, Position = o.Position[vp] }).Entity;
                    row.Position = o.Position[vp] + k;
                }
            }
            var next = byVp.Keys.ToDictionary(vp => vp, vp => o.Position[vp]);
            foreach (var turn in turns)
            {
                var row = new InstanceEntity { Id = turn.Instance, Partition = turn.Vp, Model = "sequence", Definition = "s", Status = Kernel.Durable.InstanceStatus.Running, State = turn.State, Revision = turn.Revision, History = Kernel.Durable.History.Full, StateVersion = 1 };
                if (turn.Revision == 1) db.Instances.Add(row);
                else
                {
                    var entry = db.Instances.Attach(row);
                    entry.Property(i => i.Revision).OriginalValue = turn.Revision - 1;
                    entry.State = EntityState.Modified;
                }
                if (turn.Consumed is { } id)
                {
                    var message = db.Messages.Attach(new MessageEntity { Id = id, InboxScope = 0, Done = false }).Entity;
                    db.Messages.Remove(message);
                }
                db.Journal.Add(new JournalEntity { Partition = turn.Vp, Position = ++next[turn.Vp], Instance = turn.Instance, Revision = turn.Revision, Entries = turn.Journal });
            }
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(); return false; }
        catch (DbUpdateException)
        {
            // ENGINES.md E1: a stale owner collides with its successor's rows before the fence is checked.
            await tx.RollbackAsync();
            if (fenceInBatch && !await HoldsAsync(o, byVp.Keys)) return false;
            throw;
        }
        foreach (var (vp, k) in byVp) o.Position[vp] += k;
        return true;
    }

    private async Task<bool> HoldsAsync(Ownership o, IEnumerable<int> vps)
    {
        await using var db = create();
        if (fence == Fence.PerVirtualPartition)
        {
            var rows = await db.Partitions.AsNoTracking().Where(p => p.Id >= o.Range.Lo && p.Id < o.Range.Hi).ToDictionaryAsync(p => p.Id);
            return vps.All(vp => rows.TryGetValue(vp, out var p) && p.Epoch == o.VpEpoch[vp] && p.Position == o.Position[vp]);
        }
        var range = await db.Ranges.AsNoTracking().SingleOrDefaultAsync(r => r.Lo == o.Range.Lo);
        return range is not null && range.Epoch == o.Epoch && range.Hi == o.Range.Hi;
    }

    /// <summary>An empty commit: renew the lease. (i) Every row of the range in one statement; (ii) the one row. Returns rows touched.</summary>
    public async Task<int> BeatAsync(Ownership o, long leaseMs)
    {
        await using var db = create();
        return fence == Fence.PerVirtualPartition
            ? await db.Database.ExecuteSqlRawAsync($$"""UPDATE "Partitions" SET "LeaseUntil" = {{Now}} + {0} WHERE "Id" >= {1} AND "Id" < {2} AND "Owner" = {3}""", leaseMs, o.Range.Lo, o.Range.Hi, o.Node)
            : await db.Database.ExecuteSqlRawAsync($$"""UPDATE "Ranges" SET "LeaseUntil" = {{Now}} + {0} WHERE "Lo" = {1} AND "Hi" = {2} AND "Epoch" = {3}""", leaseMs, o.Range.Lo, o.Range.Hi, o.Epoch);
    }

    /// <summary>
    /// A cooperative rebalance: the owner gives up the tail <c>[at, Hi)</c> of its range. (i) One statement frees the
    /// rows; the owner keeps the rest untouched. (ii) The owner splits its row: the kept row gets a new epoch and bound,
    /// a new free row covers the tail; the owner adopts the new epoch itself, so its next commit lands without a reset.
    /// </summary>
    public async Task<bool> ReleaseTailAsync(Ownership o, int at)
    {
        if (at <= o.Range.Lo || at >= o.Range.Hi) throw new ArgumentOutOfRangeException(nameof(at));
        await using var db = create();
        await using var tx = await db.Database.BeginTransactionAsync();
        if (fence == Fence.PerVirtualPartition)
        {
            var rows = await db.Database.ExecuteSqlRawAsync("""UPDATE "Partitions" SET "Owner" = NULL WHERE "Id" >= {0} AND "Id" < {1} AND "Owner" = {2}""", at, o.Range.Hi, o.Node);
            if (rows != o.Range.Hi - at) { await tx.RollbackAsync(); return false; }
        }
        else
        {
            var kept = await NextEpochAsync(db);
            var freed = await NextEpochAsync(db);
            if (await db.Database.ExecuteSqlRawAsync("""UPDATE "Ranges" SET "Hi" = {0}, "Epoch" = {1} WHERE "Lo" = {2} AND "Hi" = {3} AND "Epoch" = {4}""", at, kept, o.Range.Lo, o.Range.Hi, o.Epoch) != 1) { await tx.RollbackAsync(); return false; }
            await db.Database.ExecuteSqlRawAsync("""INSERT INTO "Ranges" ("Lo", "Hi", "Owner", "Epoch", "LeaseUntil") VALUES ({0}, {1}, NULL, {2}, 0)""", at, o.Range.Hi, freed);
            o.Epoch = kept;
        }
        await tx.CommitAsync();
        foreach (var vp in Enumerable.Range(at, o.Range.Hi - at)) { o.Position.Remove(vp); o.VpEpoch.Remove(vp); }
        o.Range = new VpRange(o.Range.Lo, at);
        return true;
    }

    /// <summary>
    /// (ii) Merge two adjacent ranges this node holds into one row: the first grows to the second's bound at a new
    /// epoch, the second is deleted. (i) Nothing to write: the node's two sets of rows become one in memory.
    /// </summary>
    public async Task<bool> MergeAsync(Ownership first, Ownership second)
    {
        if (first.Node != second.Node || first.Range.Hi != second.Range.Lo) throw new ArgumentException("not adjacent ranges of one node");
        if (fence == Fence.PerRange)
        {
            await using var db = create();
            await using var tx = await db.Database.BeginTransactionAsync();
            var epoch = await NextEpochAsync(db);
            if (await db.Database.ExecuteSqlRawAsync("""UPDATE "Ranges" SET "Hi" = {0}, "Epoch" = {1} WHERE "Lo" = {2} AND "Hi" = {3} AND "Epoch" = {4}""", second.Range.Hi, epoch, first.Range.Lo, first.Range.Hi, first.Epoch) != 1
                || await db.Database.ExecuteSqlRawAsync("""DELETE FROM "Ranges" WHERE "Lo" = {0} AND "Epoch" = {1}""", second.Range.Lo, second.Epoch) != 1)
            { await tx.RollbackAsync(); return false; }
            await tx.CommitAsync();
            first.Epoch = epoch;
        }
        foreach (var (vp, position) in second.Position) { first.Position[vp] = position; if (second.VpEpoch.TryGetValue(vp, out var e)) first.VpEpoch[vp] = e; }
        first.Range = first.Range.Merge(second.Range);
        return true;
    }

    /// <summary>A message from outside, as the API inserts it.</summary>
    public async Task AddAsync(string id, int vp, string target)
    {
        await using var db = create();
        db.Messages.Add(new MessageEntity { Id = id, InboxScope = 0, Partition = vp, Target = target, Kind = "start", Seq = postgres ? 0 : ++_seq, Start = "{\"model\":\"sequence\"}" });
        await db.SaveChangesAsync();
    }

    /// <summary>Let a lease run out without waiting: the test's clock.</summary>
    public async Task ExpireAsync(string node)
    {
        await using var db = create();
        if (fence == Fence.PerVirtualPartition) await db.Database.ExecuteSqlRawAsync("""UPDATE "Partitions" SET "LeaseUntil" = 0 WHERE "Owner" = {0}""", node);
        else await db.Database.ExecuteSqlRawAsync("""UPDATE "Ranges" SET "LeaseUntil" = 0 WHERE "Owner" = {0}""", node);
    }

    /// <summary>The journal-position semantics: every virtual partition's positions are 1..n with no gap, and its position row says n.</summary>
    public async Task<(bool Ok, string Report)> VerifyJournalAsync(int count)
    {
        await using var db = create();
        var journal = await db.Journal.AsNoTracking().Select(j => new { j.Partition, j.Position }).ToListAsync();
        var positions = fence == Fence.PerVirtualPartition
            ? await db.Partitions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Position)
            : await db.VpPositions.AsNoTracking().ToDictionaryAsync(v => v.Vp, v => v.Position);
        var faults = new List<string>();
        foreach (var group in journal.GroupBy(j => j.Partition))
        {
            var sorted = group.Select(j => j.Position).Order().ToList();
            if (!sorted.SequenceEqual(Enumerable.Range(1, sorted.Count).Select(i => (long)i))) faults.Add($"vp {group.Key}: positions [{string.Join(",", sorted)}] are not 1..{sorted.Count}");
            if (positions[group.Key] != sorted.Count) faults.Add($"vp {group.Key}: position row says {positions[group.Key]}, the journal has {sorted.Count}");
        }
        foreach (var (vp, position) in positions) if (position > 0 && !journal.Any(j => j.Partition == vp)) faults.Add($"vp {vp}: position {position} with no journal rows");
        var rows = fence == Fence.PerVirtualPartition ? $"{count} fence rows" : $"{await db.Ranges.CountAsync()} range rows, {count} position rows";
        return (faults.Count == 0, faults.Count == 0 ? $"{journal.Count} journal rows over {journal.Select(j => j.Partition).Distinct().Count()} virtual partitions, gapless; {rows}" : string.Join("; ", faults));
    }
}
