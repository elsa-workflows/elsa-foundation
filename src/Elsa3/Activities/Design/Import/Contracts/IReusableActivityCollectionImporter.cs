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
/// Implementations must apply access-scope checks before returning either resource.
/// </summary>
public interface IReusableActivityImportOperationStore
{
    ValueTask<bool> TryCreateCollectionAsync(
        ReusableActivityImportCollectionHandle collection,
        CancellationToken cancellationToken = default);

    ValueTask<ReusableActivityImportCollectionHandle?> FindCollectionAsync(
        string handle,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);

    ValueTask<ReusableActivityImportReceipt?> FindReceiptAsync(
        string idempotencyKey,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);
}

public interface IReusableActivityImportOperationService
{
    ValueTask<ReusableActivityImportUploadResult> UploadAsync(
        Stream json,
        long? contentLength,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);

    ValueTask<ReusableActivityImportAnalysisPage> AnalyzeAsync(
        string collectionHandle,
        int offset,
        int limit,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);

    ValueTask<ReusableActivityImportSelectionReadiness> ExpandSelectionAsync(
        string collectionHandle,
        string planId,
        IReadOnlyCollection<string> selectedSourceVersionIds,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);

    ValueTask<ReusableActivityImportReceipt> ApplyAsync(
        string collectionHandle,
        string planId,
        IReadOnlyCollection<string> selectedSourceVersionIds,
        string idempotencyKey,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);

    ValueTask<ReusableActivityImportReceipt> GetStatusAsync(
        string idempotencyKey,
        ReusableActivityImportAccessScope accessScope,
        CancellationToken cancellationToken = default);
}
