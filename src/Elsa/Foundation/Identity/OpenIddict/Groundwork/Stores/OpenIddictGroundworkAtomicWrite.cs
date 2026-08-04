using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Core.Queries;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Serialization;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;

/// <summary>
/// Reproduces the identity domain's exactly-once atomic-mutation machinery
/// (<c>GroundworkIdentityAtomicWrite</c>) for OpenIddict (spec 106 T030). Per attempt, on one unit of work:
/// stage the caller's mutation, save a deterministic mutation receipt in the SAME unit of work, then commit.
/// A replay, a receipt-CAS race, or a lost commit acknowledgement all resolve by reading the receipt back
/// from a fresh session rather than re-executing the caller's staged mutation - that is the exactly-once
/// property the authorization and token stores need for redeem/revoke/prune.
/// </summary>
public sealed class OpenIddictGroundworkAtomicWrite
{
    private const int ReceiptCleanupAttemptInterval = 32;
    private const int ReceiptCleanupBatchSize = 64;
    private const int MaxReceiptReclaimAttempts = 3;
    private static readonly TimeSpan DefaultReconciliationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultReceiptLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan ReceiptCleanupInterval = TimeSpan.FromMinutes(5);
    private static readonly VersionedJsonDocumentCodec Codec = OpenIddictGroundworkJson.CreateCodec();

    private readonly OpenIddictGroundworkStoreSessionFactory? _sessionFactory;
    private readonly IDocumentStore? _directStore;
    private readonly OpenIddictMutationReceiptCleanupCoordinator _cleanupCoordinator;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _reconciliationTimeout;
    private readonly TimeSpan _receiptLifetime;

    public OpenIddictGroundworkAtomicWrite(
        OpenIddictGroundworkStoreSessionFactory sessionFactory,
        TimeProvider? timeProvider = null,
        TimeSpan? reconciliationTimeout = null,
        TimeSpan? receiptLifetime = null)
        : this(
            sessionFactory,
            new OpenIddictMutationReceiptCleanupCoordinator(),
            timeProvider,
            reconciliationTimeout,
            receiptLifetime)
    {
    }

    internal OpenIddictGroundworkAtomicWrite(
        OpenIddictGroundworkStoreSessionFactory sessionFactory,
        OpenIddictMutationReceiptCleanupCoordinator cleanupCoordinator,
        TimeProvider? timeProvider = null,
        TimeSpan? reconciliationTimeout = null,
        TimeSpan? receiptLifetime = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _cleanupCoordinator = cleanupCoordinator ?? throw new ArgumentNullException(nameof(cleanupCoordinator));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _reconciliationTimeout = reconciliationTimeout ?? DefaultReconciliationTimeout;
        _receiptLifetime = receiptLifetime ?? DefaultReceiptLifetime;
        ValidateTimeouts(_reconciliationTimeout, _receiptLifetime);
    }

    internal OpenIddictGroundworkAtomicWrite(
        IDocumentStore directStore,
        TimeProvider? timeProvider = null,
        TimeSpan? reconciliationTimeout = null,
        TimeSpan? receiptLifetime = null)
        : this(
            directStore,
            new OpenIddictMutationReceiptCleanupCoordinator(),
            timeProvider,
            reconciliationTimeout,
            receiptLifetime)
    {
    }

    internal OpenIddictGroundworkAtomicWrite(
        IDocumentStore directStore,
        OpenIddictMutationReceiptCleanupCoordinator cleanupCoordinator,
        TimeProvider? timeProvider = null,
        TimeSpan? reconciliationTimeout = null,
        TimeSpan? receiptLifetime = null)
    {
        _directStore = directStore ?? throw new ArgumentNullException(nameof(directStore));
        _cleanupCoordinator = cleanupCoordinator ?? throw new ArgumentNullException(nameof(cleanupCoordinator));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _reconciliationTimeout = reconciliationTimeout ?? DefaultReconciliationTimeout;
        _receiptLifetime = receiptLifetime ?? DefaultReceiptLifetime;
        ValidateTimeouts(_reconciliationTimeout, _receiptLifetime);
    }

