using Elsa.Persistence.EntityFramework;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

internal sealed partial class EfRuleRunStore
{
    public async ValueTask<RuleWorkClaimResult> ClaimWorkAsync(RuleClaimRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var pins = request.SupportedImplementations.Select(RuleRecordCodec.PinHash).Distinct(StringComparer.Ordinal).ToArray();
        if (pins.Length == 0 || request.InvocationSchemas.Count == 0 || request.ReceiptSchemas.Count == 0 || request.StateSchemas.Count == 0)
            return new(RuleWorkClaimDisposition.NoEligibleWork, null);

        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(cancellationToken);
                var now = await db.ReadUtcTicksAsync(cancellationToken);
                string? after = null;
                var unsupported = false;
                while (true)
                {
                    // Page addresses, not multi-megabyte payloads. A schema-incompatible row cannot hide later work.
                    var query = db.Runs.AsNoTracking().Where(row => row.Status == (int)RuleRunStatus.Running
                        && row.Admitted && pins.Contains(row.PinHash64)
                        && (row.WorkExpiresAtUtcTicks == null || row.WorkExpiresAtUtcTicks <= now));
                    if (after is not null) query = query.Where(row => string.Compare(row.RunKey, after) > 0);
                    var keys = await query.OrderBy(row => row.RunKey).Select(row => row.RunKey)
                        .Take(RuleLimits.MaxPageEntries).ToArrayAsync(cancellationToken);
                    if (keys.Length == 0) return new(unsupported ? RuleWorkClaimDisposition.Unavailable : RuleWorkClaimDisposition.NoEligibleWork, null);
                    foreach (var key in keys)
                    {
                        db.ChangeTracker.Clear();
                        var row = await db.Runs.SingleOrDefaultAsync(row => row.RunKey == key, cancellationToken);
                        if (row is null || row.Status != (int)RuleRunStatus.Running || !row.Admitted
                            || row.WorkExpiresAtUtcTicks > now || !pins.Contains(row.PinHash64, StringComparer.Ordinal)) continue;
                        RuleRowCodec.State state;
                        try { state = RuleRowCodec.Read(row); }
                        catch (NotSupportedException)
                        {
                            // A newer retained envelope/provider schema must not hide compatible rows behind it.
                            unsupported = true;
                            continue;
                        }
                        if (!Supports(request, state.Invocation)) continue;
                        row.WorkOwner = request.Owner;
                        row.WorkFence = checked(row.WorkFence + 1);
                        row.WorkExpiresAtUtcTicks = NewExpiry(now);
                        if (!await SaveAsync(db, cancellationToken, new(now, row.WorkExpiresAtUtcTicks.Value)))
                            return new(RuleWorkClaimDisposition.Unavailable, null);
                        return new(RuleWorkClaimDisposition.Claimed, new(WorkLease(row, state.Invocation.Identity), row.ModelRevision));
                    }
                    after = keys[^1];
                }
            }
            catch (Exception error) when (IsWriteConflict(error)) { }
            catch (Exception error) when (IsStoreUnavailable(error)) { return new(RuleWorkClaimDisposition.Unavailable, null); }
        }
        return new(RuleWorkClaimDisposition.Unavailable, null);
    }

    public ValueTask<RuleLeaseConfirmation> RenewAsync(RuleWorkLease lease, CancellationToken cancellationToken = default) =>
        ChangeLeaseAsync(lease, release: false, cancellationToken);

    public ValueTask<RuleLeaseConfirmation> ReleaseAsync(RuleWorkLease lease, CancellationToken cancellationToken = default) =>
        ChangeLeaseAsync(lease, release: true, cancellationToken);

    private async ValueTask<RuleLeaseConfirmation> ChangeLeaseAsync(RuleWorkLease lease, bool release, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RuleRecordCodec.ScopeBytes(lease.Identity);
        var key = RuleRecordCodec.Hash(scope);
        var identity = Convert.ToBase64String(scope);
        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(cancellationToken);
                var row = await db.Runs.SingleOrDefaultAsync(row => row.RunKey == key, cancellationToken);
                // Check exact identity before parsing or exposing any other invocation's state.
                if (row is null) return new(RuleLeaseDisposition.LeaseLost, null);
                if (row.IdentityBase64 != identity) return new(RuleLeaseDisposition.Conflict, null);
                if (row.Fingerprint64 != lease.Fingerprint.Sha256) return new(RuleLeaseDisposition.LeaseLost, null);
                _ = RuleRowCodec.Read(row);
                var now = await db.ReadUtcTicksAsync(cancellationToken);
                if (!OwnsWork(row, lease, now)) return new(RuleLeaseDisposition.LeaseLost, null);
                var previousExpiry = row.WorkExpiresAtUtcTicks!.Value;
                if (release)
                {
                    row.WorkOwner = null;
                    row.WorkExpiresAtUtcTicks = null;
                    row.WorkFence = checked(row.WorkFence + 1);
                }
                else
                {
                    row.WorkExpiresAtUtcTicks = Math.Max(previousExpiry, NewExpiry(now));
                    // Even an unchanged expiry must compare the row token and pass the schema write gate.
                    db.Entry(row).Property(candidate => candidate.WorkExpiresAtUtcTicks).IsModified = true;
                }
                // Authorize against the persisted old expiry, never the extended expiry or caller's stale timestamp.
                if (!await SaveAsync(db, cancellationToken, new(now, previousExpiry)))
                    return new(RuleLeaseDisposition.LeaseLost, null);
                return new(release ? RuleLeaseDisposition.Released : RuleLeaseDisposition.Renewed,
                    release ? null : WorkLease(row, lease.Identity));
            }
            catch (Exception error) when (IsWriteConflict(error)) { }
            catch (Exception error) when (IsStoreUnavailable(error)) { return new(RuleLeaseDisposition.Unavailable, null); }
        }
        return new(RuleLeaseDisposition.Unavailable, null);
    }

    private static bool Supports(RuleClaimRequest request, RuleInvocationEnvelope invocation) =>
        request.InvocationSchemas.Contains(invocation.InvocationSchemaVersion)
        && request.ReceiptSchemas.Contains(invocation.ExpectedImplementation.ReceiptSchemaVersion)
        && request.StateSchemas.Contains(invocation.ExpectedImplementation.StateSchemaVersion);

    private static bool OwnsWork(RuleRunRecord row, RuleWorkLease lease, long now) =>
        row.Status == (int)RuleRunStatus.Running && row.Admitted && row.WorkOwner == lease.Owner
        && row.WorkFence == lease.Fence && row.WorkExpiresAtUtcTicks > now;

    private static RuleWorkLease WorkLease(RuleRunRecord row, RuleRunIdentity identity) =>
        new(identity, new(row.Fingerprint64), row.WorkOwner!, row.WorkFence, new(row.WorkExpiresAtUtcTicks!.Value, TimeSpan.Zero));

    private long NewExpiry(long now) => new DateTimeOffset(now, TimeSpan.Zero).Add(_workLeaseDuration).UtcTicks;

    private static TimeSpan ValidateLeaseDuration(TimeSpan duration) => duration > TimeSpan.Zero && duration <= TimeSpan.FromDays(1)
        ? duration : throw new ArgumentOutOfRangeException(nameof(duration), "Lease duration must be positive and at most one day.");

    private static bool IsWriteConflict(Exception error) =>
        EfRelationalExceptionClassifier.IsSaveConflict(error, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey | EfWriteConflict.Transient)
        || EfRelationalExceptionClassifier.IsTransientWriteConflict(error);

    private static bool IsStoreUnavailable(Exception error) =>
        EfRelationalExceptionClassifier.IsProviderFailure(error) || error is NotSupportedException || IsInvalidRecord(error);

    private sealed record LeaseWindow(long SampledAt, long ExpiresAt);
}
