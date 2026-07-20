using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Coordinates one replay-safe, multi-document design mutation. The caller supplies a stable operation
/// identity independently from the canonical request fingerprint; this coordinator persists the accepted
/// result in the same Groundwork unit of work as the mutation.
/// </summary>
public sealed class GroundworkDesignAtomicWrite
{
    private const string MarkerIdentityVersion = "elsa-design-operation:v1";
    private const string RollbackFailureDataKey = "Elsa.Persistence.Groundwork.DesignAtomicWrite.RollbackFailure";
    private static readonly TimeSpan DefaultReconciliationTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions MarkerJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDocumentStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _reconciliationTimeout;

    public GroundworkDesignAtomicWrite(
        IDocumentStore store,
        TimeProvider? timeProvider = null,
        TimeSpan? reconciliationTimeout = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _reconciliationTimeout = reconciliationTimeout ?? DefaultReconciliationTimeout;
        if (_reconciliationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconciliationTimeout),
                "The design-operation reconciliation timeout must be positive.");
        }
    }

    /// <summary>
    /// Executes <paramref name="stage"/> at most once for the supplied operation identity. Exact replays return
    /// the previously committed result, while key reuse with different mutation material returns a conflict.
    /// </summary>
    public async Task<GroundworkDesignAtomicWriteResult> ExecuteAsync(
        GroundworkDesignAtomicWriteRequest request,
        Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync(request, beforeAttempt: null, stage, cancellationToken);

    /// <summary>
    /// Executes an operation-owned read preflight after an exact-replay lookup misses and before the
    /// transactional unit of work starts. Callers can use this to perform provider reads while holding
    /// their aggregate lock without opening a second store session inside the write transaction.
    /// </summary>
    public async Task<GroundworkDesignAtomicWriteResult> ExecuteAsync(
        GroundworkDesignAtomicWriteRequest request,
        Func<CancellationToken, Task>? beforeAttempt,
        Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stage);
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        // This preflight intentionally precedes every provider operation, including the replay lookup.
        if (_store.TransactionBoundary != TransactionBoundary.CrossUnitAtomic)
        {
            throw new GroundworkDesignAtomicWriteReadinessException(
                $"Groundwork cannot atomically execute design operation '{request.Operation.OperationKind}' " +
                $"with key '{request.Operation.OperationKey}' because the active store advertises " +
                $"transaction boundary '{_store.TransactionBoundary}'.");
        }

        var markerId = CreateMarkerId(request.Operation);
        if (await LoadMarkerAsync(markerId, request.Operation, cancellationToken) is { } existing)
        {
            return Resolve(
                existing,
                request,
                GroundworkDesignAtomicWriteStatus.Replayed);
        }

        if (beforeAttempt is not null)
            await beforeAttempt(cancellationToken);

        try
        {
            return await ExecuteAttemptAsync(request, markerId, stage, cancellationToken);
        }
        catch (DesignOperationMarkerRaceException)
        {
            try
            {
                var marker = await LoadMarkerAsync(markerId, request.Operation, cancellationToken)
                             ?? throw new GroundworkDesignAtomicWriteUncertainCommitException(
                                 $"Design operation marker '{markerId}' conflicted, but the winning marker could not be reloaded.");
                return Resolve(
                    marker,
                    request,
                    GroundworkDesignAtomicWriteStatus.Replayed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (GroundworkDesignAtomicWriteUncertainCommitException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new GroundworkDesignAtomicWriteUncertainCommitException(
                    $"Design operation marker '{markerId}' conflicted, but the winning marker could not be classified.",
                    exception);
            }
        }
        catch (DocumentCommitAcknowledgementUncertainException exception)
        {
            using var reconciliation = new CancellationTokenSource(_reconciliationTimeout, _timeProvider);
            try
            {
                if (await LoadMarkerAsync(markerId, request.Operation, reconciliation.Token) is { } marker)
                {
                    return Resolve(
                        marker,
                        request,
                        GroundworkDesignAtomicWriteStatus.Reconciled);
                }
            }
            catch (OperationCanceledException) when (reconciliation.IsCancellationRequested)
            {
                // Preserve the uncertain-commit contract when the independently bounded reconciliation
                // lookup also times out. The provider may still have committed the mutation and marker.
            }
            catch (Exception reconciliationException)
            {
                throw new GroundworkDesignAtomicWriteUncertainCommitException(
                    $"Design operation '{request.Operation.OperationKind}' with key " +
                    $"'{request.Operation.OperationKey}' may have committed, but its durable marker could not be classified.",
                    new AggregateException(exception, reconciliationException));
            }

            throw new GroundworkDesignAtomicWriteUncertainCommitException(
                $"Design operation '{request.Operation.OperationKind}' with key " +
                $"'{request.Operation.OperationKey}' may have committed, but its durable marker could not be reconciled.",
                exception);
        }
    }

    private async Task<GroundworkDesignAtomicWriteResult> ExecuteAttemptAsync(
        GroundworkDesignAtomicWriteRequest request,
        string markerId,
        Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage,
        CancellationToken cancellationToken)
    {
        IDocumentUnitOfWork unitOfWork;
        try
        {
            unitOfWork = await _store.BeginAsync(CommitScope(request), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GroundworkDesignAtomicWriteProviderFailureException(
                "Groundwork could not begin the design-operation unit of work.",
                exception);
        }

        await using (unitOfWork)
        {
            try
            {
                var context = new GroundworkDesignAtomicWriteContext(unitOfWork);
                var staged = await stage(context, cancellationToken);
                ArgumentNullException.ThrowIfNull(staged);
                ValidateStageResult(staged);

                if (!staged.IsAccepted)
                {
                    await unitOfWork.RollbackAsync(CancellationToken.None);
                    return GroundworkDesignAtomicWriteResult.Rejected();
                }

                // Groundwork already rolls back and terminally poisons a UoW before returning a non-success
                // write result. Still attempt rollback for compatible providers and test doubles, while tolerating
                // the expected terminal-UoW rejection from conforming Groundwork providers.
                if (context.HasNonSuccessWrite)
                {
                    await ObserveTerminalRollbackAsync(unitOfWork);
                    return GroundworkDesignAtomicWriteResult.Rejected();
                }

                var markerResult = await context.SaveAsync(
                    CreateMarkerRequest(markerId, request, staged),
                    cancellationToken);
                if (markerResult.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                {
                    await ObserveTerminalRollbackAsync(unitOfWork);
                    throw new DesignOperationMarkerRaceException();
                }
                if (markerResult.Status != DocumentStoreWriteStatus.Saved)
                {
                    await ObserveTerminalRollbackAsync(unitOfWork);
                    return GroundworkDesignAtomicWriteResult.Rejected();
                }

                try
                {
                    await unitOfWork.CommitAsync(cancellationToken);
                }
                catch (DocumentCommitAcknowledgementUncertainException)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new GroundworkDesignAtomicWriteProviderFailureException(
                        "Groundwork could not commit the design-operation unit of work.",
                        exception);
                }

                return GroundworkDesignAtomicWriteResult.Committed(
                    staged.AuthoritativeResultFingerprint!,
                    staged.AuthoritativeResultJson!);
            }
            catch (DocumentCommitAcknowledgementUncertainException)
            {
                // A rollback is unsafe after an uncertain acknowledgement because the transaction may
                // already be durable. The outer coordinator performs a fresh marker reconciliation read.
                throw;
            }
            catch (DesignOperationMarkerRaceException)
            {
                // The marker-save branch already made a best-effort rollback attempt. The outer coordinator
                // reloads the winning durable marker without re-running the caller's staging callback.
                throw;
            }
            catch (Exception exception)
            {
                await RollbackPreservingPrimaryAsync(unitOfWork, exception);
                throw;
            }
        }
    }

    private static async Task ObserveTerminalRollbackAsync(IDocumentUnitOfWork unitOfWork)
    {
        try
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // IDocumentUnitOfWork guarantees that the preceding non-success save/delete has already
            // atomically rolled back and completed the UoW. Conforming providers therefore reject this
            // compatibility observation call; its failure cannot make the preceding decision uncertain.
        }
    }

    private static async Task RollbackPreservingPrimaryAsync(
        IDocumentUnitOfWork unitOfWork,
        Exception primaryException)
    {
        try
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollbackException)
        {
            primaryException.Data[RollbackFailureDataKey] = rollbackException;
        }
    }

    private async Task<DesignOperationMarker?> LoadMarkerAsync(
        string markerId,
        GroundworkDesignOperationIdentity operation,
        CancellationToken cancellationToken)
    {
        DocumentEnvelope? envelope;
        try
        {
            envelope = await _store.LoadAsync(
                GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind,
                markerId,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GroundworkDesignAtomicWriteProviderFailureException(
                $"Groundwork could not read design-operation marker '{markerId}'.",
                exception);
        }
        if (envelope is null)
            return null;
        if (!StringComparer.Ordinal.Equals(
                envelope.SchemaVersion,
                GroundworkDesignAtomicWriteStorageManifest.SchemaVersion))
        {
            throw CorruptMarker(
                markerId,
                new InvalidDataException(
                    $"Unsupported design-operation marker schema version '{envelope.SchemaVersion}'."));
        }

        DesignOperationMarker? marker;
        try
        {
            marker = JsonSerializer.Deserialize<DesignOperationMarker>(
                envelope.ContentJson,
                MarkerJsonOptions);
        }
        catch (JsonException exception)
        {
            throw CorruptMarker(markerId, exception);
        }

        if (marker is null ||
            !StringComparer.Ordinal.Equals(marker.OperationKind, operation.OperationKind) ||
            !StringComparer.Ordinal.Equals(marker.OperationKey, operation.OperationKey) ||
            string.IsNullOrWhiteSpace(marker.CanonicalRequestFingerprint) ||
            string.IsNullOrWhiteSpace(marker.AuthoritativeResultFingerprint))
        {
            throw CorruptMarker(
                markerId,
                new InvalidDataException("The design-operation marker identity did not match its requested operation."));
        }

        ValidateJson(
            marker.AuthoritativeResultJson,
            nameof(marker.AuthoritativeResultJson),
            exception => CorruptMarker(markerId, exception));
        return marker;
    }

    private static GroundworkDesignAtomicWriteResult Resolve(
        DesignOperationMarker marker,
        GroundworkDesignAtomicWriteRequest request,
        GroundworkDesignAtomicWriteStatus exactMatchStatus)
    {
        if (!StringComparer.Ordinal.Equals(
                marker.CanonicalRequestFingerprint,
                request.CanonicalRequestFingerprint))
        {
            return GroundworkDesignAtomicWriteResult.Conflict();
        }

        return exactMatchStatus switch
        {
            GroundworkDesignAtomicWriteStatus.Replayed =>
                GroundworkDesignAtomicWriteResult.Replayed(
                    marker.AuthoritativeResultFingerprint,
                    marker.AuthoritativeResultJson),
            GroundworkDesignAtomicWriteStatus.Reconciled =>
                GroundworkDesignAtomicWriteResult.Reconciled(
                    marker.AuthoritativeResultFingerprint,
                    marker.AuthoritativeResultJson),
            _ => throw new ArgumentOutOfRangeException(
                nameof(exactMatchStatus),
                exactMatchStatus,
                "An exact marker match can only be classified as replayed or reconciled.")
        };
    }

    private static SaveDocumentRequest CreateMarkerRequest(
        string markerId,
        GroundworkDesignAtomicWriteRequest request,
        GroundworkDesignAtomicWriteStageResult staged)
    {
        var marker = new DesignOperationMarker(
            request.Operation.OperationKind,
            request.Operation.OperationKey,
            request.CanonicalRequestFingerprint,
            staged.AuthoritativeResultFingerprint!,
            staged.AuthoritativeResultJson!);
        return new SaveDocumentRequest(
            GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind,
            markerId,
            GroundworkDesignAtomicWriteStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(marker, MarkerJsonOptions),
            ExpectedVersion: 0);
    }

    private static DocumentCommitScope CommitScope(GroundworkDesignAtomicWriteRequest request) =>
        new([
            .. request.MutatedDocumentKinds,
            GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind
        ]);

    private static string CreateMarkerId(GroundworkDesignOperationIdentity operation)
    {
        var framed = string.Concat(
            MarkerIdentityVersion,
            "|",
            Encoding.UTF8.GetByteCount(operation.OperationKind).ToString(CultureInfo.InvariantCulture),
            ":",
            operation.OperationKind,
            "|",
            Encoding.UTF8.GetByteCount(operation.OperationKey).ToString(CultureInfo.InvariantCulture),
            ":",
            operation.OperationKey);
        return $"design-operation-v1-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(framed)))}";
    }

    private static void ValidateRequest(GroundworkDesignAtomicWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Operation.OperationKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Operation.OperationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CanonicalRequestFingerprint);
        ArgumentNullException.ThrowIfNull(request.MutatedDocumentKinds);
        if (request.MutatedDocumentKinds.Count == 0)
            throw new ArgumentException("At least one mutated document kind is required.", nameof(request));
        if (request.MutatedDocumentKinds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Mutated document kinds cannot be blank.", nameof(request));
        if (request.MutatedDocumentKinds.Contains(
                GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind,
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "The design-operation marker kind is managed by the atomic-write coordinator.",
                nameof(request));
        }
    }

    private static void ValidateStageResult(GroundworkDesignAtomicWriteStageResult result)
    {
        if (!result.IsAccepted)
        {
            if (result.AuthoritativeResultFingerprint is not null ||
                result.AuthoritativeResultJson is not null)
            {
                throw new ArgumentException(
                    "A rejected design-operation stage cannot declare an authoritative result.",
                    nameof(result));
            }

            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(result.AuthoritativeResultFingerprint);
        ValidateJson(
            result.AuthoritativeResultJson,
            nameof(result.AuthoritativeResultJson),
            exception => new ArgumentException(
                "The authoritative design-operation result must be valid JSON.",
                nameof(result),
                exception));
    }

    private static void ValidateJson(
        string? json,
        string parameterName,
        Func<Exception, Exception> exceptionFactory)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw exceptionFactory(new ArgumentException("JSON cannot be blank.", parameterName));
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw exceptionFactory(exception);
        }
    }

    private static GroundworkDesignAtomicWriteCorruptMarkerException CorruptMarker(
        string markerId,
        Exception exception) =>
        new($"Design operation marker '{markerId}' is corrupt.", markerId, exception);

    private sealed record DesignOperationMarker(
        string OperationKind,
        string OperationKey,
        string CanonicalRequestFingerprint,
        string AuthoritativeResultFingerprint,
        string AuthoritativeResultJson);

    private sealed class DesignOperationMarkerRaceException : Exception;
}

