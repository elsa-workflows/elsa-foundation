using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Foundation.Identity.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

public sealed class GroundworkIdentityAtomicWrite
{
    private const int ReceiptCleanupAttemptInterval = 32;
    private const int ReceiptCleanupBatchSize = 64;
    private const int MaxReceiptReclaimAttempts = 3;
    private static readonly TimeSpan DefaultReconciliationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultReceiptLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan ReceiptCleanupInterval = TimeSpan.FromMinutes(5);

    private readonly IGroundworkStoreSessionFactory? _sessionFactory;
    private readonly IDocumentStore? _directStore;
    private readonly IdentityMutationReceiptCleanupCoordinator _cleanupCoordinator;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _reconciliationTimeout;
    private readonly TimeSpan _receiptLifetime;

    public GroundworkIdentityAtomicWrite(
        IGroundworkStoreSessionFactory sessionFactory,
        TimeProvider? timeProvider = null,
        TimeSpan? reconciliationTimeout = null,
        TimeSpan? receiptLifetime = null)
        : this(
            sessionFactory,
            new IdentityMutationReceiptCleanupCoordinator(),
            timeProvider,
            reconciliationTimeout,
            receiptLifetime)
    {
    }

    internal GroundworkIdentityAtomicWrite(
        IGroundworkStoreSessionFactory sessionFactory,
        IdentityMutationReceiptCleanupCoordinator cleanupCoordinator,
        TimeProvider? timeProvider = null,
        TimeSpan? reconciliationTimeout = null,
        TimeSpan? receiptLifetime = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _cleanupCoordinator = cleanupCoordinator ?? throw new ArgumentNullException(nameof(cleanupCoordinator));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _reconciliationTimeout = reconciliationTimeout ?? DefaultReconciliationTimeout;
        _receiptLifetime = receiptLifetime ?? DefaultReceiptLifetime;
        if (_reconciliationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(reconciliationTimeout), "The reconciliation timeout must be positive.");
        if (_receiptLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(receiptLifetime), "The mutation receipt lifetime must be positive.");
    }

    internal GroundworkIdentityAtomicWrite(
        IDocumentStore directStore,
        TimeProvider? timeProvider = null,
        TimeSpan? reconciliationTimeout = null,
        TimeSpan? receiptLifetime = null)
        : this(
            directStore,
            new IdentityMutationReceiptCleanupCoordinator(),
            timeProvider,
            reconciliationTimeout,
            receiptLifetime)
    {
    }

    internal GroundworkIdentityAtomicWrite(
        IDocumentStore directStore,
        IdentityMutationReceiptCleanupCoordinator cleanupCoordinator,
        TimeProvider? timeProvider = null,
        TimeSpan? reconciliationTimeout = null,
        TimeSpan? receiptLifetime = null)
    {
        _directStore = directStore ?? throw new ArgumentNullException(nameof(directStore));
        _cleanupCoordinator = cleanupCoordinator ?? throw new ArgumentNullException(nameof(cleanupCoordinator));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _reconciliationTimeout = reconciliationTimeout ?? DefaultReconciliationTimeout;
        _receiptLifetime = receiptLifetime ?? DefaultReceiptLifetime;
        if (_reconciliationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(reconciliationTimeout), "The reconciliation timeout must be positive.");
        if (_receiptLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(receiptLifetime), "The mutation receipt lifetime must be positive.");
    }

    public async ValueTask<DocumentStoreWriteResult> SaveAsync(
        SaveDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fingerprint = Fingerprint(request);
        var mutation = GroundworkIdentityAtomicMutation.Create(
            "save-document",
            fingerprint,
            request.DocumentKind);
        return await ExecuteAsync(
            mutation,
            (unitOfWork, token) => unitOfWork.SaveAsync(request, token),
            cancellationToken);
    }

