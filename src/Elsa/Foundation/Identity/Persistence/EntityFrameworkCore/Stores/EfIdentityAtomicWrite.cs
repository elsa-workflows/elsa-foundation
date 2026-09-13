using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>Identity-owned transactional mutation identity used for durable acknowledgement replay.</summary>
public sealed record EfIdentityAtomicMutation
{
    private EfIdentityAtomicMutation(string operationName, string requestFingerprint, string? tenantId)
    {
        OperationName = operationName;
        RequestFingerprint = requestFingerprint;
        TenantId = tenantId;
        OperationId = IdentityEntityFrameworkKey.FramedRecordId(operationName, requestFingerprint);
        MutationReceiptId = IdentityEntityFrameworkKey.FramedRecordId("mutation-receipt", OperationId);
    }

    public string OperationName { get; }
    public string OperationId { get; }
    public string RequestFingerprint { get; }
    public string? TenantId { get; }
    public string MutationReceiptId { get; }

    public static EfIdentityAtomicMutation Create(string operationName, string requestFingerprint, string? tenantId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        return new EfIdentityAtomicMutation(operationName, requestFingerprint, tenantId);
    }
}

/// <summary>
/// Commits an Identity mutation and its receipt in one EF transaction. A repeated operation identity
/// returns the committed outcome without staging domain writes a second time. Expired receipts are
/// reclaimed with a CAS and cleanup is bounded so receipt volume cannot grow without limit.
/// </summary>
public sealed class EfIdentityAtomicWrite
{
    private const int CleanupAttemptInterval = 32;
    private const int CleanupBatchSize = 64;
    private const int MaximumReclaimAttempts = 3;
    private static readonly TimeSpan DefaultReconciliationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultReceiptLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    private readonly IdentityIamDbContext context;
    private readonly TimeProvider clock;
    private readonly TimeSpan reconciliationTimeout;
    private readonly TimeSpan receiptLifetime;
    private readonly EfIdentityMutationReceiptCleanupCoordinator cleanup;
    private readonly IPersistenceAccessContextAccessor? accessContextAccessor;

