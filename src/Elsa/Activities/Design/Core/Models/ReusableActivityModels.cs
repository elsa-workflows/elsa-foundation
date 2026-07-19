using System.Text.Json;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Core.Models;

public enum ActivityContentAuthorityKind
{
    Design,
    ProviderSource
}

public enum ActivityDefinitionDraftStatus
{
    Active,
    Published,
    Discarded
}

public enum ActivityDefinitionVersionLifecycle
{
    Active,
    Retired,
    Revoked
}

public enum ActivityDefinitionVersionResolutionKind
{
    Unspecified = 0,
    AuthorableActivity = 1,
    ReusableTemplateBoundary = 2
}

public enum ActivityBoundaryDurability
{
    Required
}

public enum ActivityDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public enum ActivityVersionCompatibility
{
    Identical,
    NonBehavioral,
    Compatible,
    Breaking
}

public enum ActivityVersionBump
{
    None,
    Patch,
    Minor,
    Major
}

public enum ActivityVersionChangeArea
{
    Contract,
    Default,
    Outcome,
    Durability,
    Provider,
    Implementation,
    Dependency,
    Presentation
}

public enum ActivityVersionChangeImpact
{
    NonBehavioral,
    Additive,
    Breaking
}

public enum ActivityDependencyDirection
{
    Outbound,
    Inbound
}

public enum ActivityDependencyConsistencyKind
{
    AuthoritativeDirect,
    DerivedProjection
}

public enum ActivityUpgradePlanStatus
{
    Ready,
    Blocked,
    Applied,
    Expired
}

public enum ActivityUpgradeAction
{
    UpdateDraft,
    CloneActivityVersion,
    CloneWorkflowVersion
}

public sealed record ActivityContentAuthority(
    ActivityContentAuthorityKind Kind,
    string AuthorityKey,
    string? SourceId = null);

public static class WellKnownActivityContentAuthorities
{
    public const string Design = "elsa.activity-design";
}

public sealed record ActivityDefinitionForkOrigin(
    string DefinitionId,
    string VersionId,
    string Version);

