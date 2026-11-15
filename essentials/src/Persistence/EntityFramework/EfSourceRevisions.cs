using Elsa.Persistence.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Persistence.EntityFramework;

/// <summary>One CAS/snapshot/pin implementation for typed domain adapters. Scope comes from trusted composition.</summary>
public sealed class EfSourceRevisions(DbContext db, SourceScope scope, bool singleCopyPerSource = false) : ISourceRevisionReader, ISourceRevisionWriter
{
    private string ScopeKey => ScopeHash(scope);
    public static string ScopeHash(SourceScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Identity(scope.Partition); Identity(scope.Kind); Identity(scope.Authority);
        if (scope.TenantId is not null) Identity(scope.TenantId);
        return EfRelationalIdentity.HashLengthFramed(scope.Partition, scope.TenantId is null ? "global" : "tenant", scope.TenantId ?? "", scope.Kind);
    }
    public static string CopyKey(SourceScope scope, string sourceId, string copyId)
    {
        Identity(sourceId); Identity(copyId);
        return EfRelationalIdentity.HashLengthFramed(ScopeHash(scope), sourceId, copyId);
    }
    private static void Identity(string value, int maximum = 256)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) throw new ArgumentException("Invalid source revision identity.");
    }
    // SQL Server pads equality even under BIN2. Authority is ownership, not part of
    // ScopeHash, so compare its exact text before a query counts or pages rows.
    private IQueryable<SourceWorkingCopy> Copies => db.Set<SourceWorkingCopy>().Where(x => x.ScopeKey == ScopeKey && x.Partition == scope.Partition && x.TenantId == scope.TenantId && x.Kind == scope.Kind && x.Authority + "|" == scope.Authority + "|");
    public static void ValidateCopy(SourceWorkingCopy copy, SourceScope scope, string sourceId, string copyId)
    {
        if (copy.ScopeKey != ScopeHash(scope) || copy.Partition != scope.Partition || copy.TenantId != scope.TenantId ||
            copy.Kind != scope.Kind || copy.Authority != scope.Authority || copy.SourceId != sourceId || copy.CopyId != copyId ||
            copy.SourceIdentity is not null && copy.SourceIdentity != EfRelationalIdentity.HashLengthFramed(ScopeHash(scope), sourceId) || copy.Key != CopyKey(scope, sourceId, copyId) || copy.Revision < 0 || copy.Fence < 0)
            throw new SourceRevisionException("identity-conflict");
    }
    private void Verify(SourceWorkingCopy copy)
    {
        ValidateCopy(copy, scope, copy.SourceId, copy.CopyId);
        if (singleCopyPerSource && !copy.Discarded && copy.SourceIdentity is null) throw new SourceRevisionException("identity-conflict");
    }
    private async Task<SourceWorkingCopy?> ReadCopy(string sourceId, string copyId, CancellationToken ct, bool discarded = false)
    {
        var key = CopyKey(scope, sourceId, copyId);
        var stamped = await Copies.AsNoTracking().Where(x => x.Key == key)
            .Select(x => new { Row = x, Stamp = EF.Property<string>(x, EfSchemaVersion.ColumnName) }).SingleOrDefaultAsync(ct);
        if (stamped is null) return null;
        EfSchemaVersion.EnsureCurrent(EfSourceModel.Chain, stamped.Stamp);
        var row = stamped.Row;
        ValidateCopy(row, scope, sourceId, copyId);
        Verify(row);
        return row.Discarded && !discarded ? null : row;
    }
    public async Task<SourceWorkingCopy?> FindAsync(string sourceId, string copyId, CancellationToken cancellationToken = default)
    {
        try { return await ReadCopy(sourceId, copyId, cancellationToken); }
        catch (Exception e) when (ProviderFailure(e)) { throw new SourceRevisionException("storage-unavailable", e); }
    }
    public async Task<SourceSnapshot?> FindSnapshotAsync(string sourceId, string copyId, string snapshotId, CancellationToken cancellationToken = default)
    {
        try
        {
            var copy = await ReadCopy(sourceId, copyId, cancellationToken, true);
            if (copy is null) return null;
            var stamped = await db.Set<SourceSnapshot>().AsNoTracking().Where(x => x.Key == snapshotId && x.WorkingCopyKey == copy.Key)
                .Select(x => new { Row = x, Stamp = EF.Property<string>(x, EfSchemaVersion.ColumnName) }).SingleOrDefaultAsync(cancellationToken);
            if (stamped is null) return null;
            EfSchemaVersion.EnsureCurrent(EfSourceModel.Chain, stamped.Stamp);
            if (!StringComparer.Ordinal.Equals(stamped.Row.Key, snapshotId)) return null;
            VerifySnapshot(stamped.Row, copy.Key);
            return stamped.Row;
        }
        catch (Exception e) when (ProviderFailure(e)) { throw new SourceRevisionException("storage-unavailable", e); }
    }
    public async Task<SourceSnapshotRead?> FindSourceSnapshotAsync(string sourceId, string snapshotId, CancellationToken cancellationToken = default)
    {
        Identity(sourceId); Identity(snapshotId);
        try
        {
            var row = await (from snapshot in db.Set<SourceSnapshot>().AsNoTracking()
                             join copy in Copies.AsNoTracking() on snapshot.WorkingCopyKey equals copy.Key
                             where copy.SourceId + "|" == sourceId + "|" && snapshot.Key == snapshotId
                             select new { Copy = copy, Snapshot = snapshot, CopyStamp = EF.Property<string>(copy, EfSchemaVersion.ColumnName),
                                 SnapshotStamp = EF.Property<string>(snapshot, EfSchemaVersion.ColumnName) }).SingleOrDefaultAsync(cancellationToken);
            if (row is null || !StringComparer.Ordinal.Equals(row.Snapshot.Key, snapshotId)) return null;
            VerifyReadSnapshot(sourceId, row.Copy, row.Snapshot, row.CopyStamp, row.SnapshotStamp);
            return new(row.Copy.CopyId, row.Snapshot);
        }
        catch (Exception exception) when (ProviderFailure(exception)) { throw new SourceRevisionException("storage-unavailable", exception); }
    }

    public async Task<SourceSnapshotPage> HistorySourceAsync(string sourceId, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        Identity(sourceId);
        if (skip < 0 || take is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(take));
        try
        {
            var query = from snapshot in db.Set<SourceSnapshot>().AsNoTracking()
                        join copy in Copies.AsNoTracking() on snapshot.WorkingCopyKey equals copy.Key
                        where copy.SourceId + "|" == sourceId + "|"
                        select new { Copy = copy, Snapshot = snapshot, CopyStamp = EF.Property<string>(copy, EfSchemaVersion.ColumnName),
                            SnapshotStamp = EF.Property<string>(snapshot, EfSchemaVersion.ColumnName) };
            var totals = query.GroupBy(x => 1).Select(group => new { Total = group.Count() });
            var page = query.OrderBy(x => x.Snapshot.Key).Skip(skip).Take(take);
            var rows = await (from total in totals from item in page.DefaultIfEmpty()
                              select new { total.Total, item.Copy, item.Snapshot, item.CopyStamp, item.SnapshotStamp }).ToListAsync(cancellationToken);
            var items = new List<SourceSnapshotRead>();
            foreach (var row in rows.Where(x => x.Snapshot is not null))
            {
                VerifyReadSnapshot(sourceId, row.Copy, row.Snapshot, row.CopyStamp, row.SnapshotStamp);
                items.Add(new(row.Copy.CopyId, row.Snapshot));
            }
            return new(rows.FirstOrDefault()?.Total ?? 0, items);
        }
        catch (Exception exception) when (ProviderFailure(exception)) { throw new SourceRevisionException("storage-unavailable", exception); }
    }

    private void VerifyReadSnapshot(string sourceId, SourceWorkingCopy copy, SourceSnapshot snapshot, string? copyStamp, string? snapshotStamp)
    {
        EfSchemaVersion.EnsureCurrent(EfSourceModel.Chain, copyStamp);
        EfSchemaVersion.EnsureCurrent(EfSourceModel.Chain, snapshotStamp);
        ValidateCopy(copy, scope, sourceId, copy.CopyId);
        Verify(copy); VerifySnapshot(snapshot, copy.Key);
    }

    public async Task<SourcePage> ListAsync(string? search, int skip, int take, CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(take));
        try
        {
            var query = Copies.AsNoTracking().Where(x => !x.Discarded);
            if (!string.IsNullOrEmpty(search)) { Identity(search, 512); var key = search.ToUpperInvariant(); query = query.Where(x => x.NameSearchKey.Contains(key)); }
            // An aggregate left-joined to a bounded page preserves totals for an out-of-range page.
            var totals = query.GroupBy(x => 1).Select(group => new { Total = group.Count() });
            var page = query.OrderBy(x => x.Key).Skip(skip).Take(take);
            var result = await (from total in totals
                                from item in page.DefaultIfEmpty()
                                select new { total.Total, Item = item, Stamp = item == null ? null : EF.Property<string>(item, EfSchemaVersion.ColumnName) }).ToListAsync(cancellationToken);
            foreach (var entry in result.Where(x => x.Item is not null)) EfSchemaVersion.EnsureCurrent(EfSourceModel.Chain, entry.Stamp);
            var items = result.Where(x => x.Item is not null).Select(x => x.Item).ToArray();
            foreach (var item in items) Verify(item);
            return new(result.FirstOrDefault()?.Total ?? 0, items);
        }
        catch (Exception e) when (ProviderFailure(e)) { throw new SourceRevisionException("storage-unavailable", e); }
    }
    public async Task<SourcePage> ListSourcesAsync(string? search, int skip, int take, CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(take));
        try
        {
            var query = Copies.AsNoTracking();
            if (!string.IsNullOrEmpty(search))
            {
                Identity(search, 512);
                var key = search.ToUpperInvariant();
                var matchingSources = query.Where(x => x.NameSearchKey.Contains(key)).Select(x => x.SourceId + "|");
                query = query.Where(x => matchingSources.Contains(x.SourceId + "|"));
            }
            // SQL Server pads string equality even with BIN2. A non-space terminator
            // makes trailing spaces significant without changing persisted identities.
            var totals = query.Select(x => x.SourceId + "|").Distinct().GroupBy(x => 1).Select(group => new { Total = group.Count() });
            // A provider-translatable anti-join selects one copy without loading or
            // grouping authored content in memory. Active copies precede discarded ones.
            var sources = query.Where(copy => !query.Any(other => other.SourceId + "|" == copy.SourceId + "|" &&
                (copy.Discarded && !other.Discarded || copy.Discarded == other.Discarded && string.Compare(other.Key, copy.Key) < 0)));
            var page = sources.OrderBy(x => x.SourceId + "|").Skip(skip).Take(take);
            var rows = await (from total in totals from item in page.DefaultIfEmpty()
                              select new { total.Total, Item = item, Stamp = item == null ? null : EF.Property<string>(item, EfSchemaVersion.ColumnName) }).ToListAsync(cancellationToken);
            var items = rows.Where(x => x.Item is not null).Select(x => x.Item).ToArray();
            foreach (var row in rows.Where(x => x.Item is not null)) { EfSchemaVersion.EnsureCurrent(EfSourceModel.Chain, row.Stamp); Verify(row.Item); }
            return new(rows.FirstOrDefault()?.Total ?? 0, items);
        }
        catch (Exception exception) when (ProviderFailure(exception)) { throw new SourceRevisionException("storage-unavailable", exception); }
    }

    public async Task<IReadOnlyList<SourceSnapshot>> HistoryAsync(string sourceId, string copyId, int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(take));
        try
        {
            var copy = await ReadCopy(sourceId, copyId, cancellationToken, true);
            if (copy is null) return [];
            var rows = await db.Set<SourceSnapshot>().AsNoTracking().Where(x => x.WorkingCopyKey == copy.Key)
                .OrderBy(x => x.Revision).ThenBy(x => x.Key).Skip(skip).Take(take)
                .Select(x => new { Row = x, Stamp = EF.Property<string>(x, EfSchemaVersion.ColumnName) }).ToListAsync(cancellationToken);
            foreach (var entry in rows) { EfSchemaVersion.EnsureCurrent(EfSourceModel.Chain, entry.Stamp); VerifySnapshot(entry.Row, copy.Key); }
            return rows.Select(x => x.Row).ToArray();
        }
        catch (Exception e) when (ProviderFailure(e)) { throw new SourceRevisionException("storage-unavailable", e); }
    }
    public static SourceWorkingCopy NewCopy(SourceScope scope, string sourceId, string copyId, string name, string profile, string content)
    {
        Identity(name, 512); Identity(profile, 128); ArgumentNullException.ThrowIfNull(content);
        return new() { Key = CopyKey(scope, sourceId, copyId), ScopeKey = ScopeHash(scope), Partition = scope.Partition,
            TenantId = scope.TenantId, Kind = scope.Kind, Authority = scope.Authority, SourceId = sourceId, CopyId = copyId,
            Name = name, NameSearchKey = name.ToUpperInvariant(), Profile = profile, Content = content };
    }
    /// <summary>Stages the same CAS inside a domain's existing atomic command/layout transaction.</summary>
    public static void StageSave(DbContext db, SourceWorkingCopy row, SourceScope scope, string sourceId, string copyId, long expectedRevision, string name, string profile, string content)
    {
        Identity(name, 512); Identity(profile, 128); ArgumentNullException.ThrowIfNull(content);
        ValidateCopy(row, scope, sourceId, copyId);
        if (row.Discarded || row.Revision != expectedRevision) throw new SourceRevisionException("revision-conflict");
        if (db.Entry(row).State == EntityState.Detached) db.Attach(row);
        db.Entry(row).Property(x => x.Revision).OriginalValue = expectedRevision;
        row.Content = content; row.Name = name; row.NameSearchKey = name.ToUpperInvariant(); row.Profile = profile;
        row.Revision = checked(expectedRevision + 1);
    }
    public Task<SourceWorkingCopy> CreateAsync(string sourceId, string copyId, string name, string profile, string content, CancellationToken cancellationToken = default) =>
        Write(async ct => { var row = NewCopy(scope, sourceId, copyId, name, profile, content); if (singleCopyPerSource) row.SourceIdentity = EfRelationalIdentity.HashLengthFramed(ScopeHash(scope), sourceId); RejectTrackedDuplicate(row); Add(row); await Task.CompletedTask; return row; }, cancellationToken);
    public Task<SourceWorkingCopy> ForkAsync(string sourceId, string copyId, string snapshotId, string newSourceId, string newCopyId, string name, CancellationToken cancellationToken = default) =>
        Write(async ct =>
        {
            var origin = await RequireCopy(sourceId, copyId, ct, true);
            var snapshot = await RequireSnapshot(snapshotId, origin.Key, ct);
            Touch(origin);
            var copy = NewCopy(scope, newSourceId, newCopyId, name, snapshot.Profile, snapshot.Content);
            if (singleCopyPerSource) copy.SourceIdentity = EfRelationalIdentity.HashLengthFramed(ScopeHash(scope), newSourceId);
            copy.BaseSnapshotId = snapshot.Key;
            RejectTrackedDuplicate(copy); Add(copy); Add(NewPin(snapshot.Key, "fork:" + copy.Key));
            return copy;
        }, cancellationToken);
    public Task<SourceWorkingCopy> SaveAsync(string sourceId, string copyId, long expectedRevision, string name, string profile, string content, CancellationToken cancellationToken = default) =>
        Write(async ct => { var row = await RequireCopy(sourceId, copyId, ct); StageSave(db, row, scope, sourceId, copyId, expectedRevision, name, profile, content); return row; }, cancellationToken);
    public Task<SourceSnapshot> CaptureAsync(string sourceId, string copyId, long expectedRevision, string operationId, CancellationToken cancellationToken = default)
    {
        Identity(operationId);
        return Write(async ct =>
        {
            var copy = await RequireCopy(sourceId, copyId, ct, true);
            var key = EfRelationalIdentity.HashLengthFramed(copy.Key, operationId);
            var stamped = await db.Set<SourceSnapshot>().AsNoTracking().Where(x => x.Key == key)
                .Select(x => new { Row = x, Stamp = EF.Property<string>(x, EfSchemaVersion.ColumnName) }).SingleOrDefaultAsync(ct);
            if (stamped is not null) EfSchemaVersion.EnsureCurrent(EfSourceModel.Chain, stamped.Stamp);
            var existing = stamped?.Row;
            if (existing is not null)
            {
                VerifySnapshot(existing, copy.Key);
                if (existing.OperationId != operationId || existing.Revision != expectedRevision) throw new SourceRevisionException("operation-conflict");
                return existing;
            }
            if (copy.Discarded) throw new SourceRevisionException("not-found");
            if (copy.Revision != expectedRevision) throw new SourceRevisionException("revision-conflict");
            Touch(copy);
            var snapshot = new SourceSnapshot { Key = key, WorkingCopyKey = copy.Key, OperationId = operationId,
                Profile = copy.Profile, Content = copy.Content, Revision = copy.Revision, BaseSnapshotId = copy.BaseSnapshotId,
                Fingerprint = EfRelationalIdentity.HashLengthFramed(copy.Profile, copy.Content) };
            Add(snapshot);
            Add(NewPin(key, "capture:" + operationId));
            return snapshot;
        }, cancellationToken);
    }
    public Task PinAsync(string sourceId, string copyId, string snapshotId, string owner, CancellationToken cancellationToken = default) => ChangePin(sourceId, copyId, snapshotId, owner, false, cancellationToken);
    public Task UnpinAsync(string sourceId, string copyId, string snapshotId, string owner, CancellationToken cancellationToken = default) => ChangePin(sourceId, copyId, snapshotId, owner, true, cancellationToken);
    private async Task ChangePin(string sourceId, string copyId, string snapshotId, string owner, bool remove, CancellationToken cancellationToken)
    {
        Identity(owner, 512);
        await Write(async ct =>
        {
            var copy = await RequireCopy(sourceId, copyId, ct, true);
            await RequireSnapshot(snapshotId, copy.Key, ct);
            Touch(copy);
            var value = NewPin(snapshotId, owner);
            var stamped = await db.Set<SourceSnapshotPin>().Where(x => x.Key == value.Key)
                .Select(x => new { Row = x, Stamp = EF.Property<string>(x, EfSchemaVersion.ColumnName) }).SingleOrDefaultAsync(ct);
            if (stamped is not null) EfSchemaVersion.EnsureCurrent(EfSourceModel.Chain, stamped.Stamp);
            var existing = stamped?.Row;
            if (existing is not null && (existing.SnapshotKey != snapshotId || existing.Owner != owner)) throw new SourceRevisionException("identity-conflict");
            if (remove && existing is not null) db.Remove(existing);
            if (!remove && existing is null) Add(value);
            return true;
        }, cancellationToken);
    }
    public async Task DeleteSnapshotAsync(string sourceId, string copyId, string snapshotId, CancellationToken cancellationToken = default) =>
        await Write(async ct =>
        {
            var copy = await RequireCopy(sourceId, copyId, ct, true); Touch(copy);
            var snapshot = await RequireSnapshot(snapshotId, copy.Key, ct);
            if (await db.Set<SourceSnapshotPin>().AnyAsync(x => x.SnapshotKey == snapshotId, ct)) throw new SourceRevisionException("snapshot-pinned");
            // Retain operation identity while this copy can still capture; retired copies cannot recreate it.
            if (!copy.Discarded) throw new SourceRevisionException("source-active");
            db.Remove(snapshot); return true;
        }, cancellationToken);
    public async Task DiscardAsync(string sourceId, string copyId, long expectedRevision, CancellationToken cancellationToken = default) =>
        await Write(async ct =>
        {
            var copy = await RequireCopy(sourceId, copyId, ct);
            StageDiscard(db, copy, scope, sourceId, copyId, expectedRevision); return true;
        }, cancellationToken);
    private async Task<SourceWorkingCopy> RequireCopy(string sourceId, string copyId, CancellationToken ct, bool discarded = false)
    {
        // Never use a previously tracked copy as authorization/concurrency evidence.
        var row = await ReadCopy(sourceId, copyId, ct, discarded) ?? throw new SourceRevisionException("not-found");
        var tracked = db.ChangeTracker.Entries<SourceWorkingCopy>().SingleOrDefault(x => x.Entity.Key == row.Key);
        if (tracked is not null) tracked.State = EntityState.Detached;
        db.Attach(row);
        // The untracked read checked this stamp, but shadow values do not travel with the detached entity.
        var stamp = db.Entry(row).Property<string>(EfSchemaVersion.ColumnName);
        stamp.OriginalValue = stamp.CurrentValue = EfSourceModel.SchemaVersion;
        return row;
    }
    /// <summary>Retires a source inside its domain's existing discard transaction without deleting history.</summary>
    public static void StageDiscard(DbContext db, SourceWorkingCopy copy, SourceScope scope, string sourceId, string copyId, long expectedRevision)
    {
        ValidateCopy(copy, scope, sourceId, copyId);
        if (copy.Discarded || copy.Revision != expectedRevision) throw new SourceRevisionException("revision-conflict");
        if (db.Entry(copy).State == EntityState.Detached) db.Attach(copy);
        db.Entry(copy).Property(x => x.Revision).OriginalValue = expectedRevision;
        copy.Discarded = true; copy.SourceIdentity = null; copy.Revision = checked(copy.Revision + 1); Touch(copy);
    }
    private void RejectTrackedDuplicate(SourceWorkingCopy row)
    {
        if (db.ChangeTracker.Entries<SourceWorkingCopy>().Any(x => x.Entity.Key == row.Key)) throw new SourceRevisionException("identity-conflict");
    }
    private static void Touch(SourceWorkingCopy copy) => copy.Fence = checked(copy.Fence + 1);
    // Composed modules check stamps before SQL runs; a database default alone is too late for admission.
    private void Add<T>(T row) where T : class =>
        db.Add(row).Property<string>(EfSchemaVersion.ColumnName).CurrentValue = EfSourceModel.SchemaVersion;
    private static SourceSnapshotPin NewPin(string snapshotId, string owner) => new() { Key = EfRelationalIdentity.HashLengthFramed(snapshotId, owner), SnapshotKey = snapshotId, Owner = owner };
    private static void VerifySnapshot(SourceSnapshot row, string copyKey)
    {
        if (row.WorkingCopyKey != copyKey || row.Key != EfRelationalIdentity.HashLengthFramed(copyKey, row.OperationId) ||
            row.Fingerprint != EfRelationalIdentity.HashLengthFramed(row.Profile, row.Content) || row.Revision < 0)
            throw new SourceRevisionException("identity-conflict");
    }
    private async Task<SourceSnapshot> RequireSnapshot(string key, string copyKey, CancellationToken ct)
    {
        var stamped = await db.Set<SourceSnapshot>().Where(x => x.Key == key && x.WorkingCopyKey == copyKey)
            .Select(x => new { Row = x, Stamp = EF.Property<string>(x, EfSchemaVersion.ColumnName) }).SingleOrDefaultAsync(ct) ?? throw new SourceRevisionException("not-found");
        EfSchemaVersion.EnsureCurrent(EfSourceModel.Chain, stamped.Stamp);
        var row = stamped.Row;
        if (!StringComparer.Ordinal.Equals(row.Key, key)) throw new SourceRevisionException("not-found");
        VerifySnapshot(row, copyKey); return row;
    }
    private async Task<T> Write<T>(Func<CancellationToken, Task<T>> stage, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Use the staged source operation inside a domain-owned transaction.");
        if (db.ChangeTracker.HasChanges()) throw new SourceRevisionException("pending-changes");
        IDbContextTransaction? transaction = null;
        var committing = false;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await stage(ct); await db.SaveChangesAsync(ct);
            committing = true; await transaction.CommitAsync(ct); return result;
        }
        catch (Exception e) when (ProviderFailure(e))
        {
            db.ChangeTracker.Clear();
            throw new SourceRevisionException(committing ? "outcome-unknown" : e is DbUpdateConcurrencyException ? "revision-conflict" : EfRelationalExceptionClassifier.IsUniqueConstraintViolation(e) ? "identity-conflict" : "storage-unavailable", e);
        }
        catch (OperationCanceledException e) when (committing)
        {
            db.ChangeTracker.Clear(); throw new SourceRevisionException("outcome-unknown", e);
        }
        catch { db.ChangeTracker.Clear(); throw; }
        finally
        {
            if (transaction is not null)
            {
                try { await transaction.DisposeAsync(); }
                catch (Exception e) when (ProviderFailure(e)) { /* Preserve the operation result/refusal; commit failures were classified above. */ }
            }
        }
    }
    private static bool ProviderFailure(Exception e) => e is DbUpdateException or System.Data.Common.DbException;
}