    public EfIdentityAtomicWrite(
        IdentityIamDbContext context,
        TimeProvider? timeProvider = null,
        TimeSpan? reconciliationTimeout = null,
        TimeSpan? receiptLifetime = null,
        EfIdentityMutationReceiptCleanupCoordinator? cleanupCoordinator = null,
        IPersistenceAccessContextAccessor? accessContextAccessor = null)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        clock = timeProvider ?? TimeProvider.System;
        this.reconciliationTimeout = reconciliationTimeout ?? DefaultReconciliationTimeout;
        this.receiptLifetime = receiptLifetime ?? DefaultReceiptLifetime;
        cleanup = cleanupCoordinator ?? new EfIdentityMutationReceiptCleanupCoordinator();
        this.accessContextAccessor = accessContextAccessor;
        if (this.reconciliationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(reconciliationTimeout));
        if (this.receiptLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(receiptLifetime));
    }

    public async ValueTask<EfIdentityWriteResult> ExecuteAsync(
        EfIdentityAtomicMutation mutation,
        Func<CancellationToken, Task<EfIdentityWriteResult>> stageAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(stageAsync);
        if (mutation.TenantId is { } tenantId && accessContextAccessor is not null)
            EfIdentityStoreSupport.EnsureTenant(accessContextAccessor, tenantId);
        context.EnsureProviderBinding();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await CleanupIfDueAsync(cancellationToken);
            if (await ReadActiveReceiptAsync(mutation, cancellationToken) is { } replay)
                return replay;
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (IdentityEntityFrameworkPersistenceException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (IdentityEntityFrameworkUncertainCommitException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (InvalidOperationException)
        {
            // Wrong-provider binding and request-fingerprint mismatches are caller/configuration
            // errors, not provider failures. Preserve their useful contract exception types.
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception)
        {
            context.ChangeTracker.Clear();
            throw EfIdentityStoreSupport.Failure("Unable to prepare the Identity atomic mutation.", exception);
        }

        for (var attempt = 0; attempt < EfIdentityStoreSupport.MaximumWriteAttempts; attempt++)
        {
            var ownsTransaction = context.Database.CurrentTransaction is null;
            // Rollback/reconciliation must be able to dispose an owned transaction before issuing
            // receipt reads. Keep ownership explicit here so RollbackAsync does not double-dispose
            // an `await using` local while the provider still exposes it as CurrentTransaction.
            IDbContextTransaction? transaction = null;
            var stageStarted = false;
            var commitAttempted = false;
            try
            {
                if (ownsTransaction)
                    transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                stageStarted = true;
                var result = await stageAsync(cancellationToken);
                if (!result.Succeeded)
                {
                    var rollbackException = await RollbackAsync(transaction);
                    context.ChangeTracker.Clear();
                    if (rollbackException is not null)
                        throw new IdentityEntityFrameworkPersistenceException("Unable to roll back the unsuccessful Identity mutation.", rollbackException);
                    return result;
                }

                var now = clock.GetUtcNow();
                context.MutationReceipts.Add(new MutationReceiptEntity
                {
                    Id = mutation.MutationReceiptId,
                    MutationReceiptId = mutation.MutationReceiptId,
                    OperationId = mutation.OperationId,
                    RequestFingerprint = mutation.RequestFingerprint,
                    Status = (int)result.Status,
                    Version = result.Version,
                    Message = result.Message,
                    AuthoritativeId = result.Id,
                    FailedUnitId = result.FailedUnitId,
                    CreatedAt = now,
                    ExpiresAt = now.Add(receiptLifetime),
                    Revision = 1
                });
                await context.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    try
                    {
                        // Once commit begins, cancellation cannot tell us whether the provider
                        // committed. Always complete the provider commit and reconcile failures.
                        commitAttempted = true;
                        await transaction.CommitAsync(CancellationToken.None);
                        await transaction.DisposeAsync();
                        transaction = null;
                    }
                    catch (Exception exception)
                    {
                        var rollbackException = await RollbackAsync(transaction);
                        return await ReconcileOrThrowAsync(mutation, exception, rollbackException, CancellationToken.None);
                    }
                }
                context.ChangeTracker.Clear();
                return result;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                var rollbackException = await RollbackAsync(transaction);
                if (!stageStarted)
                {
                    context.ChangeTracker.Clear();
                    Exception beginFailure = rollbackException is null ? exception : new AggregateException(exception, rollbackException);
                    throw EfIdentityStoreSupport.Failure($"Unable to begin the Identity atomic mutation '{mutation.OperationId}'.", beginFailure);
                }
                if (!ownsTransaction)
                {
                    context.ChangeTracker.Clear();
                    if (EfIdentityStoreSupport.IsMutationReceiptConflict(exception))
                        throw EfIdentityStoreSupport.Failure($"Identity mutation '{mutation.OperationId}' encountered a duplicate receipt inside an ambient transaction; the caller-owned transaction must be rolled back before replay can be attempted.", exception);
                    return ConflictResult(mutation, exception);
                }
                return await ReconcileOrConflictAsync(mutation, exception, rollbackException, rollbackException is null ? cancellationToken : CancellationToken.None);
            }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
            {
                var rollbackException = await RollbackAsync(transaction);
                if (!stageStarted)
                {
                    context.ChangeTracker.Clear();
                    Exception beginFailure = rollbackException is null ? exception : new AggregateException(exception, rollbackException);
                    throw EfIdentityStoreSupport.Failure($"Unable to begin the Identity atomic mutation '{mutation.OperationId}'.", beginFailure);
                }
                if (!ownsTransaction)
                {
                    context.ChangeTracker.Clear();
                    if (EfIdentityStoreSupport.IsMutationReceiptConflict(exception))
                        throw EfIdentityStoreSupport.Failure($"Identity mutation '{mutation.OperationId}' encountered a duplicate receipt inside an ambient transaction; the caller-owned transaction must be rolled back before replay can be attempted.", exception);
                    return ConflictResult(mutation, exception);
                }
                return await ReconcileOrConflictAsync(mutation, exception, rollbackException, rollbackException is null ? cancellationToken : CancellationToken.None);
            }
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                var rollbackException = await RollbackAsync(transaction);
                context.ChangeTracker.Clear();
                if (!ownsTransaction)
                {
                    var ambientFailure = rollbackException is null ? exception : new AggregateException(exception, rollbackException);
                    throw EfIdentityStoreSupport.Failure($"Identity mutation '{mutation.OperationId}' encountered a transient provider conflict inside an ambient transaction and cannot safely retry it.", ambientFailure);
                }
                if (rollbackException is not null)
                    return await ReconcileOrThrowAsync(mutation, exception, rollbackException, CancellationToken.None);
                if (attempt + 1 < EfIdentityStoreSupport.MaximumWriteAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)), cancellationToken);
                    continue;
                }

                var failure = rollbackException is null ? exception : new AggregateException(exception, rollbackException);
                throw EfIdentityStoreSupport.Failure($"Identity mutation '{mutation.OperationId}' exceeded the {EfIdentityStoreSupport.MaximumWriteAttempts}-attempt transient conflict limit.", failure);
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                var rollbackException = await RollbackAsync(transaction);
                context.ChangeTracker.Clear();
                if (rollbackException is not null)
                    throw new IdentityEntityFrameworkPersistenceException("Unable to roll back the unsuccessful Identity mutation.", new AggregateException(exception, rollbackException));
                throw;
            }
            catch (IdentityEntityFrameworkAdmissionException exception)
            {
                var rollbackException = await RollbackAsync(transaction);
                context.ChangeTracker.Clear();
                if (rollbackException is not null)
                    throw new IdentityEntityFrameworkPersistenceException("Unable to roll back the rejected Identity mutation.", new AggregateException(exception, rollbackException));
                throw;
            }
            catch (IdentityEntityFrameworkPersistenceException exception)
            {
                var rollbackException = await RollbackAsync(transaction);
                context.ChangeTracker.Clear();
                if (rollbackException is not null)
                    throw new IdentityEntityFrameworkPersistenceException("Unable to roll back the failed Identity mutation.", new AggregateException(exception, rollbackException));
                throw;
            }
            catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException && exception is not IdentityEntityFrameworkUncertainCommitException)
            {
                var rollbackException = await RollbackAsync(transaction);
                if (!stageStarted)
                {
                    context.ChangeTracker.Clear();
                    var beginFailure = rollbackException is null ? exception : new AggregateException(exception, rollbackException);
                    throw EfIdentityStoreSupport.Failure($"Unable to begin the Identity atomic mutation '{mutation.OperationId}'.", beginFailure);
                }
                if (!ownsTransaction)
                {
                    context.ChangeTracker.Clear();
                    var ambientFailure = rollbackException is null ? exception : new AggregateException(exception, rollbackException);
                    throw EfIdentityStoreSupport.Failure($"Identity mutation '{mutation.OperationId}' failed inside an ambient transaction whose commit is owned by the caller.", ambientFailure);
                }
                if (!commitAttempted && rollbackException is null)
                {
                    context.ChangeTracker.Clear();
                    throw EfIdentityStoreSupport.Failure($"Identity mutation '{mutation.OperationId}' failed before commit.", exception);
                }
                return await ReconcileOrThrowAsync(mutation, exception, rollbackException, CancellationToken.None);
            }
        }

        throw new InvalidOperationException("Identity mutation execution did not produce a result.");
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            context.EnsureProviderBinding();
            var now = clock.GetUtcNow();
            var rows = await EfIdentityStoreSupport.ReadAsync(
                context,
                "Reading expired Identity mutation receipts",
                () => context.MutationReceipts
                    .Where(x => x.ExpiresAt <= now)
                    .OrderBy(x => x.ExpiresAt)
                    .Take(CleanupBatchSize)
                    .ToListAsync(cancellationToken));
            foreach (var row in rows)
            {
                EnsureReceiptSelfIdentity(row);
                context.MutationReceipts.Remove(row);
            }
            if (rows.Count != 0)
            {
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Another cleanup/reclaim worker won the delete race. Treat the batch as
                    // already reconciled; the next bounded cleanup window will remove leftovers.
                    context.ChangeTracker.Clear();
                    return 0;
                }
                catch (OperationCanceledException)
                {
                    context.ChangeTracker.Clear();
                    throw;
                }
                catch (IdentityEntityFrameworkPersistenceException)
                {
                    context.ChangeTracker.Clear();
                    throw;
                }
                catch (Exception exception)
                {
                    context.ChangeTracker.Clear();
                    throw EfIdentityStoreSupport.Failure("Unable to delete expired Identity mutation receipts.", exception);
                }
            }
            context.ChangeTracker.Clear();
            return rows.Count;
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (IdentityEntityFrameworkPersistenceException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (InvalidOperationException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception)
        {
            context.ChangeTracker.Clear();
            throw EfIdentityStoreSupport.Failure("Unable to clean up expired Identity mutation receipts.", exception);
        }
    }

    private async Task<EfIdentityWriteResult?> ReadActiveReceiptAsync(EfIdentityAtomicMutation mutation, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumReclaimAttempts; attempt++)
        {
            var row = await EfIdentityStoreSupport.ReadAsync(
                context,
                "Reading the Identity mutation receipt",
                () => context.MutationReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mutation.MutationReceiptId, cancellationToken));
            if (row is null)
                return null;
            EnsureReceiptIdentity(row, mutation);
            if (row.ExpiresAt > clock.GetUtcNow())
                return ToResult(row);

            var expired = await EfIdentityStoreSupport.ReadAsync(
                context,
                "Re-reading the expired Identity mutation receipt",
                () => context.MutationReceipts.SingleOrDefaultAsync(x => x.Id == row.Id, cancellationToken));
            if (expired is null)
                continue;
            EnsureReceiptIdentity(expired, mutation);
            context.MutationReceipts.Remove(expired);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
                continue;
            }
            catch (DbUpdateConcurrencyException)
            {
                context.ChangeTracker.Clear();
            }
            catch (OperationCanceledException)
            {
                context.ChangeTracker.Clear();
                throw;
            }
            catch (IdentityEntityFrameworkPersistenceException)
            {
                context.ChangeTracker.Clear();
                throw;
            }
            catch (Exception exception)
            {
                context.ChangeTracker.Clear();
                throw EfIdentityStoreSupport.Failure("Unable to reclaim the expired Identity mutation receipt.", exception);
            }
        }
        throw new IdentityEntityFrameworkUncertainCommitException($"Expired Identity mutation receipt '{mutation.MutationReceiptId}' kept changing during bounded reclamation.", new InvalidOperationException("Receipt reclaim limit exceeded."));
    }

    private static void EnsureReceiptIdentity(MutationReceiptEntity row, EfIdentityAtomicMutation mutation)
    {
        EnsureReceiptSelfIdentity(row);
        if (!string.Equals(row.Id, mutation.MutationReceiptId, StringComparison.Ordinal) ||
            !string.Equals(row.OperationId, mutation.OperationId, StringComparison.Ordinal) ||
            !string.Equals(row.RequestFingerprint, mutation.RequestFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Identity mutation receipt '{mutation.MutationReceiptId}' belongs to a different mutation identity.");
        }
    }

    private static void EnsureReceiptSelfIdentity(MutationReceiptEntity row)
    {
        if (!string.Equals(row.Id, row.MutationReceiptId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(row.OperationId) ||
            string.IsNullOrWhiteSpace(row.RequestFingerprint))
        {
            throw new InvalidOperationException($"Identity mutation receipt '{row.Id}' has a corrupt persisted identity.");
        }
    }

    private async Task CleanupIfDueAsync(CancellationToken cancellationToken)
    {
        // Cleanup cadence is shared process-wide, but must be independent for separate
        // Identity databases using the same provider. Hash provider + connection identity so
        // credentials never become part of the in-memory scheduler key.
        var database = context.Database.GetDbConnection();
        var scope = IdentityEntityFrameworkKey.FramedRecordId(
            context.Database.ProviderName ?? "identity",
            database.ConnectionString);
        await cleanup.RunIfDueAsync(scope, clock.GetUtcNow(), CleanupInterval, CleanupAttemptInterval, CleanupExpiredAsync, cancellationToken);
    }

    private async Task<EfIdentityWriteResult> ReconcileOrConflictAsync(EfIdentityAtomicMutation mutation, Exception exception, Exception? rollbackException, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        // Unique/CAS failures happen during SaveChanges, before the explicit commit is attempted.
        // A successful rollback proves that no receipt could have become durable, so classify the
        // domain conflict immediately instead of spending the reconciliation window polling. A
        // duplicate receipt is the exception: another context may have committed this operation,
        // so its typed result must be reread and replayed.
        var receiptConflict = exception is DbUpdateException update && EfIdentityStoreSupport.IsMutationReceiptConflict(update);
        var domainReservationConflict = exception is DbUpdateException reservationUpdate &&
                                        EfIdentityStoreSupport.UniqueConflictUnit(reservationUpdate) is not null;
        if (rollbackException is null && !receiptConflict)
        {
            if (domainReservationConflict)
                return ConflictResult(mutation, exception);

            // A competing execution of the same request can lose on the authority/relationship
            // row before its receipt insert is attempted. Relational conflict detection waits for
            // the winning transaction, so one post-rollback read is sufficient to replay that
            // exact request without delaying unrelated stale-CAS conflicts.
            var concurrentReceipt = await ReadActiveReceiptAsync(mutation, cancellationToken);
            return concurrentReceipt ?? ConflictResult(mutation, exception);
        }

        EfIdentityWriteResult? receipt;
        IdentityEntityFrameworkUncertainCommitException? reconciliationFailure = null;
        try
        {
            receipt = await TryReadWithinWindowAsync(mutation, cancellationToken);
        }
        catch (IdentityEntityFrameworkUncertainCommitException reconciliationException)
        {
            receipt = null;
            reconciliationFailure = reconciliationException;
        }
        if (reconciliationFailure is not null)
        {
            var details = rollbackException is null
                ? new AggregateException(exception, reconciliationFailure)
                : new AggregateException(exception, rollbackException, reconciliationFailure);
            throw new IdentityEntityFrameworkUncertainCommitException($"Identity mutation '{mutation.OperationId}' could not safely classify its failed transaction because receipt reconciliation also failed.", details);
        }
        if (receiptConflict && receipt is not null)
            return receipt;
        if (receiptConflict)
        {
            var details = rollbackException is null
                ? new AggregateException(exception, new InvalidOperationException("The concurrent mutation receipt was not visible within the bounded reconciliation window."))
                : new AggregateException(exception, rollbackException, new InvalidOperationException("The concurrent mutation receipt was not visible within the bounded reconciliation window."));
            throw new IdentityEntityFrameworkUncertainCommitException($"Identity mutation '{mutation.OperationId}' could not replay the concurrent mutation receipt within the bounded reconciliation window.", details);
        }
        if (rollbackException is not null)
        {
            var details = receipt is null ? new AggregateException(exception, rollbackException) : new AggregateException(exception, rollbackException, new InvalidOperationException("A durable receipt was observed while rolling back the failed Identity mutation."));
            throw new IdentityEntityFrameworkUncertainCommitException($"Identity mutation '{mutation.OperationId}' could not safely classify its failed transaction.", details);
        }
        if (receipt is not null)
            return receipt;
        var failure = rollbackException is null ? exception : new AggregateException(exception, rollbackException);
        if (exception is DbUpdateException or DbUpdateConcurrencyException)
            return ConflictResult(mutation, exception);
        throw new IdentityEntityFrameworkPersistenceException($"Identity mutation '{mutation.OperationId}' conflicted without a durable receipt.", failure);
    }

    private static EfIdentityWriteResult ConflictResult(EfIdentityAtomicMutation mutation, Exception exception) =>
        new(
            EfIdentityWriteStatus.Conflict,
            null,
            "Identity mutation conflicted with an existing durable value.",
            FailedUnitId: exception is DbUpdateException update
                ? EfIdentityStoreSupport.UniqueConflictUnit(update) ?? mutation.OperationId
                : mutation.OperationId);

    private async Task<EfIdentityWriteResult> ReconcileOrThrowAsync(EfIdentityAtomicMutation mutation, Exception exception, Exception? rollbackException, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        EfIdentityWriteResult? receipt;
        IdentityEntityFrameworkUncertainCommitException? reconciliationFailure = null;
        try
        {
            receipt = await TryReadWithinWindowAsync(mutation, cancellationToken);
        }
        catch (IdentityEntityFrameworkUncertainCommitException reconciliationException)
        {
            receipt = null;
            reconciliationFailure = reconciliationException;
        }
        if (reconciliationFailure is not null)
        {
            var details = rollbackException is null
                ? new AggregateException(exception, reconciliationFailure)
                : new AggregateException(exception, rollbackException, reconciliationFailure);
            throw new IdentityEntityFrameworkUncertainCommitException($"Identity mutation '{mutation.OperationId}' has an uncertain commit outcome because receipt reconciliation failed.", details);
        }
        if (rollbackException is not null && receipt is not null)
            throw new IdentityEntityFrameworkUncertainCommitException($"Identity mutation '{mutation.OperationId}' had rollback errors despite a durable receipt.", new AggregateException(exception, rollbackException));
        if (receipt is not null)
            return receipt;
        var failure = rollbackException is null ? exception : new AggregateException(exception, rollbackException);
        throw new IdentityEntityFrameworkUncertainCommitException($"Identity mutation '{mutation.OperationId}' has an uncertain commit outcome because its receipt was not observed within the bounded reconciliation window.", failure);
    }

    private async Task<EfIdentityWriteResult?> TryReadWithinWindowAsync(EfIdentityAtomicMutation mutation, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(reconciliationTimeout);
        var delay = TimeSpan.FromMilliseconds(25);
        Exception? lastReadFailure = null;
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var receipt = await ReadActiveReceiptAsync(mutation, timeout.Token);
                if (receipt is not null)
                    return receipt;
            }
            catch (IdentityEntityFrameworkUncertainCommitException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastReadFailure = exception;
                context.ChangeTracker.Clear();
            }
            catch (IdentityEntityFrameworkPersistenceException exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A failed reconciliation query can be a transient provider outage. Keep polling
                // until the bounded window expires, while preserving the last provider error for
                // the final uncertain-commit exception.
                lastReadFailure = exception;
                context.ChangeTracker.Clear();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            { await Task.Delay(delay, timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { break; }
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 250));
        }
        if (lastReadFailure is not null)
            throw new IdentityEntityFrameworkUncertainCommitException($"Identity mutation '{mutation.OperationId}' receipt reconciliation did not complete within the bounded window.", lastReadFailure);
        return null;
    }

    private static async Task<Exception?> RollbackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is null)
            return null;

        Exception? rollbackException = null;
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            rollbackException = exception;
        }

        try
        {
            await transaction.DisposeAsync();
        }
        catch (Exception exception)
        {
            rollbackException = rollbackException is null ? exception : new AggregateException(rollbackException, exception);
        }

        return rollbackException;
    }

    private static EfIdentityWriteResult ToResult(MutationReceiptEntity row)
    {
        if (!Enum.IsDefined(typeof(EfIdentityWriteStatus), row.Status))
            throw new IdentityEntityFrameworkPersistenceException(
                "Identity mutation receipt contains an unknown write status.",
                new InvalidDataException($"Unknown Identity write status value '{row.Status}'."));

        var status = (EfIdentityWriteStatus)row.Status;
        if (status is (EfIdentityWriteStatus.Inserted or EfIdentityWriteStatus.Updated or EfIdentityWriteStatus.Deleted) && row.Version is not > 0)
            throw new IdentityEntityFrameworkPersistenceException(
                "Identity mutation receipt contains an invalid successful write version.",
                new InvalidDataException($"Successful Identity write status '{status}' requires a positive version."));

        return new EfIdentityWriteResult(status, row.Version, row.Message, row.AuthoritativeId, row.FailedUnitId);
    }

}