    private static void ValidateTimeouts(TimeSpan reconciliationTimeout, TimeSpan receiptLifetime)
    {
        if (reconciliationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(reconciliationTimeout), "The reconciliation timeout must be positive.");
        if (receiptLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(receiptLifetime), "The mutation receipt lifetime must be positive.");
    }

    public async ValueTask<DocumentStoreWriteResult> ExecuteAsync(
        OpenIddictGroundworkAtomicMutation mutation,
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
                new InvalidDataException("A competing exact OpenIddict mutation won the receipt CAS, but its authoritative receipt was not readable."));
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
        var query = new DocumentQuery(
            OpenIddictGroundworkJson.MutationReceiptDocumentKind,
            OpenIddictGroundworkStorageManifest.ListExpiredMutationReceiptsQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                OpenIddictGroundworkStorageManifest.MutationReceiptExpiresAtField,
                now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)))],
            [new DocumentQueryOrder(OpenIddictGroundworkStorageManifest.MutationReceiptExpiresAtField)],
            take: ReceiptCleanupBatchSize);
        var expired = await boundedStore.QueryAsync(query, cancellationToken);
        foreach (var envelope in expired.Documents.Take(ReceiptCleanupBatchSize))
        {
            var receipt = DeserializeReceipt(envelope);
            if (receipt is null || !string.Equals(receipt.MutationReceiptId, envelope.Id, StringComparison.Ordinal))
                throw new InvalidDataException("The expired mutation receipt query returned an invalid receipt document.");
            if (receipt.ExpiresAt > now)
                continue;

            var delete = await store.DeleteAsync(
                new DeleteDocumentRequest(
                    OpenIddictGroundworkJson.MutationReceiptDocumentKind,
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
        OpenIddictGroundworkAtomicMutation mutation,
        Func<IDocumentUnitOfWork, CancellationToken, Task<DocumentStoreWriteResult>> stageAsync,
        CancellationToken cancellationToken)
    {
        if (_directStore is not null)
            return await ExecuteAttemptOnStoreAsync(_directStore, mutation, stageAsync, cancellationToken);

        await using var session = await _sessionFactory!.CreateAsync(cancellationToken);
        return await ExecuteAttemptOnStoreAsync(session.DocumentStore, mutation, stageAsync, cancellationToken);
    }

    private async Task<DocumentStoreWriteResult> ExecuteAttemptOnStoreAsync(
        IDocumentStore store,
        OpenIddictGroundworkAtomicMutation mutation,
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
        OpenIddictGroundworkAtomicMutation mutation,
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

            await using var session = await _sessionFactory!.CreateAsync(timeout.Token);
            if (runCleanup)
                await CleanupExpiredReceiptsIfDueAsync(session.DocumentStore, session.BoundedDocumentStore, timeout.Token);
            cleanupCompleted = true;
            return await ReadReceiptAsync(session.DocumentStore, mutation, commitException, timeout.Token);
        }
        catch (Exception) when (!cleanupCompleted)
        {
            throw;
        }
        catch (OpenIddictGroundworkUncertainCommitException)
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
        OpenIddictGroundworkAtomicMutation mutation,
        Exception? commitException,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxReceiptReclaimAttempts; attempt++)
        {
            var envelope = await store.LoadAsync(
                OpenIddictGroundworkJson.MutationReceiptDocumentKind,
                mutation.MutationReceiptId,
                cancellationToken);
            if (envelope is null)
                return null;

            OpenIddictMutationReceipt? receipt;
            try
            {
                receipt = DeserializeReceipt(envelope);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw Uncertain(mutation, commitException, exception);
            }

            if (!IsMatchingReceipt(receipt, mutation))
            {
                throw Uncertain(
                    mutation,
                    commitException,
                    new InvalidDataException("The mutation receipt did not match the attempted OpenIddict mutation."));
            }

            if (receipt!.ExpiresAt > _timeProvider.GetUtcNow())
                return receipt.Outcome.ToResult();

            var delete = await store.DeleteAsync(
                new DeleteDocumentRequest(
                    OpenIddictGroundworkJson.MutationReceiptDocumentKind,
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

    private static OpenIddictMutationReceipt? DeserializeReceipt(DocumentEnvelope envelope) =>
        Codec.Deserialize<OpenIddictMutationReceipt>(envelope);

    private static bool IsMatchingReceipt(
        OpenIddictMutationReceipt? receipt,
        OpenIddictGroundworkAtomicMutation mutation) =>
        receipt is not null &&
        string.Equals(receipt.MutationReceiptId, mutation.MutationReceiptId, StringComparison.Ordinal) &&
        string.Equals(receipt.OperationIdentity, mutation.OperationIdentity.Value, StringComparison.Ordinal);

    private SaveDocumentRequest MutationReceiptRequest(
        OpenIddictGroundworkAtomicMutation mutation,
        DocumentStoreWriteResult result)
    {
        var createdAt = _timeProvider.GetUtcNow();
        var receipt = new OpenIddictMutationReceipt(
            mutation.MutationReceiptId,
            mutation.OperationIdentity.Value,
            mutation.OperationName,
            OpenIddictAtomicMutationOutcome.FromResult(result),
            createdAt,
            createdAt.Add(_receiptLifetime));
        return Codec.CreateSaveRequest(
            OpenIddictGroundworkJson.MutationReceiptDocumentKind,
            mutation.MutationReceiptId,
            receipt,
            expectedVersion: 0);
    }

    private static DocumentCommitScope CommitScope(OpenIddictGroundworkAtomicMutation mutation) =>
        new([
            .. mutation.CommitScope.Kinds,
            OpenIddictGroundworkJson.MutationReceiptDocumentKind
        ]);

    private static OpenIddictGroundworkUncertainCommitException Uncertain(
        OpenIddictGroundworkAtomicMutation mutation,
        Exception? commitException,
        Exception reconciliationException) =>
        new(
            $"OpenIddict mutation '{mutation.OperationIdentity.Value}' has an uncertain commit outcome because its mutation receipt could not be classified within the bounded reconciliation window.",
            commitException is null
                ? reconciliationException
                : new AggregateException(commitException, reconciliationException));

    private sealed record OpenIddictMutationReceipt(
        string MutationReceiptId,
        string OperationIdentity,
        string OperationName,
        OpenIddictAtomicMutationOutcome Outcome,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt);

    private sealed record OpenIddictAtomicMutationOutcome(
        DocumentStoreWriteStatus Status,
        OpenIddictAtomicMutationEnvelope? Document,
        string? AuthoritativeId)
    {
        public static OpenIddictAtomicMutationOutcome FromResult(DocumentStoreWriteResult result) =>
            new(
                result.Status,
                result.Document is null ? null : OpenIddictAtomicMutationEnvelope.FromEnvelope(result.Document),
                result.AuthoritativeId);

        public DocumentStoreWriteResult ToResult() =>
            new(Status, Document?.ToEnvelope(), AuthoritativeId);
    }

    private sealed record OpenIddictAtomicMutationEnvelope(
        string DocumentKind,
        string Id,
        string SchemaVersion,
        long Version,
        string ContentJson,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string? Scope)
    {
        public static OpenIddictAtomicMutationEnvelope FromEnvelope(DocumentEnvelope envelope) =>
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

/// <summary>
/// Debounces the opportunistic expired-receipt cleanup per access scope, mirroring
/// <c>IdentityMutationReceiptCleanupCoordinator</c>. OpenIddict Groundwork storage is always global, so in
/// practice one scope key ever exists, but the coordinator stays scope-keyed for parity with the identity
/// template and to tolerate future non-global session sources.
/// </summary>
internal sealed class OpenIddictMutationReceiptCleanupCoordinator
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
