using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Entities;

namespace Elsa.Activities.Design.Persistence.Core.Entities;

/// <summary>Durable high-water mark for one coherent activity-management snapshot.</summary>
public sealed class ActivityManagementProjectionWatermark : TenantEntity
{
    public const string CurrentId = "current";

    public long Sequence { get; set; }

    public long RetainedFromSequence { get; set; }

    public DateTimeOffset AdvancedAt { get; set; }
}

/// <summary>Immutable timestamp marker for a retained management snapshot sequence.</summary>
public sealed class ActivityManagementProjectionSnapshot : TenantEntity
{
    public long Sequence { get; init; }

    public DateTimeOffset AsOf { get; init; }
}

public abstract class ActivityManagementProjectionRevision : TenantEntity
{
    public string ResourceId { get; init; } = null!;

    public string DefinitionId { get; init; } = null!;

    public long ValidFromSequence { get; init; }

    public long ValidToSequenceExclusive { get; set; } = long.MaxValue;

    public string ValidFromKey { get; init; } = null!;

    public string ValidToKey { get; set; } = null!;

    public string VisibilityKey { get; init; } = null!;

    public string SortKey { get; init; } = null!;

    public string SearchText { get; init; } = null!;
}

/// <summary>Disclosure-safe temporal definition summary. It intentionally excludes contracts, provider payloads, layouts and diagnostics.</summary>
public sealed class ActivityDefinitionManagementProjectionRevision : ActivityManagementProjectionRevision
{
    public string ActivityTypeKey { get; init; } = null!;

    public string Category { get; init; } = null!;

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public ActivityContentAuthority ContentAuthority { get; init; } = null!;

    public string? HeadVersionId { get; init; }

    public string? RecommendedVersionId { get; init; }

    public ActivityManagementVersionProjectionReference? Head { get; init; }

    public ActivityManagementVersionProjectionReference? Recommendation { get; init; }

    public string? HeadProviderKey { get; init; }

    public string? RecommendationProviderKey { get; init; }

    public long DraftCount { get; init; }

    public long VersionCount { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Disclosure-safe temporal draft summary.</summary>
public sealed class ActivityDefinitionDraftManagementProjectionRevision : ActivityManagementProjectionRevision
{
    public string DraftId { get; init; } = null!;

    public long Revision { get; init; }

    public string? SourceVersionId { get; init; }

    public ActivityDefinitionDraftStatus Status { get; init; }

    public string ProviderKey { get; init; } = null!;

    public string ProviderSchemaVersion { get; init; } = null!;

    public string? PresentationLabel { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Disclosure-safe temporal immutable-version summary.</summary>
public sealed class ActivityDefinitionVersionManagementProjectionRevision : ActivityManagementProjectionRevision
{
    public string DefinitionVersionId { get; init; } = null!;

    public string Version { get; init; } = null!;

    public ActivityDefinitionVersionLifecycle Lifecycle { get; init; }

    public string ProviderKey { get; init; } = null!;

    public string ProviderSchemaVersion { get; init; } = null!;

    public DateTimeOffset PublishedAt { get; init; }
}

public sealed record ActivityManagementVersionProjectionReference(
    string DefinitionVersionId,
    string Version,
    ActivityDefinitionVersionLifecycle Lifecycle,
    string ProviderKey,
    string ProviderSchemaVersion);