public sealed record ReusableActivityDefinition(
    string DefinitionId,
    string ActivityTypeKey,
    string? TenantId,
    string Category,
    string DisplayName,
    string? Description,
    ActivityContentAuthority ContentAuthority,
    ActivityDefinitionForkOrigin? ForkedFrom,
    string? HeadVersionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ActivityInputDefault(string Syntax, JsonElement Value);

public sealed record ActivityInputContract(
    string ReferenceKey,
    string Name,
    TypeReference Type,
    bool IsRequired,
    ActivityInputDefault? Default,
    string StorageDriverKey,
    ActivityBoundaryDurability Durability = ActivityBoundaryDurability.Required,
    string? DisplayName = null,
    string? Description = null,
    string? Category = null,
    float Order = 0,
    string? UiHint = null,
    JsonElement? UiSpecifications = null);

public sealed record ActivityOutputContract(
    string ReferenceKey,
    string Name,
    TypeReference Type,
    bool IsRequired,
    string StorageDriverKey,
    ActivityBoundaryDurability Durability = ActivityBoundaryDurability.Required,
    string? DisplayName = null,
    string? Description = null,
    string? Category = null,
    float Order = 0,
    string? UiHint = null,
    JsonElement? UiSpecifications = null,
    ValueRepresentation? SourceRepresentation = null);

public sealed record ActivityOutcomeContract(
    string ReferenceKey,
    string Name,
    bool IsEmitted,
    string? Description = null);

public sealed record ActivityContract(
    string ContractSchemaVersion,
    IReadOnlyList<ActivityInputContract> Inputs,
    IReadOnlyList<ActivityOutputContract> Outputs,
    IReadOnlyList<ActivityOutcomeContract> Outcomes);

public sealed record ActivityProviderManifest(
    string ProviderKey,
    string SchemaVersion,
    JsonElement Payload);

public sealed record ActivityDefinitionDraftState(
    ActivityContract Contract,
    ActivityProviderManifest Provider,
    IReadOnlyDictionary<string, string> Options);

public sealed record ActivityLayoutRecord(string NodeId, JsonElement Data);

public sealed record ReusableActivityDraft(
    string DraftId,
    string DefinitionId,
    string? TenantId,
    long Revision,
    string? SourceVersionId,
    ActivityDefinitionDraftStatus Status,
    ActivityDefinitionDraftState State,
    IReadOnlyList<ActivityLayoutRecord> Layout,
    ActivityDraftValidation? Validation,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ReusableActivityVersion(
    string VersionId,
    string DefinitionId,
    string? TenantId,
    string Version,
    string? SourceDraftId,
    string? SourceVersionId,
    ActivityContract Contract,
    ActivityProviderManifest Provider,
    string TemplateId,
    string TemplateHash,
    string SourceReferenceId,
    string ProviderFingerprint,
    ActivityDefinitionVersionLifecycle Lifecycle,
    DateTimeOffset PublishedAt);

public sealed record ActivityDiagnosticSubject(
    string Kind,
    string Id,
    string? DefinitionId = null,
    string? VersionId = null,
    long? Revision = null);

public sealed record ActivityDependencyPathItem(
    string DefinitionId,
    string VersionId,
    string Version,
    string TemplateHash);

public sealed record ActivityNodeOrigin(string Kind, string Id);

public sealed record ActivityDiagnosticLocation(
    string? ProviderKey = null,
    string? JsonPointer = null,
    string? ReferenceKey = null,
    IReadOnlyList<ActivityNodeOrigin>? NodeOrigin = null,
    IReadOnlyList<ActivityDependencyPathItem>? DependencyPath = null);

public sealed record ActivityDiagnostic(
    string Code,
    ActivityDiagnosticSeverity Severity,
    string Message,
    ActivityDiagnosticSubject Subject,
    ActivityDiagnosticLocation? Location = null,
    string? Remediation = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        Metadata ?? new Dictionary<string, string>();
}

public sealed record ActivityDraftValidation(
    string DraftId,
    long Revision,
    bool IsValid,
    DateTimeOffset ValidatedAt,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed record ActivityVersionIdentity(
    string Kind,
    string DefinitionId,
    string? VersionId = null,
    string? DraftId = null,
    long? Revision = null,
    string? Version = null,
    string? TemplateHash = null);

public sealed record ActivityVersionProviderDiff(
    string? FromKey,
    string? FromSchemaVersion,
    string? ToKey,
    string? ToSchemaVersion,
    bool Changed);

public sealed record ActivityVersionDiffSummary(
    int Breaking,
    int Additive,
    int NonBehavioral,
    int Warnings);

public sealed record ActivityVersionChangeSubject(
    string? MemberKind = null,
    string? ReferenceKey = null,
    string? DependencyVersionId = null,
    string? OccurrenceId = null);

public sealed record ActivityVersionChange(
    string ChangeId,
    ActivityVersionChangeArea Area,
    string Kind,
    ActivityVersionChangeSubject Subject,
    JsonElement? Before,
    JsonElement? After,
    ActivityVersionChangeImpact Impact,
    ActivityVersionBump RequiredBump,
    string Message);

public sealed record ActivityVersionDiff(
    ActivityVersionIdentity From,
    ActivityVersionIdentity To,
    ActivityVersionCompatibility Compatibility,
    ActivityVersionBump RequiredBump,
    bool BehaviorChanged,
    ActivityVersionProviderDiff Provider,
    ActivityVersionDiffSummary Summary,
    IReadOnlyList<ActivityVersionChange> Changes,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed record ActivityVersionImplementationFacts(
    string? ProviderFingerprint = null,
    long? NodeCount = null,
    long? ResumeTargetCount = null,
    string? LayoutHash = null,
    int? LayoutCount = null,
    IReadOnlyList<ActivityRuntimeRequirementDeclaration>? RuntimeRequirements = null);

public sealed record ActivityDraftDiffCandidateRequest(
    string DefinitionId,
    string DraftId,
    long Revision,
    ActivityDefinitionDraftState State,
    IReadOnlyList<ActivityLayoutRecord> Layout,
    string? BaseVersionId);

public sealed record ActivityDraftDiffCandidate(
    string? TemplateHash,
    string? ProviderFingerprint,
    long? NodeCount,
    long? ResumeTargetCount,
    IReadOnlyList<ActivityRuntimeRequirementDeclaration> RuntimeRequirements,
    IReadOnlyList<ActivityDependencyItem> Dependencies,
    IReadOnlyList<ActivityVersionChange> ProviderCompatibilityChanges,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed record ActivityDefinitionReference(
    string Kind,
    string DefinitionId,
    string? VersionId = null,
    string? Version = null,
    string? DraftId = null,
    long? Revision = null,
    string? TemplateHash = null,
    string? TenantId = null,
    ActivityDefinitionVersionLifecycle? Lifecycle = null);

public sealed record ActivityDependencyOccurrence(
    string OccurrenceId,
    IReadOnlyList<ActivityNodeOrigin> NodeOrigin);

public sealed record ActivityDependencyItem(
    string RelationshipId,
    ActivityDefinitionReference Owner,
    ActivityDefinitionReference Dependency,
    ActivityDependencyOccurrence Occurrence,
    bool IsDirect,
    int Depth,
    IReadOnlyList<ActivityDefinitionReference> Path);

public sealed record ActivityDependencyQuery(
    ActivityDependencyDirection Direction,
    bool Transitive,
    IReadOnlySet<string> Include);

public sealed record ActivityDependencyConsistency(
    ActivityDependencyConsistencyKind Kind,
    bool IsAuthoritative,
    long? AsOfSequence,
    DateTimeOffset? AsOf,
    string? RebuildId = null);

public sealed record ActivityDependencyPage(
    ActivityDefinitionReference Root,
    ActivityDependencyQuery Query,
    ActivityDependencyConsistency Consistency,
    IReadOnlyList<ActivityDependencyItem> Items,
    string? NextCursor);

public sealed record ActivityDependencyProjectionReadRequest(
    string RootVersionId,
    ActivityDependencyQuery Query,
    string? TenantId,
    string? Watermark,
    int Offset,
    int Limit);

public sealed record ActivityDependencyProjectionSlice(
    ActivityDefinitionReference Root,
    ActivityDependencyConsistency Consistency,
    IReadOnlyList<ActivityDependencyItem> Items,
    string Watermark,
    int? NextOffset);

public sealed record ActivityVersionReplacement(string FromVersionId, string ToVersionId);

public sealed record ActivityUpgradeRoot(string Kind, string Id);

public sealed record ActivityUpgradePlanRequest(
    IReadOnlyList<ActivityVersionReplacement> Replacements,
    IReadOnlyList<ActivityUpgradeRoot> Roots,
    bool IncludeTransitiveDependents,
    bool CreateDraftsForPublishedDependents,
    string? TenantId = null);

/// <summary>
/// One exact owner snapshot returned by the authoritative upgrade-discovery seam. The planner only
/// consumes provider-neutral identity, concurrency and occurrence facts; it never reads workflow
/// state or provider manifests directly.
/// </summary>
public sealed record ActivityUpgradeOwnerSnapshot(
    ActivityDefinitionReference Owner,
    string? DefinitionHeadVersionId,
    string? SourceVersionId,
    IReadOnlyList<ActivityUpgradeOccurrenceReplacement> DirectReplacements,
    IReadOnlyList<ActivityDefinitionReference> DependencyPath,
    bool RequiresPublishedChildVersion = false,
    string? RequiredChildStepId = null,
    ActivityDraftDiffCandidateRequest? DiffCandidate = null);

public sealed record ActivityUpgradeDiscovery(
    IReadOnlyList<ActivityUpgradeOwnerSnapshot> Owners,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed record ActivityUpgradeExpectedSnapshot(
    string Kind,
    string Id,
    long? Revision,
    string DefinitionId,
    string? HeadVersionId);

public sealed record ActivityUpgradeTarget(
    string Kind,
    string DefinitionId,
    string? DraftId,
    long? Revision,
    string? SourceVersionId = null);

public sealed record ActivityUpgradeOccurrenceReplacement(
    string OccurrenceId,
    string FromVersionId,
    string ToVersionId);

public sealed record ActivityUpgradeStep(
    string StepId,
    int Order,
    ActivityUpgradeTarget Target,
    ActivityUpgradeAction Action,
    IReadOnlyList<string> DependsOnStepIds,
    IReadOnlyList<ActivityUpgradeOccurrenceReplacement> Replacements,
    long? ExpectedRevision,
    string? ExpectedDefinitionHeadVersionId,
    ActivityVersionDiff? ResultingDiff,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed record ActivityUpgradePlan(
    string PlanId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    ActivityUpgradePlanStatus Status,
    IReadOnlyList<ActivityVersionReplacement> Replacements,
    IReadOnlyList<ActivityUpgradeExpectedSnapshot> ExpectedSnapshots,
    IReadOnlyList<ActivityUpgradeStep> Steps,
    IReadOnlyList<ActivityDiagnostic> Diagnostics,
    DateTimeOffset? AppliedAt = null,
    IReadOnlyList<ActivityUpgradeAppliedDraft>? AppliedDrafts = null,
    string? TenantId = null);

public sealed record ActivityUpgradeApplyRequest(
    string PlanId,
    IReadOnlyList<string>? SelectedStepIds = null);

public sealed record ActivityUpgradeAppliedDraft(
    string Kind,
    string DraftId,
    string DefinitionId,
    long Revision,
    bool Created);

public sealed record ActivityUpgradeApplyResult(
    string PlanId,
    ActivityUpgradePlanStatus Status,
    DateTimeOffset AppliedAt,
    IReadOnlyList<ActivityUpgradeAppliedDraft> Drafts,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed class ActivityUpgradeApplyException(
    int statusCode,
    string errorCode,
    string message,
    IReadOnlyList<ActivityDiagnostic>? diagnostics = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
    public IReadOnlyList<ActivityDiagnostic> Diagnostics { get; } = diagnostics ?? [];
}

public sealed record ActivityDependencyProjectionRebuild(
    string RebuildId,
    long Sequence,
    DateTimeOffset AsOf,
    IReadOnlyList<ActivityDependencyItem> Items);

public sealed record ActivityResolvedDependency(
    string DefinitionId,
    string VersionId,
    string Version,
    string TemplateId,
    string TemplateHash,
    ActivityContract Contract,
    ActivityDefinitionVersionLifecycle Lifecycle,
    string? TenantId,
    string OccurrenceId,
    IReadOnlyList<ActivityNodeOrigin> NodeOrigin,
    string? ParentOccurrenceId = null,
    string ChildSlotName = "activity-graph",
    int ChildIndex = 0);

public sealed record ActivityRuntimeRequirementDeclaration(string ConsumerKey, string SchemaVersion);

public sealed record ActivityResourceMeasurements(
    long LocalNodeCount,
    long ClosedNodeCount,
    long DependencyCount,
    long MaximumObservedAuthoredDepth,
    long DescriptorBytes,
    long LayoutBytes,
    long EstimatedDurableBoundarySlots);

public sealed record ActivityContractProposal(
    ActivityContract Contract,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed record ActivityManifestMigrationRequest(
    ActivityProviderManifest Source,
    string TargetSchemaVersion);

public sealed record ActivityManifestMigration(
    ActivityProviderManifest? Manifest,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed record ActivityDraftValidationRequest(
    string DefinitionId,
    string DraftId,
    long Revision,
    ActivityDefinitionDraftState State);

public sealed record ActivityVersionDiffRequest(
    ActivityVersionIdentity From,
    ActivityVersionIdentity To,
    ActivityContract FromContract,
    ActivityContract ToContract,
    ActivityProviderManifest FromProvider,
    ActivityProviderManifest ToProvider,
    IReadOnlyList<ActivityDependencyItem> FromDependencies,
    IReadOnlyList<ActivityDependencyItem> ToDependencies,
    IReadOnlyList<ActivityVersionChange>? ProviderCompatibilityChanges = null,
    ActivityVersionImplementationFacts? FromImplementation = null,
    ActivityVersionImplementationFacts? ToImplementation = null,
    bool CompareDependencies = true,
    IReadOnlyList<ActivityDiagnostic>? Diagnostics = null);

public sealed record ActivityAdmissionContext(string? TenantId, string Operation);

public sealed record ActivityAdmissionDecision(
    bool IsAccepted,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);