/// <summary>A stable caller identity for one logical design mutation.</summary>
public sealed record GroundworkDesignOperationIdentity
{
    public GroundworkDesignOperationIdentity(string operationKind, string operationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        OperationKind = operationKind;
        OperationKey = operationKey;
    }

    public string OperationKind { get; }
    public string OperationKey { get; }
}

/// <summary>The complete provider-neutral material needed to execute one replay-safe design mutation.</summary>
public sealed record GroundworkDesignAtomicWriteRequest
{
    public GroundworkDesignAtomicWriteRequest(
        GroundworkDesignOperationIdentity operation,
        string canonicalRequestFingerprint,
        IReadOnlyCollection<string> mutatedDocumentKinds)
    {
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRequestFingerprint);
        ArgumentNullException.ThrowIfNull(mutatedDocumentKinds);
        CanonicalRequestFingerprint = canonicalRequestFingerprint;
        MutatedDocumentKinds = Array.AsReadOnly(mutatedDocumentKinds.ToArray());
    }

    public GroundworkDesignOperationIdentity Operation { get; }
    public string CanonicalRequestFingerprint { get; }
    public IReadOnlyCollection<string> MutatedDocumentKinds { get; }
}

/// <summary>
/// Exposes only staging operations for an active design transaction. Commit and rollback remain exclusively
/// owned by <see cref="GroundworkDesignAtomicWrite"/>, preventing a command callback from making domain writes
/// durable before the operation marker has been staged.
/// </summary>
public sealed class GroundworkDesignAtomicWriteContext
{
    private readonly IDocumentUnitOfWork _unitOfWork;

