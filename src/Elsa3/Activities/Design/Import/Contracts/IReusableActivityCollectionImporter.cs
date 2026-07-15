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
