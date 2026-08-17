using Elsa.Foundation.Identity.Persistence.Groundwork.Exceptions;
using Groundwork.Store;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Executes Identity-owned row mutations and their durable receipt in one exact public-v2 unit of
/// work. A retry addresses the same receipt, so acknowledgement loss replays the committed outcome.
/// </summary>
public sealed class GroundworkIdentityAtomicWrite
{
    private const int CleanupAttemptInterval = 32;
    private const int CleanupBatchSize = 64;
    private const int MaxReclaimAttempts = 3;
    private static readonly TimeSpan DefaultReconciliationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultReceiptLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    private readonly GroundworkIdentityRowStore rows;
    private readonly IdentityMutationReceiptCleanupCoordinator cleanup;
    private readonly TimeProvider clock;
    private readonly TimeSpan reconciliationTimeout;
    private readonly TimeSpan receiptLifetime;

    public GroundworkIdentityAtomicWrite(
        GroundworkIdentityRowStore rows,
        TimeProvider? timeProvider = null,
        TimeSpan? reconciliationTimeout = null,
        TimeSpan? receiptLifetime = null)
        : this(rows, new IdentityMutationReceiptCleanupCoordinator(), timeProvider, reconciliationTimeout, receiptLifetime)
    {
    }

    internal GroundworkIdentityAtomicWrite(
        GroundworkIdentityRowStore rows,
        IdentityMutationReceiptCleanupCoordinator cleanupCoordinator,
        TimeProvider? timeProvider = null,
        TimeSpan? reconciliationTimeout = null,
        TimeSpan? receiptLifetime = null)
    {
        this.rows = rows ?? throw new ArgumentNullException(nameof(rows));
        cleanup = cleanupCoordinator ?? throw new ArgumentNullException(nameof(cleanupCoordinator));
        clock = timeProvider ?? TimeProvider.System;
        this.reconciliationTimeout = reconciliationTimeout ?? DefaultReconciliationTimeout;
        this.receiptLifetime = receiptLifetime ?? DefaultReceiptLifetime;
        if (this.reconciliationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(reconciliationTimeout), "The reconciliation timeout must be positive.");
        if (this.receiptLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(receiptLifetime), "The mutation receipt lifetime must be positive.");
    }

    public ValueTask<GroundworkIdentityWriteResult> SaveAsync(
        GroundworkIdentityRowWrite write,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        var mutation = GroundworkIdentityAtomicMutation.Create("save-row", Fingerprint(write), write.UnitId);
        return ExecuteAsync(
            mutation,
            (batch, token) => Task.FromResult(batch.Save(write, token)),
            cancellationToken);
    }

    public async ValueTask<GroundworkIdentityWriteResult> ExecuteAsync(
        GroundworkIdentityAtomicMutation mutation,
        Func<GroundworkIdentityMutationBatch, CancellationToken, Task<GroundworkIdentityWriteResult>> stageAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(stageAsync);
        cancellationToken.ThrowIfCancellationRequested();

        if (await ReadReceiptAsync(mutation, null, runCleanup: true, cancellationToken) is { } replay)
            return replay;

        try
        {
            return await ExecuteAttemptAsync(mutation, stageAsync, cancellationToken);
        }
        catch (BatchWriteException batchFailure)
        {
            if (await ReadReceiptAsync(mutation, batchFailure, runCleanup: false, CancellationToken.None) is { } raced)
                return raced;
            return Failure(batchFailure);
        }
        catch (Exception commitException) when (commitException is not GroundworkIdentityUncertainCommitException)
        {
            if (await ReadReceiptAsync(mutation, commitException, runCleanup: false, CancellationToken.None) is { } reconciled)
                return reconciled;

            ExceptionDispatchInfo.Capture(commitException).Throw();
            throw;
        }
    }

