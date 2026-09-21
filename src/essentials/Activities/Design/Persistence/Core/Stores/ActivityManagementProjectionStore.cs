using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;

namespace Elsa.Activities.Design.Persistence.Core.Stores;

public sealed record ActivityManagementSnapshot(long Sequence, DateTimeOffset AsOf);

public sealed record ActivityManagementProjectionPageQuery(
    string? TenantId,
    long? SnapshotSequence,
    int Offset,
    int Limit,
    string? Search = null,
    ActivityContentAuthorityKind? Authority = null,
    string? ProviderKey = null,
    ActivityDefinitionDraftStatus? DraftStatus = null,
    ActivityDefinitionVersionLifecycle? VersionLifecycle = null);

public sealed record ActivityManagementProjectionPage<T>(
    IReadOnlyList<T> Items,
    int? NextOffset,
    long TotalCount,
    ActivityManagementSnapshot Snapshot);

public sealed class ActivityManagementSnapshotExpiredException(long sequence)
    : Exception("The requested activity-management snapshot is no longer retained.")
{
    public long Sequence { get; } = sequence;
}

/// <summary>
/// Reads stable disclosure-safe activity-management snapshots. Every predicate, ordering rule,
/// page window and exact count is executed by the admitted persistence provider.
/// </summary>
public interface IActivityDefinitionManagementProjectionStore
{
    Task<ActivityManagementSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken = default);

    Task<ActivityManagementProjectionPage<ActivityDefinitionManagementProjectionRevision>> ReadDefinitionsAsync(
        ActivityManagementProjectionPageQuery query,
        CancellationToken cancellationToken = default);

    Task<ActivityDefinitionManagementProjectionRevision?> FindDefinitionAsync(
        string definitionId,
        string? tenantId,
        long? snapshotSequence = null,
        CancellationToken cancellationToken = default);

    Task<ActivityManagementProjectionPage<ActivityDefinitionDraftManagementProjectionRevision>> ReadDraftsAsync(
        string definitionId,
        ActivityManagementProjectionPageQuery query,
        CancellationToken cancellationToken = default);

    Task<ActivityManagementProjectionPage<ActivityDefinitionVersionManagementProjectionRevision>> ReadVersionsAsync(
        string definitionId,
        ActivityManagementProjectionPageQuery query,
        CancellationToken cancellationToken = default);
}
