using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

/// <summary>
/// Reads the activity picker from the stable management projection. The projection owns the
/// non-null definition-identity ordering required for an unfiltered bounded page; the underlying
/// authoring index remains available for equality/IN lookups without changing its applied schema.
/// </summary>
public sealed class GroundworkRecommendedActivityDefinitionPickerStore(
    IActivityDefinitionManagementProjectionStore managementProjections,
    IActivityDefinitionVersionPublicationStore publications) : IRecommendedActivityDefinitionPickerStore
{
    public async Task<RecommendedActivityDefinitionPickerPage> ReadAsync(
        string? tenantId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var items = new List<RecommendedActivityDefinitionPickerItem>(limit);
        var sourceOffset = offset;
        long totalCount = offset;
        long? snapshotSequence = null;
        while (items.Count < limit)
        {
            var page = await managementProjections.ReadDefinitionsAsync(
                new(
                    tenantId,
                    snapshotSequence,
                    sourceOffset,
                    Math.Min(100, Math.Max(limit * 2, 20))),
                cancellationToken);
            snapshotSequence ??= page.Snapshot.Sequence;
            totalCount = page.TotalCount;
            if (page.Items.Count == 0)
                break;

            foreach (var projection in page.Items)
            {
                sourceOffset++;
                if (!IsVisible(projection.TenantId, tenantId) || projection.RecommendedVersionId is null)
                    continue;

                var publication = await publications.FindAsync(projection.RecommendedVersionId, cancellationToken);
                if (publication is null ||
                    publication.Lifecycle != ActivityDefinitionVersionLifecycle.Active ||
                    !StringComparer.Ordinal.Equals(publication.DefinitionId, projection.DefinitionId) ||
                    !StringComparer.Ordinal.Equals(publication.TenantId, projection.TenantId))
                    continue;

                items.Add(new(ToDefinition(projection), publication));
                if (items.Count == limit)
                    break;
            }

            if (page.NextOffset is null)
                break;
        }

        return new(items, sourceOffset < totalCount ? sourceOffset : null);
    }

    private static ActivityDefinition ToDefinition(ActivityDefinitionManagementProjectionRevision projection) => new()
    {
        Id = projection.DefinitionId,
        TenantId = projection.TenantId,
        ActivityTypeKey = projection.ActivityTypeKey,
        Category = projection.Category,
        DisplayName = projection.DisplayName,
        Description = projection.Description,
        CreatedAt = projection.CreatedAt,
        LastModifiedAt = projection.UpdatedAt
    };

    private static bool IsVisible(string? itemTenantId, string? tenantId) =>
        itemTenantId is null || StringComparer.Ordinal.Equals(itemTenantId, tenantId);
}