    public async ValueTask<DocumentStoreWriteResult> ExecuteAsync(
        GroundworkIdentityAtomicMutation mutation,
        Func<IDocumentUnitOfWork, CancellationToken, Task<DocumentStoreWriteResult>> stageAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(stageAsync);
        cancellationToken.ThrowIfCancellationRequested();

        if (await ReadReceiptInNewSessionAsync(
                mutation,
                commitException: null,
                runCleanup: true,
                cancellationToken) is { } replay)
            return replay;

        try
        {
            return await ExecuteAttemptAsync(mutation, stageAsync, cancellationToken);
        }
        catch (ReceiptRaceException)
        {
            if (await ReadReceiptInNewSessionAsync(
                    mutation,
                    commitException: null,
                    runCleanup: false,
                    CancellationToken.None) is { } raced)
                return raced;

            throw Uncertain(
                mutation,
                commitException: null,
                new InvalidDataException("A competing exact identity mutation won the receipt CAS, but its authoritative receipt was not readable."));
        }
        catch (CommitAttemptException attempt)
        {
            if (await ReadReceiptInNewSessionAsync(
                    mutation,
                    attempt.CommitException,
                    runCleanup: false,
                    CancellationToken.None) is { } reconciled)
                return reconciled;

            ExceptionDispatchInfo.Capture(attempt.CommitException).Throw();
            throw;
        }
    }

    private Task CleanupExpiredReceiptsIfDueAsync(
        IDocumentStore store,
        IBoundedDocumentStore boundedStore,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        return _cleanupCoordinator.RunIfDueAsync(
            CleanupScopeKey(store),
            now,
            ReceiptCleanupInterval,
            ReceiptCleanupAttemptInterval,
            token => CleanupExpiredReceiptsAsync(
                store,
                boundedStore,
                now,
                token),
            cancellationToken);
    }

