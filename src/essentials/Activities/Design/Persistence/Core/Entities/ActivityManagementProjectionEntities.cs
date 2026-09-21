using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Entities;
using System.Text.Json;
using System.ComponentModel.DataAnnotations.Schema;

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
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public string ActivityTypeKey { get; init; } = null!;

    public string Category { get; init; } = null!;

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    private ActivityContentAuthority? contentAuthority;

    /// <summary>Decoded authority material. The persisted JSON and database-generated validity
    /// marker are exposed separately so reads remain provider-side and fail closed.</summary>
    [NotMapped]
    public ActivityContentAuthority ContentAuthority
    {
        get
        {
            if (contentAuthority is not null)
                return contentAuthority;
            if (string.IsNullOrWhiteSpace(ContentAuthorityJson))
                return null!;
            try
            {
                return contentAuthority = JsonSerializer.Deserialize<ActivityContentAuthority>(ContentAuthorityJson, Json)!;
            }
            catch (JsonException) { return null!; }
            catch (NotSupportedException) { return null!; }
            catch (InvalidOperationException) { return null!; }
            catch (ArgumentException) { return null!; }
        }
        init => contentAuthority = value;
    }

    /// <summary>Raw authority JSON. This is the storage column retained for compatibility.</summary>
    public string? ContentAuthorityJson { get; private set; }

    /// <summary>Database-generated semantic validity marker for the persisted authority JSON.</summary>
    public bool ContentAuthorityIsValid { get; private set; }

    /// <summary>Canonical authority JSON maintained with <see cref="ContentAuthorityJson"/>.</summary>
    public string? ContentAuthorityCanonicalJson { get; private set; }

    /// <summary>STJ-encoded JSON token for the authoritative key, used by provider-safe integrity predicates.</summary>
    public string? ContentAuthorityAuthorityKeyJson { get; private set; }

    /// <summary>STJ-encoded JSON token for the authoritative source, or the literal null token.</summary>
    public string? ContentAuthoritySourceIdJson { get; private set; }

    /// <summary>Decoded authority key persisted as an unbounded scalar for provider-safe predicates.</summary>
    public string? ContentAuthorityAuthorityKey { get; private set; }

    /// <summary>Decoded source identifier persisted as an unbounded scalar for provider-safe predicates.</summary>
    public string? ContentAuthoritySourceId { get; private set; }

    /// <summary>
    /// Integrity digest over the raw, canonical, token and decoded scalar authority material.
    /// Provider validity predicates compare this digest before allowing a row into a page.
    /// </summary>
    public string? ContentAuthorityIntegrityHash { get; private set; }

    /// <summary>
    /// Provider-safe scalar projection of <see cref="ContentAuthority"/>. Writers must derive this
    /// value from <c>ContentAuthority.Kind</c>; it exists because JSON-converted members are not
    /// translatable by every EF provider.
    /// </summary>
    public ActivityContentAuthorityKind ContentAuthorityKind { get; init; }

    public string? HeadVersionId { get; init; }

    public string? RecommendedVersionId { get; init; }

    public ActivityManagementVersionProjectionReference? Head { get; init; }

    public ActivityManagementVersionProjectionReference? Recommendation { get; init; }

    public string? HeadProviderKey { get; init; }

    public string? RecommendationProviderKey { get; init; }

    public long DraftCount { get; set; }

    public long VersionCount { get; set; }

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
