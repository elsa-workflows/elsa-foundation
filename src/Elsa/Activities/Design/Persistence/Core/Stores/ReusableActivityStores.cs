using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;

namespace Elsa.Activities.Design.Persistence.Core.Stores;

/// <summary>Reads Groundwork-targeted authoring facts associated with catalog definitions.</summary>
public interface IActivityDefinitionAuthoringStore
{
    Task<ActivityDefinitionAuthoringState?> FindAsync(
        string definitionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityDefinitionAuthoringState>> ListAsync(
        IEnumerable<string> definitionIds,
        CancellationToken cancellationToken = default);
}

public interface IActivityDefinitionDraftStore
{
    Task<ActivityDefinitionDraft?> FindAsync(
        string draftId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityDefinitionDraft>> ListByDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default);
}

public interface IActivityDefinitionVersionPublicationStore
{
    Task<ActivityDefinitionVersionPublication?> FindAsync(
        string definitionVersionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default);
}

public sealed record RecommendedActivityDefinitionPickerItem(
    ActivityDefinition Definition,
    ActivityDefinitionVersionPublication Version);

public sealed record RecommendedActivityDefinitionPickerPage(
    IReadOnlyList<RecommendedActivityDefinitionPickerItem> Items,
    int? NextOffset);

public interface IRecommendedActivityDefinitionPickerStore
{
    Task<RecommendedActivityDefinitionPickerPage> ReadAsync(
        string? tenantId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);
}

public interface IActivityDefinitionLayoutStore
{
    Task<ActivityDefinitionDraftLayout?> FindDraftLayoutAsync(
        string draftId,
        CancellationToken cancellationToken = default);

    Task<ActivityDefinitionVersionLayout?> FindVersionLayoutAsync(
        string definitionVersionId,
        CancellationToken cancellationToken = default);
}

public interface IActivityDraftValidationStore
{
    Task<ActivityDraftValidationState?> FindAsync(
        string draftId,
        long revision,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads authoritative immutable outbound dependency facts. Reverse, transitive, and current-draft
/// usage projections are deliberately separate because they are rebuildable and carry a watermark.
/// </summary>
public interface IActivityDirectDependencyStore
{
    Task<IReadOnlyList<ActivityDependencyEdge>> ListOutboundAsync(
        string ownerVersionId,
        CancellationToken cancellationToken = default);
}

public interface IActivityDependencyProjectionStore
{
    /// <summary>
    /// Reads the aggregate rebuildable projection. Items may be owned by activity/workflow versions
    /// or drafts; concrete source population is independent of this provider-neutral read contract.
    /// </summary>
    Task<ActivityDependencyProjectionSlice> ReadAsync(
        ActivityDependencyProjectionReadRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ActivityDependencyWatermarkExpiredException(string watermark)
    : Exception("The requested activity dependency projection watermark is no longer available.")
{
    public string Watermark { get; } = watermark;
}

public interface IActivityUpgradePlanStore
{
    Task<ActivityUpgradePlan?> FindAsync(
        string planId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        ActivityUpgradePlan plan,
        CancellationToken cancellationToken = default);
}

public interface IActivityDependencyProjectionRebuilder
{
    Task RebuildAsync(
        ActivityDependencyProjectionRebuild rebuild,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the complete derived snapshot at the next provider-owned sequence. This is the
    /// recovery/convergence path for source mutations that intentionally do not synchronously couple
    /// their transaction to Activity Design.
    /// </summary>
    Task<ActivityDependencyProjectionRebuild> RebuildCurrentAsync(
        string rebuildId,
        DateTimeOffset asOf,
        IReadOnlyList<ActivityDependencyItem> items,
        CancellationToken cancellationToken = default);
}
