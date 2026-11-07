using System.Text.Json;
using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

// The host/family owner must capture actual current authorized context, never echo a request or a broker header.
// This is an internal integration seam, not a permission/capability granted by constructing a partition value.
internal interface IRuntimeDeliveryDiscoveryOwner
{
    RuntimeDeliveryDiscoveryPartition CaptureCurrent();
}

/// <summary>Unregistered SQLite read/claim/renew mechanics. Complete source participation remains unavailable.</summary>
internal sealed class EfRuntimeDeliverySourceStore : IRuntimeDeliverySourceStore
{
    private readonly RuntimeDbContext context;
    private readonly IPersistenceAccessContextAccessor access;
    private readonly IRuntimeDeliveryDiscoveryOwner owner;
    private readonly TimeProvider clock;
    private readonly IReadOnlyDictionary<string, RuntimeDeliveryCanonicalFamilyReader> readers;

    internal EfRuntimeDeliverySourceStore(RuntimeDbContext context, IPersistenceAccessContextAccessor access,
        IRuntimeDeliveryDiscoveryOwner owner, IReadOnlyCollection<RuntimeDeliveryCanonicalFamilyReader> readers, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(access); ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(readers); ArgumentNullException.ThrowIfNull(clock);
        var copy = readers.ToArray();
        if (copy.Length > 16 || copy.Any(item => item is null || string.IsNullOrWhiteSpace(item.Family)) ||
            copy.Select(item => item.Family).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Canonical source readers must be finite, exact and unique.", nameof(readers));
        this.context = context; this.access = access; this.owner = owner; this.clock = clock;
        this.readers = copy.ToDictionary(item => item.Family, StringComparer.Ordinal);
    }

    public bool CanParticipate(RuntimeDeliveryBoundary boundary) => false;
    public ValueTask<RuntimeSourceCommitResult> CommitAsync(RuntimeSourceCommit commit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException<RuntimeSourceCommitResult>(new NotSupportedException(
            "The full selected-store tuple and typed source commit boundary are not implemented."));
    }

    public ValueTask<RuntimeDeliverySourceSnapshot?> ReadAsync(RuntimeDeliverySourceAddress source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return OwnedAsync<RuntimeDeliverySourceSnapshot?>(async () =>
        {
            var current = Current();
            if (source.Scope != current.Scope) throw new InvalidOperationException("The source belongs to another persistence scope.");
            var read = await Reader(source.Family).ReadAsync(context, source, cancellationToken);
            if (read is null) return null;
            RequireCurrent(read.Partition); await TestLedgerAsync(read.Partition, touch: false, cancellationToken);
            RequireCurrent(read.Partition);
            return read.Snapshot;
        }, cancellationToken, writes: false);
    }

    public async ValueTask<IReadOnlyList<RuntimeDeliveryWorkClaim>> ClaimAsync(RuntimeDeliveryClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await OwnedAsync<IReadOnlyList<RuntimeDeliveryWorkClaim>>(async () =>
            {
                var now = Now(); var current = Current();
                request.RequirePartition(access.Current, current, now);
                var visibleAfter = Deadline(now, request.Visibility);
                var selected = request.Families.Select(Reader).ToArray();
                if (request.WorkKinds.Any(kind => !selected.Any(reader => reader.Work.Any(item => item.Kind == kind))))
                    throw new NotSupportedException("The requested canonical work kind is unsupported.");
                await TestLedgerAsync(current, touch: false, cancellationToken);
                IQueryable<RuntimeDeliveryWorkEntity>? query = null;
                foreach (var reader in selected)
                foreach (var work in reader.Work.Where(item => request.WorkKinds.Contains(item.Kind, StringComparer.Ordinal)))
                {
                    var eligible = reader.EligibleRows(context, current, work, now.UtcTicks);
                    var candidates = reader.CandidateRows(context, current, work, now.UtcTicks);
                    // Fail closed for matching owed work with a missing/unsupported/header-mismatched source. This
                    // EXISTS is before Take; it neither clips a page nor silently skips an obligation as unsupported.
                    if (await candidates.AnyAsync(row => !eligible.Any(valid => valid.Id == row.Id), cancellationToken))
                        throw new InvalidDataException("Canonical source capability or immutable work binding is unavailable.");
                    query = query is null ? eligible : query.Concat(eligible);
                }
                if (query is null) throw new NotSupportedException("No exact canonical work capability is configured.");
                var rows = await query.OrderBy(row => row.ClaimableAtUtcTicks).ThenBy(row => row.RecordedAtUtcTicks)
                    .ThenBy(row => row.Id).Take(request.Limit).ToArrayAsync(cancellationToken);
                if (rows.Length > 0) await TestLedgerAsync(current, touch: true, cancellationToken);
                var claims = new List<RuntimeDeliveryWorkClaim>(rows.Length);
                foreach (var row in rows)
                {
                    var reader = Reader(EfRelationalIdentity.Decode(row.SourceFamily));
                    var work = reader.ReadWork(row);
                    var read = await reader.ReadAsync(context, work.Source, cancellationToken)
                        ?? throw new InvalidDataException("The canonical source is missing.");
                    Validate(row, work, read); RequireCurrent(read.Partition);
                    if (!reader.IsClaimable(read, work)) throw new DbUpdateConcurrencyException("The canonical source eligibility changed.");
                    await read.StageFenceAsync(context, cancellationToken);
                    var revision = checked(row.Revision + 1); var fence = checked(row.FencingToken + 1);
                    var changed = await Exact(row).ExecuteUpdateAsync(update => update
                        .SetProperty(item => item.Status, RuntimeDeliveryWorkTransport.Claimed)
                        .SetProperty(item => item.Revision, revision).SetProperty(item => item.FencingToken, fence)
                        .SetProperty(item => item.OwnerId, EfRelationalIdentity.Encode(request.OwnerId))
                        .SetProperty(item => item.ClaimedAtUtcTicks, (long?)now.UtcTicks)
                        .SetProperty(item => item.VisibleAfterUtcTicks, (long?)visibleAfter.UtcTicks)
                        .SetProperty(item => item.ClaimableAtUtcTicks, visibleAfter.UtcTicks), cancellationToken);
                    if (changed != 1) throw new DbUpdateConcurrencyException("The transport work claim changed.");
                    claims.Add(Claim(row, work, request.OwnerId, revision, fence, now, visibleAfter));
                }
                RequireCurrent(current);
                if (claims.Any(claim => Now() >= claim.VisibleAfter))
                    throw new DbUpdateConcurrencyException("The newly claimed visibility interval elapsed before commit.");
                return claims.AsReadOnly();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return Array.Empty<RuntimeDeliveryWorkClaim>(); }
    }

    public async ValueTask<RuntimeDeliveryWorkClaim?> RenewAsync(RuntimeDeliveryWorkClaim claim, TimeSpan visibility,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (visibility <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(visibility));
        try
        {
            return await OwnedAsync<RuntimeDeliveryWorkClaim?>(async () =>
            {
                var now = Now(); var current = Current();
                if (claim.Source.Scope != current.Scope) throw new InvalidOperationException("The claim belongs to another persistence scope.");
                var reader = Reader(claim.Source.Family);
                var id = EfRelationalIdentity.HashLengthFramed(claim.Source.Scope.Value, claim.Source.Family, claim.Source.SourceId, claim.WorkId);
                var row = await context.RuntimeDeliveryWorks.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
                if (row is null) return null;
                var work = reader.ReadWork(row);
                var read = await reader.ReadAsync(context, claim.Source, cancellationToken)
                    ?? throw new InvalidDataException("The canonical source is missing.");
                Validate(row, work, read); RequireCurrent(read.Partition);
                // Proposed finite lease rule for the new family: an expired claim must be reclaimed with a new fence.
                // It deliberately does not inherit the legacy outbox's elapsed-but-still-owned renewal exception.
                if (!reader.IsClaimable(read, work) || !Matches(row, work, claim) || row.Status != RuntimeDeliveryWorkTransport.Claimed ||
                    now < claim.ClaimedAt || now >= claim.VisibleAfter) return null;
                await TestLedgerAsync(current, touch: true, cancellationToken);
                await read.StageFenceAsync(context, cancellationToken);
                var proposed = Deadline(now, visibility);
                var visibleAfter = proposed > claim.VisibleAfter ? proposed : claim.VisibleAfter;
                var revision = checked(row.Revision + 1);
                var changed = await Exact(row).ExecuteUpdateAsync(update => update.SetProperty(item => item.Revision, revision)
                    .SetProperty(item => item.VisibleAfterUtcTicks, (long?)visibleAfter.UtcTicks)
                    .SetProperty(item => item.ClaimableAtUtcTicks, visibleAfter.UtcTicks), cancellationToken);
                if (changed != 1) throw new DbUpdateConcurrencyException("The transport work claim changed.");
                RequireCurrent(current);
                if (Now() >= claim.VisibleAfter || Now() >= visibleAfter)
                    throw new DbUpdateConcurrencyException("The visibility interval elapsed before renewal commit.");
                return Claim(row, work, claim.OwnerId, revision, claim.FencingToken, claim.ClaimedAt, visibleAfter);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return null; }
    }

    private IQueryable<RuntimeDeliveryWorkEntity> Exact(RuntimeDeliveryWorkEntity row) => context.RuntimeDeliveryWorks.Where(item =>
        item.Id == row.Id && item.Revision == row.Revision && item.SchemaVersion == row.SchemaVersion &&
        item.ScopeKey == row.ScopeKey && item.ScopeKeyHash == row.ScopeKeyHash && item.SourceFamily == row.SourceFamily &&
        item.SourceFamilyHash == row.SourceFamilyHash && item.SourceId == row.SourceId && item.SourceIdHash == row.SourceIdHash &&
        item.FullDiscoveryIdentity == row.FullDiscoveryIdentity && item.FullDiscoveryIdentityHash == row.FullDiscoveryIdentityHash &&
        item.TenantId == row.TenantId && item.TenantIdHash == row.TenantIdHash && item.RunKind == row.RunKind &&
        item.TestScopeId == row.TestScopeId && item.TestScopeIdHash == row.TestScopeIdHash && item.AuthorityPartitionKey == row.AuthorityPartitionKey &&
        item.SourceHeaderRevision == row.SourceHeaderRevision && item.SourceHeaderFingerprint == row.SourceHeaderFingerprint &&
        item.RequiredParticipantVersion == row.RequiredParticipantVersion && item.WorkId == row.WorkId && item.WorkIdHash == row.WorkIdHash &&
        item.WorkKind == row.WorkKind && item.WorkKindHash == row.WorkKindHash && item.Role == row.Role &&
        item.Status == row.Status && item.ClaimableIsEligible == row.ClaimableIsEligible && item.ClaimableAtUtcTicks == row.ClaimableAtUtcTicks &&
        item.OwnerId == row.OwnerId && item.FencingToken == row.FencingToken && item.ClaimedAtUtcTicks == row.ClaimedAtUtcTicks &&
        item.VisibleAfterUtcTicks == row.VisibleAfterUtcTicks && item.ContentFingerprint == row.ContentFingerprint && item.ContentJson == row.ContentJson);

    private static void Validate(RuntimeDeliveryWorkEntity row, RuntimeDeliveryCanonicalWork work, RuntimeDeliveryCanonicalRead read)
    {
        RuntimeDeliveryDiscoveryProjection.Validate(row, read.Partition);
        if (work.Source != read.Snapshot.Address || row.SourceHeaderRevision != read.Snapshot.Revision ||
            row.SourceHeaderFingerprint != read.Snapshot.HeaderFingerprint || row.Revision <= 0 || row.FencingToken < 0 ||
            !row.ClaimableIsEligible || (row.Status != RuntimeDeliveryWorkTransport.Pending && row.Status != RuntimeDeliveryWorkTransport.Claimed))
            throw new InvalidDataException("The source and transport work binding differ.");
        if (row.Status == RuntimeDeliveryWorkTransport.Pending)
        {
            if (row.FencingToken != 0 || row.OwnerId is not null || row.ClaimedAtUtcTicks is not null ||
                row.VisibleAfterUtcTicks is not null || row.ClaimableAtUtcTicks != work.NotBefore.UtcTicks)
                throw new InvalidDataException("The pending transport lease projections differ.");
        }
        else if (row.FencingToken <= 0 || string.IsNullOrWhiteSpace(row.OwnerId) ||
            string.IsNullOrWhiteSpace(EfRelationalIdentity.Decode(row.OwnerId)) || row.ClaimedAtUtcTicks is null or <= 0 ||
            row.VisibleAfterUtcTicks is null || row.VisibleAfterUtcTicks > DateTimeOffset.MaxValue.UtcTicks ||
            row.VisibleAfterUtcTicks <= row.ClaimedAtUtcTicks || row.ClaimableAtUtcTicks != row.VisibleAfterUtcTicks)
            throw new InvalidDataException("The retained transport lease projections differ.");
    }

    private static bool Matches(RuntimeDeliveryWorkEntity row, RuntimeDeliveryCanonicalWork work, RuntimeDeliveryWorkClaim claim) =>
        work.Source == claim.Source && work.WorkId == claim.WorkId && work.Kind == claim.WorkKind && work.Role == claim.Role &&
        row.SourceHeaderRevision == claim.SourceRevision && row.SourceHeaderFingerprint == claim.HeaderFingerprint &&
        row.Revision == claim.Revision && row.FencingToken == claim.FencingToken &&
        row.OwnerId == EfRelationalIdentity.Encode(claim.OwnerId) && row.ClaimedAtUtcTicks == claim.ClaimedAt.UtcTicks &&
        row.VisibleAfterUtcTicks == claim.VisibleAfter.UtcTicks && row.ContentFingerprint == claim.ContentFingerprint &&
        JsonElement.DeepEquals(work.Content, claim.Content);

    private static RuntimeDeliveryWorkClaim Claim(RuntimeDeliveryWorkEntity row, RuntimeDeliveryCanonicalWork work, string owner,
        long revision, long fence, DateTimeOffset claimedAt, DateTimeOffset visibleAfter) =>
        new(work.Source, row.SourceHeaderRevision, row.SourceHeaderFingerprint, work.WorkId, work.Kind, work.Role,
            work.Content, work.Fingerprint, revision, owner, fence, claimedAt, visibleAfter);

    private RuntimeDeliveryCanonicalFamilyReader Reader(string family) => readers.TryGetValue(family, out var reader)
        ? reader : throw new NotSupportedException("The canonical source family is unsupported.");
    private RuntimeDeliveryDiscoveryPartition Current()
    {
        var current = owner.CaptureCurrent() ?? throw new InvalidOperationException("Trusted discovery context is unavailable.");
        if (access.Current.RequireScope() != current.Scope || current.TestScope?.IsExpired(Now()) == true)
            throw new InvalidOperationException("Trusted discovery context is unavailable.");
        return current;
    }
    private void RequireCurrent(RuntimeDeliveryDiscoveryPartition expected)
    {
        if (!Current().Matches(expected)) throw new InvalidOperationException("The canonical source differs from current authorized discovery context.");
    }
    private DateTimeOffset Now()
    {
        var now = clock.GetUtcNow();
        if (now == default) throw new InvalidOperationException("A current finite observation time is required.");
        return now;
    }
    private static DateTimeOffset Deadline(DateTimeOffset now, TimeSpan visibility)
    {
        try { return now.Add(visibility); }
        catch (ArgumentOutOfRangeException) { throw new ArgumentOutOfRangeException(nameof(visibility), "The claim deadline exceeds the representable range."); }
    }

    private async ValueTask TestLedgerAsync(RuntimeDeliveryDiscoveryPartition partition, bool touch, CancellationToken cancellationToken)
    {
        if (partition.TestScope is not { } test) return;
        var scope = partition.Scope.Value; var id = WorkflowTestScopeEfSupport.Id(scope, test.ScopeId);
        var row = await context.WorkflowTestScopes.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        WorkflowTestScopeAdmission.EnsureOpen(row is null ? null : WorkflowTestScopeEfSupport.Read(row, scope, test.ScopeId), test, Now());
        if (!touch) return;
        var revision = checked(row!.Revision + 1);
        var changed = await context.WorkflowTestScopes.Where(item => item.Id == row.Id && item.Revision == row.Revision &&
            item.State == (int)WorkflowTestScopeState.Open && item.ExpiresAtUtcTicks == test.ExpiresAt.UtcTicks && item.ContentJson == row.ContentJson)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Revision, revision), cancellationToken);
        if (changed != 1) throw new DbUpdateConcurrencyException("The finite test context changed.");
    }

    // Private control flow only: no public/provider contribution can supply arbitrary mutation callbacks here.
    private async ValueTask<T> OwnedAsync<T>(Func<ValueTask<T>> action, CancellationToken cancellationToken, bool writes = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context is not RuntimeSqliteDbContext || context.Database.ProviderName != EfProviderNames.Sqlite)
            throw new NotSupportedException("Only the SQLite mechanics slice is implemented; other provider mappings are unqualified.");
        if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null ||
            context.Database.GetEnlistedTransaction() is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("The source operation requires a clean, exclusively owned committed boundary.");
        if (writes) _ = RuntimeDeliverySourceWriteVersion.For(context);
        var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var commitAttempted = false; Exception? primary = null;
        try
        {
            var result = await action(); cancellationToken.ThrowIfCancellationRequested();
            commitAttempted = true;
            try { await transaction.CommitAsync(cancellationToken); }
            catch (Exception error) { throw new EfCommitOutcomeUnknownException("The source operation commit outcome is unknown; reconcile canonical state before retrying.", error); }
            return result;
        }
        catch (Exception error)
        {
            primary = error;
            if (!commitAttempted) try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }
        finally
        {
            try { await transaction.DisposeAsync(); } catch when (primary is not null) { }
        }
    }
}