    internal GroundworkDesignAtomicWriteContext(IDocumentUnitOfWork unitOfWork) =>
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));

    internal bool HasNonSuccessWrite { get; private set; }

    public async Task<DocumentStoreWriteResult> SaveAsync(
        SaveDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DocumentStoreWriteResult result;
        try
        {
            result = await _unitOfWork.SaveAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GroundworkDesignAtomicWriteProviderFailureException(
                $"Groundwork could not save document '{request.Id}' of kind '{request.DocumentKind}'.",
                exception);
        }
        if (result.Status != DocumentStoreWriteStatus.Saved)
            HasNonSuccessWrite = true;
        return result;
    }

    public async Task<DocumentStoreWriteResult> DeleteAsync(
        DeleteDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DocumentStoreWriteResult result;
        try
        {
            result = await _unitOfWork.DeleteAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GroundworkDesignAtomicWriteProviderFailureException(
                $"Groundwork could not delete document '{request.Id}' of kind '{request.DocumentKind}'.",
                exception);
        }
        if (result.Status != DocumentStoreWriteStatus.Deleted)
            HasNonSuccessWrite = true;
        return result;
    }

    public Task<DocumentEnvelope?> LoadAsync(
        string documentKind,
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return LoadCoreAsync(documentKind, id, cancellationToken);
    }

    private async Task<DocumentEnvelope?> LoadCoreAsync(string documentKind, string id, CancellationToken cancellationToken)
    {
        try
        {
            return await _unitOfWork.LoadAsync(documentKind, id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GroundworkDesignAtomicWriteProviderFailureException(
                $"Groundwork could not read document '{id}' of kind '{documentKind}'.",
                exception);
        }
    }
}