/// <summary>Coordinates bounded mutation-receipt cleanup across EF scopes using one process-wide scheduler.</summary>
public sealed class EfIdentityMutationReceiptCleanupCoordinator
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CleanupWindow> windows = new(StringComparer.Ordinal);

    public async Task RunIfDueAsync(
        string scopeKey,
        DateTimeOffset now,
        TimeSpan interval,
        int attemptInterval,
        Func<CancellationToken, Task> cleanupAsync,
        CancellationToken cancellationToken)
    {
        var window = windows.GetOrAdd(scopeKey, static _ => new CleanupWindow());
        await window.Gate.WaitAsync(cancellationToken);
        try
        {
            window.Attempts++;
            if (window.Attempts < attemptInterval && now < window.NextRun)
                return;
            try
            {
                await cleanupAsync(cancellationToken);
            }
            catch
            {
                // Do not spin cleanup on every mutation after a provider outage. Preserve the
                // attempt count for bounded backoff and permit a retry at the next window.
                window.NextRun = now.Add(interval);
                throw;
            }
            window.Attempts = 0;
            window.NextRun = now.Add(interval);
        }
        finally
        {
            window.Gate.Release();
        }
    }

    private sealed class CleanupWindow
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int Attempts;
        public DateTimeOffset NextRun = DateTimeOffset.MinValue;
    }
}