    private static async Task CleanupExpiredReceiptsAsync(
        IDocumentStore store,
        IBoundedDocumentStore boundedStore,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var query = IdentityBoundedDocumentQueryPager.CreatePageQuery(
            IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
            IdentityStorageManifest.ListExpiredMutationReceiptsQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                IdentityStorageManifest.MutationReceiptExpiresAtField,
                now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)))],
            ReceiptCleanupBatchSize);
        var expired = await boundedStore.QueryAsync(query, cancellationToken);
        foreach (var envelope in expired.Documents.Take(ReceiptCleanupBatchSize))
        {
            var receipt = JsonSerializer.Deserialize<IdentityMutationReceipt>(
                envelope.ContentJson,
                IdentityGroundworkJson.Options);
            if (receipt is null ||
                !string.Equals(receipt.MutationReceiptId, envelope.Id, StringComparison.Ordinal))
                throw new InvalidDataException("The expired mutation receipt query returned an invalid receipt document.");
            if (receipt.ExpiresAt > now)
                continue;

            var delete = await store.DeleteAsync(
                new DeleteDocumentRequest(
                    IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
                    envelope.Id,
                    envelope.Version),
                cancellationToken);
            if (delete.Status is DocumentStoreWriteStatus.Deleted or
                DocumentStoreWriteStatus.NotFound or
                DocumentStoreWriteStatus.ConcurrencyConflict)
                continue;

            throw new InvalidOperationException(
                $"Groundwork rejected expired mutation receipt cleanup with status '{delete.Status}'.");
        }
    }

    private static string CleanupScopeKey(IDocumentStore store) => string.Join(
        ':',
        store.Access.Kind,
        store.Access.Scope?.Value ?? "global");

    private async Task<DocumentStoreWriteResult> ExecuteAttemptAsync(
        GroundworkIdentityAtomicMutation mutation,
        Func<IDocumentUnitOfWork, CancellationToken, Task<DocumentStoreWriteResult>> stageAsync,
        CancellationToken cancellationToken)
    {
        if (_directStore is not null)
            return await ExecuteAttemptOnStoreAsync(_directStore, mutation, stageAsync, cancellationToken);

        await using var session = await _sessionFactory!.CreateAsync(PersistenceAccessPolicy.Ordinary, cancellationToken);
        return await ExecuteAttemptOnStoreAsync(session.DocumentStore, mutation, stageAsync, cancellationToken);
    }

    private async Task<DocumentStoreWriteResult> ExecuteAttemptOnStoreAsync(
        IDocumentStore store,
        GroundworkIdentityAtomicMutation mutation,
        Func<IDocumentUnitOfWork, CancellationToken, Task<DocumentStoreWriteResult>> stageAsync,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await store.BeginAsync(CommitScope(mutation), cancellationToken);

        var result = await stageAsync(unitOfWork, cancellationToken);
        if (result.Status is not (DocumentStoreWriteStatus.Saved or DocumentStoreWriteStatus.Deleted))
            return result;

        var receiptResult = await unitOfWork.SaveAsync(MutationReceiptRequest(mutation, result), cancellationToken);
        if (receiptResult.Status is DocumentStoreWriteStatus.ConcurrencyConflict)
            throw new ReceiptRaceException();
        if (receiptResult.Status is not DocumentStoreWriteStatus.Saved)
            return receiptResult;

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            throw new CommitAttemptException(exception);
        }
    }

    private async Task<DocumentStoreWriteResult?> ReadReceiptInNewSessionAsync(
        GroundworkIdentityAtomicMutation mutation,
        Exception? commitException,
        bool runCleanup,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_reconciliationTimeout);
        var cleanupCompleted = !runCleanup;
        try
        {
            if (_directStore is not null)
            {
                if (runCleanup && _directStore is IBoundedDocumentStore boundedStore)
                    await CleanupExpiredReceiptsIfDueAsync(_directStore, boundedStore, timeout.Token);
                cleanupCompleted = true;
                return await ReadReceiptAsync(_directStore, mutation, commitException, timeout.Token);
            }

            await using var session = await _sessionFactory!.CreateAsync(PersistenceAccessPolicy.Ordinary, timeout.Token);
            if (runCleanup)
                await CleanupExpiredReceiptsIfDueAsync(session.DocumentStore, session.BoundedDocumentStore, timeout.Token);
            cleanupCompleted = true;
            return await ReadReceiptAsync(session.DocumentStore, mutation, commitException, timeout.Token);
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

    private async Task<DocumentStoreWriteResult?> ReadReceiptAsync(
        IDocumentStore store,
        GroundworkIdentityAtomicMutation mutation,
        Exception? commitException,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxReceiptReclaimAttempts; attempt++)
        {
            var envelope = await store.LoadAsync(
                IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
                mutation.MutationReceiptId,
                cancellationToken);
            if (envelope is null)
                return null;

            IdentityMutationReceipt? receipt;
            try
            {
                receipt = JsonSerializer.Deserialize<IdentityMutationReceipt>(
                    envelope.ContentJson,
                    IdentityGroundworkJson.Options);
            }
            catch (JsonException exception)
            {
                throw Uncertain(mutation, commitException, exception);
            }

            if (!IsMatchingReceipt(receipt, mutation))
            {
                throw Uncertain(
                    mutation,
                    commitException,
                    new InvalidDataException("The mutation receipt did not match the attempted identity mutation."));
            }

            if (receipt!.ExpiresAt > _timeProvider.GetUtcNow())
                return receipt.Outcome.ToResult();

            var delete = await store.DeleteAsync(
                new DeleteDocumentRequest(
                    IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
                    mutation.MutationReceiptId,
                    envelope.Version),
                cancellationToken);
            if (delete.Status is DocumentStoreWriteStatus.Deleted or DocumentStoreWriteStatus.NotFound)
                return null;
            if (delete.Status is not DocumentStoreWriteStatus.ConcurrencyConflict)
            {
                throw Uncertain(
                    mutation,
                    commitException,
                    new InvalidDataException($"Groundwork rejected expired mutation receipt reclamation with status '{delete.Status}'."));
            }
        }

        throw Uncertain(
            mutation,
            commitException,
            new InvalidDataException("The expired mutation receipt kept changing during bounded reclamation."));
    }

    private static bool IsMatchingReceipt(
        IdentityMutationReceipt? receipt,
        GroundworkIdentityAtomicMutation mutation) =>
        receipt is not null &&
        string.Equals(receipt.MutationReceiptId, mutation.MutationReceiptId, StringComparison.Ordinal) &&
        string.Equals(receipt.OperationId, mutation.OperationId, StringComparison.Ordinal) &&
        string.Equals(receipt.RequestFingerprint, mutation.RequestFingerprint.Value, StringComparison.Ordinal);

    private SaveDocumentRequest MutationReceiptRequest(
        GroundworkIdentityAtomicMutation mutation,
        DocumentStoreWriteResult result)
    {
        var createdAt = _timeProvider.GetUtcNow();
        var receipt = new IdentityMutationReceipt(
            mutation.MutationReceiptId,
            mutation.OperationId,
            mutation.RequestFingerprint.Value,
            IdentityAtomicMutationOutcome.FromResult(result),
            createdAt,
            createdAt.Add(_receiptLifetime));
        var content = JsonSerializer.Serialize(receipt, IdentityGroundworkJson.Options);
        return new SaveDocumentRequest(
            IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
            mutation.MutationReceiptId,
            IdentityStorageManifest.SchemaVersion,
            content,
            ExpectedVersion: 0);
    }

    private static DocumentCommitScope CommitScope(GroundworkIdentityAtomicMutation mutation) =>
        new([
            .. mutation.CommitScope.Kinds,
            IdentityStorageManifest.IdentityMutationReceiptDocumentKind
        ]);

    private static GroundworkIdentityUncertainCommitException Uncertain(
        GroundworkIdentityAtomicMutation mutation,
        Exception? commitException,
        Exception reconciliationException) =>
        new(
            $"Identity mutation '{mutation.OperationId}' has an uncertain commit outcome because its mutation receipt could not be classified within the bounded reconciliation window.",
            commitException is null
                ? reconciliationException
                : new AggregateException(commitException, reconciliationException));

    private static IdentityRequestFingerprint Fingerprint(SaveDocumentRequest request) =>
        IdentityRequestFingerprint.FromParts(
            request.DocumentKind,
            request.Id,
            request.SchemaVersion,
            request.ContentJson,
            request.ExpectedVersion?.ToString(CultureInfo.InvariantCulture));

    private sealed record IdentityMutationReceipt(
        string MutationReceiptId,
        string OperationId,
        string RequestFingerprint,
        IdentityAtomicMutationOutcome Outcome,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt);

    private sealed record IdentityAtomicMutationOutcome(
        DocumentStoreWriteStatus Status,
        IdentityAtomicMutationEnvelope? Document,
        string? AuthoritativeId)
    {
        public static IdentityAtomicMutationOutcome FromResult(DocumentStoreWriteResult result) =>
            new(
                result.Status,
                result.Document is null ? null : IdentityAtomicMutationEnvelope.FromEnvelope(result.Document),
                result.AuthoritativeId);

        public DocumentStoreWriteResult ToResult() =>
            new(Status, Document?.ToEnvelope(), AuthoritativeId);
    }

    private sealed record IdentityAtomicMutationEnvelope(
        string DocumentKind,
        string Id,
        string SchemaVersion,
        long Version,
        string ContentJson,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string? Scope)
    {
        public static IdentityAtomicMutationEnvelope FromEnvelope(DocumentEnvelope envelope) =>
            new(
                envelope.DocumentKind,
                envelope.Id,
                envelope.SchemaVersion,
                envelope.Version,
                envelope.ContentJson,
                envelope.CreatedAt,
                envelope.UpdatedAt,
                envelope.Scope?.Value);

        public DocumentEnvelope ToEnvelope() =>
            new(DocumentKind, Id, SchemaVersion, Version, ContentJson, CreatedAt, UpdatedAt)
            {
                Scope = Scope is null ? null : new StorageScope(Scope)
            };
    }

    private sealed class CommitAttemptException(Exception commitException) : Exception
    {
        public Exception CommitException { get; } = commitException;
    }

    private sealed class ReceiptRaceException : Exception
    {
    }
}

internal sealed class IdentityMutationReceiptCleanupCoordinator
{
    private readonly ConcurrentDictionary<string, CleanupWindow> _windows = new(StringComparer.Ordinal);

    public async Task RunIfDueAsync(
        string scopeKey,
        DateTimeOffset now,
        TimeSpan interval,
        int attemptInterval,
        Func<CancellationToken, Task> cleanupAsync,
        CancellationToken cancellationToken)
    {
        var window = _windows.GetOrAdd(scopeKey, static _ => new CleanupWindow());
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