/// <summary>The command-specific outcome produced after its provider writes have been staged.</summary>
public sealed record GroundworkDesignAtomicWriteStageResult
{
    private GroundworkDesignAtomicWriteStageResult(
        bool accepted,
        string? authoritativeResultFingerprint,
        string? authoritativeResultJson)
    {
        IsAccepted = accepted;
        AuthoritativeResultFingerprint = authoritativeResultFingerprint;
        AuthoritativeResultJson = authoritativeResultJson;
    }

    public bool IsAccepted { get; }
    public string? AuthoritativeResultFingerprint { get; }
    public string? AuthoritativeResultJson { get; }

    public static GroundworkDesignAtomicWriteStageResult Accepted(
        string authoritativeResultFingerprint,
        string authoritativeResultJson) =>
        new(true, authoritativeResultFingerprint, authoritativeResultJson);

    public static GroundworkDesignAtomicWriteStageResult Rejected() => new(false, null, null);
}

public enum GroundworkDesignAtomicWriteStatus
{
    /// <summary>The current attempt committed and received a definitive provider acknowledgement.</summary>
    Committed,

    /// <summary>
    /// The current attempt received an uncertain acknowledgement, then confirmed its marker. Callers may
    /// perform the same post-commit outcome publication they perform for <see cref="Committed"/>.
    /// </summary>
    Reconciled,