    private async Task<GroundworkIdentityWriteResult> ExecuteAttemptAsync(
        GroundworkIdentityAtomicMutation mutation,
        Func<GroundworkIdentityMutationBatch, CancellationToken, Task<GroundworkIdentityWriteResult>> stageAsync,
        CancellationToken cancellationToken)
    {
        var batch = new GroundworkIdentityMutationBatch(rows);
        var result = await stageAsync(batch, cancellationToken);
        if (!result.Succeeded)
            return result;

        var receipt = batch.Save(ReceiptWrite(mutation, result), cancellationToken);
        if (!receipt.Succeeded)
            throw new InvalidDataException("The exact Identity mutation receipt could not be staged.");

        var allowed = mutation.UnitIds
            .Append(IdentityStorageManifest.IdentityMutationReceiptDocumentKind)
            .ToHashSet(StringComparer.Ordinal);
        var unexpected = batch.BuildMutations().FirstOrDefault(item => !allowed.Contains(item.UnitId));
        if (unexpected is not null)
            throw new InvalidOperationException($"Identity mutation '{mutation.OperationId}' staged undeclared unit '{unexpected.UnitId}'.");

        batch.Commit(cancellationToken);
        return result;
    }

    private async Task CleanupIfDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await cleanup.RunIfDueAsync(
            rows.AccessIdentity,
            now,
            CleanupInterval,
            CleanupAttemptInterval,
            token => CleanupExpiredAsync(now, token),
            cancellationToken);
    }

    private async Task CleanupExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expired = await BoundProviderCallAsync(
                () => rows.Query(
                    IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
                    new GroundworkIdentityRowQuery(
                        IdentityStorageManifest.MutationReceiptExpiresAtField,
                        GroundworkIdentityRowComparison.LessThanOrEqual,
                        now,
                        IdentityStorageManifest.MutationReceiptExpiresAtField,
                        Take: CleanupBatchSize,
                        IncludeVersions: true,
                        ExpectedIndex: IdentityV2StorageManifest.MutationReceiptByExpiryIndex),
                    cancellationToken),
                cancellationToken);
        foreach (var row in expired.Take(CleanupBatchSize))
        {
            var receipt = GroundworkIdentityDocumentRows.Deserialize<IdentityMutationReceipt>(row);
            if (!StringComparer.Ordinal.Equals(receipt.MutationReceiptId, row.Id))
                throw new InvalidDataException("The expired mutation receipt query returned an invalid receipt row.");
            if (receipt.ExpiresAt > now)
                continue;

            var deleted = rows.Delete(new GroundworkIdentityRowDelete(
                IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
                row.Id,
                GroundworkIdentityRowWriteCondition.IfVersion(row.Version)), cancellationToken);
            if (deleted.Status is WriteOutcomeStatus.Deleted or WriteOutcomeStatus.NotFound or WriteOutcomeStatus.ConcurrencyConflict)
                continue;
            throw new InvalidOperationException($"Groundwork rejected expired mutation receipt cleanup with status '{deleted.Status}'.");
        }
    }

    private async Task<GroundworkIdentityWriteResult?> ReadReceiptAsync(
        GroundworkIdentityAtomicMutation mutation,
        Exception? commitException,
        bool runCleanup,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(reconciliationTimeout);
        var cleanupCompleted = !runCleanup;
        try
        {
            if (runCleanup)
                await CleanupIfDueAsync(timeout.Token);
            cleanupCompleted = true;

            for (var attempt = 0; attempt < MaxReclaimAttempts; attempt++)
            {
                var row = await BoundProviderCallAsync(
                        () => rows.Read(
                            IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
                            mutation.MutationReceiptId,
                            timeout.Token),
                        timeout.Token);
                if (row is null)
                    return null;

                var receipt = DeserializeReceipt(row, mutation, commitException);
                if (!Matches(receipt, mutation))
                    throw Uncertain(mutation, commitException, new InvalidDataException("The mutation receipt did not match the attempted Identity mutation."));
                if (receipt.ExpiresAt > clock.GetUtcNow())
                    return receipt.Outcome.ToResult();

                var deleted = rows.Delete(new GroundworkIdentityRowDelete(
                    IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
                    row.Id,
                    GroundworkIdentityRowWriteCondition.IfVersion(row.Version)), timeout.Token);
                if (deleted.Status is WriteOutcomeStatus.Deleted or WriteOutcomeStatus.NotFound)
                    return null;
                if (deleted.Status is not WriteOutcomeStatus.ConcurrencyConflict)
                    throw Uncertain(mutation, commitException, new InvalidDataException($"Groundwork rejected expired mutation receipt reclamation with status '{deleted.Status}'."));
            }

            throw Uncertain(mutation, commitException, new InvalidDataException("The expired mutation receipt kept changing during bounded reclamation."));
        }
        catch (Exception) when (!cleanupCompleted)
        {
            throw;
        }
        catch (GroundworkIdentityUncertainCommitException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception reconciliationException)
        {
            throw Uncertain(mutation, commitException, reconciliationException);
        }
    }

    private GroundworkIdentityRowWrite ReceiptWrite(
        GroundworkIdentityAtomicMutation mutation,
        GroundworkIdentityWriteResult result)
    {
        var createdAt = clock.GetUtcNow();
        var receipt = new IdentityMutationReceipt(
            mutation.MutationReceiptId,
            mutation.OperationId,
            mutation.RequestFingerprint.Value,
            IdentityAtomicMutationOutcome.FromResult(result),
            createdAt,
            createdAt.Add(receiptLifetime));
        return GroundworkIdentityDocumentRows.Write(
            IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
            mutation.MutationReceiptId,
            receipt,
            expectedVersion: 0,
            new Dictionary<string, object?>
            {
                [IdentityStorageManifest.MutationReceiptExpiresAtField] = receipt.ExpiresAt
            });
    }

    private static IdentityMutationReceipt DeserializeReceipt(
        GroundworkIdentityRow row,
        GroundworkIdentityAtomicMutation mutation,
        Exception? commitException)
    {
        try
        {
            return GroundworkIdentityDocumentRows.Deserialize<IdentityMutationReceipt>(row);
        }
        catch (Exception exception)
        {
            throw Uncertain(mutation, commitException, exception);
        }
    }

    private static bool Matches(IdentityMutationReceipt receipt, GroundworkIdentityAtomicMutation mutation) =>
        StringComparer.Ordinal.Equals(receipt.MutationReceiptId, mutation.MutationReceiptId) &&
        StringComparer.Ordinal.Equals(receipt.OperationId, mutation.OperationId) &&
        StringComparer.Ordinal.Equals(receipt.RequestFingerprint, mutation.RequestFingerprint.Value);

    private static GroundworkIdentityUncertainCommitException Uncertain(
        GroundworkIdentityAtomicMutation mutation,
        Exception? commitException,
        Exception reconciliationException) =>
        new(
            $"Identity mutation '{mutation.OperationId}' has an uncertain commit outcome because its mutation receipt could not be classified within the bounded reconciliation window.",
            commitException is null ? reconciliationException : new AggregateException(commitException, reconciliationException));

    private static GroundworkIdentityWriteResult Failure(BatchWriteException failure)
    {
        var attributed = failure.Outcomes
            .OrderBy(outcome => outcome.Outcome.Status == WriteOutcomeStatus.UniqueViolation ? 0 : 1)
            .First();
        var id = attributed.Write.Values?.Values.TryGetValue(IdentityV2StorageManifest.IdField, out var value) == true
            ? value as string
            : null;
        return new GroundworkIdentityWriteResult(
            attributed.Outcome.Status,
            attributed.Outcome.Version,
            $"Identity exact mutation failed with status '{attributed.Outcome.Status}'.",
            AuthoritativeId: id,
            FailedUnitId: attributed.Write.Unit.Id.Value);
    }

    // Groundwork's provider-neutral session contract is synchronous. Isolate its call so the
    // Identity reconciliation/cleanup window remains bounded even when a provider call stalls.
    private static Task<T> BoundProviderCallAsync<T>(Func<T> call, CancellationToken cancellationToken) =>
        Task.Run(call, CancellationToken.None).WaitAsync(cancellationToken);

    private static IdentityRequestFingerprint Fingerprint(GroundworkIdentityRowWrite write) =>
        IdentityRequestFingerprint.FromParts(
            write.UnitId,
            write.Id,
            IdentityStorageManifest.SchemaVersion,
            write.CanonicalJson,
            write.Condition.Kind.ToString(),
            write.Condition.ExpectedVersion?.ToString(CultureInfo.InvariantCulture));

    private sealed record IdentityMutationReceipt(
        string MutationReceiptId,
        string OperationId,
        string RequestFingerprint,
        IdentityAtomicMutationOutcome Outcome,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt);

    private sealed record IdentityAtomicMutationOutcome(
        WriteOutcomeStatus Status,
        long? Version,
        string Message,
        GroundworkIdentityRow? Row,
        string? AuthoritativeId,
        string? FailedUnitId)
    {
        public static IdentityAtomicMutationOutcome FromResult(GroundworkIdentityWriteResult result) =>
            new(result.Status, result.Version, result.Message, result.Row, result.AuthoritativeId, result.FailedUnitId);

        public GroundworkIdentityWriteResult ToResult() =>
            new(Status, Version, Message, Normalize(Row), AuthoritativeId, FailedUnitId);

        private static GroundworkIdentityRow? Normalize(GroundworkIdentityRow? row)
        {
            if (row is null || !row.ProjectedValues.Values.Any(value => value is System.Text.Json.JsonElement))
                return row;

            var columns = IdentityV2StorageManifest.Require(row.UnitId).Columns
                .ToDictionary(column => column.Name, StringComparer.Ordinal);
            var projected = row.ProjectedValues.ToDictionary(
                pair => pair.Key,
                pair => pair.Value is System.Text.Json.JsonElement element && columns.TryGetValue(pair.Key, out var column)
                    ? PortableValue(element, column.Type)
                    : pair.Value,
                StringComparer.Ordinal);
            return row with { ProjectedValues = projected };
        }

        private static object? PortableValue(System.Text.Json.JsonElement value, global::Groundwork.Kernel.PortableType type)
        {
            if (value.ValueKind == System.Text.Json.JsonValueKind.Null)
                return null;
            return type switch
            {
                global::Groundwork.Kernel.PortableType.String => value.GetString(),
                global::Groundwork.Kernel.PortableType.Int32 => value.GetInt32(),
                global::Groundwork.Kernel.PortableType.Int64 => value.GetInt64(),
                global::Groundwork.Kernel.PortableType.Decimal => value.GetDecimal(),
                global::Groundwork.Kernel.PortableType.Boolean => value.GetBoolean(),
                global::Groundwork.Kernel.PortableType.Guid => value.GetGuid(),
                global::Groundwork.Kernel.PortableType.DateTimeOffset => value.GetDateTimeOffset(),
                global::Groundwork.Kernel.PortableType.Binary => value.GetBytesFromBase64(),
                global::Groundwork.Kernel.PortableType.Json => value.Clone(),
                _ => throw new InvalidDataException($"Identity mutation receipt contains unsupported projected type '{type}'.")
            };
        }
    }
}

internal sealed class IdentityMutationReceiptCleanupCoordinator
{
    private readonly ConcurrentDictionary<string, CleanupWindow> windows = new(StringComparer.Ordinal);

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
            window.AttemptsSinceCleanup++;
            if (window.AttemptsSinceCleanup < attemptInterval && now < window.NextCleanupAt)
                return;

            await cleanupAsync(cancellationToken);
            window.AttemptsSinceCleanup = 0;
            window.NextCleanupAt = now.Add(interval);
        }
        finally
        {
            window.Gate.Release();
        }
    }

    private sealed class CleanupWindow
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int AttemptsSinceCleanup { get; set; }
        public DateTimeOffset NextCleanupAt { get; set; } = DateTimeOffset.MinValue;
    }
}
