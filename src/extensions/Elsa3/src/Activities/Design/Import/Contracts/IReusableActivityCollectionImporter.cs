using Elsa3.Activities.Design.Import.Models;

namespace Elsa3.Activities.Design.Import.Contracts;

/// <summary>
/// Elsa 3 collection migration boundary. Analysis is side-effect free; application re-analyzes the
/// supplied collection and commits one dependency-closed mutation under the observed plan identity.
/// </summary>
public interface IReusableActivityCollectionImporter
{
    ValueTask<ReusableActivityImportPlan> AnalyzeAsync(
        ReusableActivityImportCollection collection,
        CancellationToken cancellationToken = default);

    ValueTask<ReusableActivityImportApplyResult> ApplyAsync(
        ReusableActivityImportApplyRequest request,
        CancellationToken cancellationToken = default);
}
public interface IReusableActivityCollectionAnalyzer
{
    ValueTask<ReusableActivityImportPlan> AnalyzeAsync(
        ReusableActivityImportCollection collection,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Design-side mapping seam implemented by Elsa3.Mapping. It converts a reviewed collection plan
/// into provider-neutral Activity/Workflow Design mutations without giving persistence an Elsa 3
/// mapping dependency.
/// </summary>
public interface IReusableActivityImportMaterializer
{
    ValueTask<ReusableActivityImportMutation> MaterializeAsync(
        ReusableActivityImportCollection collection,
        ReusableActivityImportPlan plan,
        IReadOnlyList<ReusableActivityImportItem> selection,
        CancellationToken cancellationToken = default);
}

/// <summary>The one atomic persistence port for a selected Elsa 3 collection closure.</summary>
public interface IReusableActivityImportCommand
{
    ValueTask<ReusableActivityImportCommitResult> CommitAsync(
        ReusableActivityImportMutation mutation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Durable, immutable storage for bounded Elsa 3 collection uploads and completed import receipts.
/// Collections, receipts, and their idempotency-key namespace belong to the exact tenant-plus-user
/// operation scope. Implementations must apply that scope before returning either resource.
/// </summary>
/// <remarks>
/// A stored row is never rewritten. A collection upload holds the uploaded document verbatim, literal credentials
/// included, so it is deleted rather than kept: the two delete operations remove rows and decide nothing. When an
/// upload is deleted is the operation service's decision (<see cref="IReusableActivityImportOperationService"/>).
/// </remarks>
public interface IReusableActivityImportOperationStore
{
    ValueTask<bool> TryCreateCollectionAsync(
        ReusableActivityImportCollectionHandle collection,
        CancellationToken cancellationToken = default);

    ValueTask<ReusableActivityImportCollectionHandle?> FindCollectionAsync(
        string handle,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one collection upload in its exact tenant-plus-user scope and reports whether a row was deleted.
    /// Deleting an absent collection is not an error.
    /// </summary>
    ValueTask<bool> DeleteCollectionAsync(
        string handle,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes at most <paramref name="maxCount"/> collection uploads of the ambient persistence scope, every
    /// user's, whose expiry is at or before <paramref name="expiresAtOrBefore"/>, oldest expiry first, and returns
    /// how many it deleted.
    /// </summary>
    ValueTask<int> DeleteExpiredCollectionsAsync(
        DateTimeOffset expiresAtOrBefore,
        int maxCount,
        CancellationToken cancellationToken = default);

    ValueTask<ReusableActivityImportReceipt?> FindReceiptAsync(
        string idempotencyKey,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);
}

public interface IReusableActivityImportOperationService
{
    /// <summary>
    /// Reads, validates, and durably stores one bounded immutable Elsa 3 collection. The stored upload lives until
    /// its apply is decided or its lifetime runs out, whichever comes first, and is then deleted.
    /// </summary>
    /// <exception cref="ReusableActivityImportPayloadException">The stream cannot be read or does not contain a supported bounded collection.</exception>
    /// <exception cref="ReusableActivityImportPersistenceException">The immutable collection cannot be stored.</exception>
    ValueTask<ReusableActivityImportUploadResult> UploadAsync(
        Stream json,
        long? contentLength,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one deterministic analysis page for a scoped immutable collection.</summary>
    /// <exception cref="ReusableActivityImportNotFoundException">The collection does not exist in the current access scope.</exception>
    /// <exception cref="ReusableActivityImportExpiredException">The immutable collection has expired; the read that finds it expired deletes it.</exception>
    /// <exception cref="ReusableActivityImportPersistenceException">The collection cannot be loaded.</exception>
    ValueTask<ReusableActivityImportAnalysisPage> AnalyzeAsync(
        string collectionHandle,
        int offset,
        int limit,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);

    /// <summary>Expands a reviewed selection to its exact reusable dependency closure.</summary>
    /// <exception cref="ReusableActivityImportNotFoundException">The collection does not exist in the current access scope.</exception>
    /// <exception cref="ReusableActivityImportExpiredException">The immutable collection has expired; the read that finds it expired deletes it.</exception>
    /// <exception cref="ReusableActivityImportValidationException">The reviewed plan no longer matches the immutable collection.</exception>
    /// <exception cref="ReusableActivityImportPersistenceException">The collection cannot be loaded.</exception>
    ValueTask<ReusableActivityImportSelectionReadiness> ExpandSelectionAsync(
        string collectionHandle,
        string planId,
        IReadOnlyCollection<string> selectedSourceVersionIds,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically applies one reviewed exact selection and stores its immutable durable receipt.
    /// The idempotency key is unique within the exact tenant-plus-user operation scope; another user
    /// in the same tenant may use the same key for an independent operation over tenant-owned Design resources.
    /// </summary>
    /// <remarks>
    /// A completed apply consumes the upload: the stored collection is deleted, and a further selection from the
    /// same export needs a new upload. A replay of the same idempotency key still answers from the receipt. A
    /// refused apply deletes the upload too, unless its caller can continue with the same upload: a validation,
    /// idempotency conflict, collision or persistence exception below, a schema write refusal and a cancellation
    /// leave the upload in place for a corrected or repeated request. The delete does not observe the caller's
    /// cancellation, and a delete that fails never replaces the outcome: it is logged and the row stays. What deletes
    /// it later, and the one case where nothing does, is set out under "Upload retention" in the import's
    /// <c>EXTENSION_POINTS.md</c>.
    /// </remarks>
    /// <exception cref="ReusableActivityImportNotFoundException">The collection does not exist in the current access scope.</exception>
    /// <exception cref="ReusableActivityImportExpiredException">The immutable collection has expired; the read that finds it expired deletes it.</exception>
    /// <exception cref="ReusableActivityImportValidationException">The plan or selection is not valid and dependency-closed.</exception>
    /// <exception cref="ReusableActivityImportIdempotencyConflictException">The idempotency key is bound to another request.</exception>
    /// <exception cref="ReusableActivityImportCollisionException">A deterministic Design identity is owned by different content.</exception>
    /// <exception cref="ReusableActivityImportPersistenceException">The atomic mutation or receipt cannot be persisted.</exception>
    ValueTask<ReusableActivityImportReceipt> ApplyAsync(
        string collectionHandle,
        string planId,
        IReadOnlyCollection<string> selectedSourceVersionIds,
        string idempotencyKey,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the immutable receipt for the exact tenant-plus-user operation scope.</summary>
    /// <exception cref="ReusableActivityImportNotFoundException">The receipt does not exist in the current access scope.</exception>
    /// <exception cref="ReusableActivityImportPersistenceException">The receipt cannot be loaded.</exception>
    ValueTask<ReusableActivityImportReceipt> GetStatusAsync(
        string idempotencyKey,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);
}
