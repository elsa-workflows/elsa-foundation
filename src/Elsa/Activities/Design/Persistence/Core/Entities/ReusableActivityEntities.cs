using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Entities;

namespace Elsa.Activities.Design.Persistence.Core.Entities;

/// <summary>
/// Groundwork-targeted sibling carrying reusable-authoring facts for the existing catalog definition.
/// Keeping these facts separate prevents the feature from silently expanding the legacy EF schema.
/// </summary>
public sealed class ActivityDefinitionAuthoringState : TenantEntity
{
    public string DefinitionId { get; init; } = null!;

    public ActivityContentAuthority ContentAuthority { get; init; } = null!;

    public ActivityDefinitionForkOrigin? ForkedFrom { get; init; }

    public string? HeadVersionId { get; set; }

    /// <summary>
    /// Exact immutable version offered for new direct selection. This is an explicit product decision,
    /// independent of publication head and version ordering.
    /// </summary>
    public string? RecommendedVersionId { get; set; }
}

public sealed class ActivityDefinitionDraft : TenantEntity
{
    public string DefinitionId { get; init; } = null!;

    public long Revision { get; set; }

    public string? SourceVersionId { get; init; }

    public ActivityDefinitionDraftStatus Status { get; set; } = ActivityDefinitionDraftStatus.Active;

    public ActivityDefinitionDraftState State { get; set; } = null!;

    public string? PublishedVersionId { get; set; }

    /// <summary>Optional non-unique author-facing label kept outside behavior state.</summary>
    public string? PresentationLabel { get; set; }
}

public sealed class ActivityDefinitionDraftLayout : TenantEntity
{
    public string DraftId { get; init; } = null!;

    public long Revision { get; set; }

    public ICollection<ActivityLayoutRecord> Records { get; set; } = [];
}

public sealed class ActivityDraftValidationState : TenantEntity
{
    public string DraftId { get; init; } = null!;

    public long Revision { get; init; }

    public DateTimeOffset ValidatedAt { get; init; }

    public ICollection<ActivityDiagnostic> Diagnostics { get; init; } = [];

    public bool IsValid => Diagnostics.All(x => x.Severity != ActivityDiagnosticSeverity.Error);
}

/// <summary>
/// Immutable reusable-publication facts associated with the legacy catalog version row. The catalog
/// row remains available to existing readers while new publication consumers use this closed snapshot.
/// </summary>
public sealed class ActivityDefinitionVersionPublication : TenantEntity
{
    public string DefinitionVersionId { get; init; } = null!;

    public string DefinitionId { get; init; } = null!;

    public string Version { get; init; } = null!;

    public string ActivityTypeKey { get; init; } = null!;

    public string? SourceDraftId { get; init; }

    public string? SourceVersionId { get; init; }

    public ActivityContract Contract { get; init; } = null!;

    public ActivityProviderManifest Provider { get; init; } = null!;

    public string TemplateId { get; init; } = null!;

    public string TemplateHash { get; init; } = null!;

    public string SourceReferenceId { get; init; } = null!;

    public string ProviderFingerprint { get; init; } = null!;

    public required int DirectDependencyCount { get; init; }

    public required int ClosedTemplateCount { get; init; }

    public required ICollection<ActivityRuntimeRequirementDeclaration> RuntimeRequirements { get; init; }

    public ActivityResourceMeasurements ResourceMeasurements { get; init; } = new(0, 0, 0, 0, 0, 0, 0);

    public int ResumeTargetCount { get; init; }

    public ActivityDefinitionVersionLifecycle Lifecycle { get; set; } = ActivityDefinitionVersionLifecycle.Active;

    public DateTimeOffset PublishedAt { get; init; }
}

public sealed class ActivityDefinitionVersionLayout : TenantEntity
{
    public string DefinitionVersionId { get; init; } = null!;

    public ICollection<ActivityLayoutRecord> Records { get; init; } = [];
}

/// <summary>Authoritative direct dependency fact emitted by one successful publication.</summary>
public sealed class ActivityDependencyEdge : TenantEntity
{
    public string OwnerVersionId { get; init; } = null!;

    public string OwnerTemplateHash { get; init; } = null!;

    public string DependencyVersionId { get; init; } = null!;

    public string DependencyTemplateHash { get; init; } = null!;

    public string OccurrenceId { get; init; } = null!;

    public string? ParentOccurrenceId { get; init; }

    public string ChildSlotName { get; init; } = "activity-graph";

    public int ChildIndex { get; init; }

    public ICollection<ActivityNodeOrigin> NodeOrigin { get; init; } = [];
}

/// <summary>Single rebuildable mixed-owner reverse dependency snapshot.</summary>
public sealed class ActivityDependencyProjectionState : TenantEntity
{
    public const string CurrentId = "current";

    public string RebuildId { get; set; } = null!;

    public long Sequence { get; set; }

    public DateTimeOffset AsOf { get; set; }

    public ICollection<ActivityDependencyItem> Items { get; set; } = [];
}
