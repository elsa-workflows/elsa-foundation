using System.Collections.Immutable;

namespace Elsa.Modularity.Planning.Models;

public sealed record SelectionReason(
    string FeatureId,
    string Action,
    string SourceKind,
    string SourceId,
    string? SourceVersion,
    string Rationale);

public sealed record SelectionFinding(
    string Code,
    string Severity,
    string? FeatureId,
    string? DependencyId,
    string EvidenceSource,
    string Explanation);

public sealed record DependencyEvidence(
    string FeatureId,
    string DependencyId,
    string Mode,
    string EvidenceKind,
    bool TargetSelected)
{
    public string EvidenceSource { get; init; } = string.Empty;
}

public sealed record SelectionPlan(
    CatalogPin Catalog,
    string? InventoryId,
    string? TargetId,
    DateTimeOffset? InventoryObservedAt,
    ImmutableArray<string> SelectedFeatureIds,
    ImmutableArray<SelectionReason> Reasons,
    ImmutableArray<SelectionFinding> Findings,
    ImmutableArray<FeatureLock> ObservedLocks,
    AcceptedSelection Accepted,
    PersistenceEvidence Persistence)
{
    public ImmutableArray<DependencyEvidence> DependencyEvidence { get; init; } = [];
}

public sealed record SelectionDifference<T>(T? Old, T? Candidate);

public sealed record SelectionInputs(
    SelectionCatalog Catalog,
    AuthoredComposition Authored,
    HostInventory? Inventory = null,
    IReadOnlyCollection<WorkspaceProfile>? WorkspaceProfiles = null,
    PersistenceEvidence? Persistence = null);

public sealed record ReresolutionDiff(
    SelectionPlan OldPlan,
    SelectionPlan CandidatePlan,
    ImmutableArray<string> AddedFeatureIds,
    ImmutableArray<string> RemovedFeatureIds,
    ImmutableArray<SelectionDifference<SelectionReason>> ChangedReasons,
    ImmutableArray<SelectionDifference<DependencyExplanation>> ChangedReviewedExplanations,
    ImmutableArray<SelectionDifference<SelectionFinding>> ChangedDependencyFindings,
    ImmutableArray<SelectionDifference<FeatureLock>> ChangedLocks,
    string SettingsResourceImpact);