    /// <summary>A marker from an earlier or competing attempt supplied the authoritative result.</summary>
    Replayed,

    /// <summary>The provider rejected a staged decision and rolled the complete unit of work back.</summary>
    Rejected,

    /// <summary>The operation key already belongs to different canonical mutation material.</summary>
    Conflict
}

/// <summary>The terminal classification and authoritative result of one design mutation attempt.</summary>
public sealed record GroundworkDesignAtomicWriteResult
{
    private GroundworkDesignAtomicWriteResult(
        GroundworkDesignAtomicWriteStatus status,
        string? authoritativeResultFingerprint,
        string? authoritativeResultJson)
    {
        Status = status;
        AuthoritativeResultFingerprint = authoritativeResultFingerprint;
        AuthoritativeResultJson = authoritativeResultJson;
    }

    public GroundworkDesignAtomicWriteStatus Status { get; }
    public string? AuthoritativeResultFingerprint { get; }
    public string? AuthoritativeResultJson { get; }

    internal static GroundworkDesignAtomicWriteResult Committed(string fingerprint, string json) =>
        new(GroundworkDesignAtomicWriteStatus.Committed, fingerprint, json);

    internal static GroundworkDesignAtomicWriteResult Replayed(string fingerprint, string json) =>
        new(GroundworkDesignAtomicWriteStatus.Replayed, fingerprint, json);

    internal static GroundworkDesignAtomicWriteResult Reconciled(string fingerprint, string json) =>
        new(GroundworkDesignAtomicWriteStatus.Reconciled, fingerprint, json);

    internal static GroundworkDesignAtomicWriteResult Rejected() =>
        new(GroundworkDesignAtomicWriteStatus.Rejected, null, null);

    internal static GroundworkDesignAtomicWriteResult Conflict() =>
        new(GroundworkDesignAtomicWriteStatus.Conflict, null, null);
}

public sealed class GroundworkDesignAtomicWriteReadinessException(string message)
    : InvalidOperationException(message);

/// <summary>Classifies a raw Groundwork operation failure so a design adapter can map it without a catch-all.</summary>
public sealed class GroundworkDesignAtomicWriteProviderFailureException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);

public sealed class GroundworkDesignAtomicWriteUncertainCommitException(
    string message,
    Exception? innerException = null)
    : InvalidOperationException(message, innerException);

public sealed class GroundworkDesignAtomicWriteCorruptMarkerException(
    string message,
    string markerId,
    Exception innerException)
    : InvalidOperationException(message, innerException)
{
    public string MarkerId { get; } = markerId;
}
