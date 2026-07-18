using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityTypeReferenceView(string Alias, string CollectionKind);

public sealed record ActivityInputDefaultView(string Syntax, JsonElement Value);

public sealed record ActivityInputContractView(
    string ReferenceKey,
    string Name,
    ActivityTypeReferenceView Type,
    bool IsRequired,
    [property: JsonRequired] bool IsNullable,
    ActivityInputDefaultView? Default,
    string StorageDriverKey,
    ActivityBoundaryDurability Durability = ActivityBoundaryDurability.Required,
    string? DisplayName = null,
    string? Description = null,
    string? Category = null,
    float Order = 0,
    string? UiHint = null,
    JsonElement? UiSpecifications = null);

public sealed record ActivityOutputContractView(
    string ReferenceKey,
    string Name,
    ActivityTypeReferenceView Type,
    bool IsRequired,
    [property: JsonRequired] bool IsNullable,
    string StorageDriverKey,
    ActivityBoundaryDurability Durability = ActivityBoundaryDurability.Required,
    string? DisplayName = null,
    string? Description = null,
    string? Category = null,
    float Order = 0,
    string? UiHint = null,
    JsonElement? UiSpecifications = null);

public sealed record ActivityOutcomeContractView(
    string ReferenceKey,
    string Name,
    bool IsEmitted,
    string? Description = null);

public sealed record ActivityContractView(
    string ContractSchemaVersion,
    IReadOnlyList<ActivityInputContractView> Inputs,
    IReadOnlyList<ActivityOutputContractView> Outputs,
    IReadOnlyList<ActivityOutcomeContractView> Outcomes);

public static class ActivityContractViewMappings
{
    public static ActivityContract ToDomain(this ActivityContractView view) => new(
        view.ContractSchemaVersion,
        view.Inputs.Select(x => new ActivityInputContract(
            x.ReferenceKey,
            x.Name,
            x.Type.ToDomain(),
            x.IsRequired,
            x.IsNullable,
            x.Default is null ? null : new(x.Default.Syntax, x.Default.Value.Clone()),
            x.StorageDriverKey,
            x.Durability,
            x.DisplayName,
            x.Description,
            x.Category,
            x.Order,
            x.UiHint,
            Clone(x.UiSpecifications))).ToArray(),
        view.Outputs.Select(x => new ActivityOutputContract(
            x.ReferenceKey,
            x.Name,
            x.Type.ToDomain(),
            x.IsRequired,
            x.IsNullable,
            x.StorageDriverKey,
            x.Durability,
            x.DisplayName,
            x.Description,
            x.Category,
            x.Order,
            x.UiHint,
            Clone(x.UiSpecifications))).ToArray(),
        view.Outcomes.Select(x => new ActivityOutcomeContract(x.ReferenceKey, x.Name, x.IsEmitted, x.Description)).ToArray());

    public static ActivityContractView ToView(this ActivityContract contract) => new(
        contract.ContractSchemaVersion,
        contract.Inputs.Select(x => new ActivityInputContractView(
            x.ReferenceKey,
            x.Name,
            x.Type.ToView(),
            x.IsRequired,
            x.IsNullable,
            x.Default is null ? null : new(x.Default.Syntax, x.Default.Value.Clone()),
            x.StorageDriverKey,
            x.Durability,
            x.DisplayName,
            x.Description,
            x.Category,
            x.Order,
            x.UiHint,
            Clone(x.UiSpecifications))).ToArray(),
        contract.Outputs.Select(x => new ActivityOutputContractView(
            x.ReferenceKey,
            x.Name,
            x.Type.ToView(),
            x.IsRequired,
            x.IsNullable,
            x.StorageDriverKey,
            x.Durability,
            x.DisplayName,
            x.Description,
            x.Category,
            x.Order,
            x.UiHint,
            Clone(x.UiSpecifications))).ToArray(),
        contract.Outcomes.Select(x => new ActivityOutcomeContractView(x.ReferenceKey, x.Name, x.IsEmitted, x.Description)).ToArray());

    public static TypeReference ToDomain(this ActivityTypeReferenceView view)
    {
        if (!Enum.TryParse<CollectionKind>(view.CollectionKind, false, out var collectionKind) ||
            !StringComparer.Ordinal.Equals(collectionKind.ToString(), view.CollectionKind))
            throw new ArgumentException($"Collection kind '{view.CollectionKind}' is not supported.", nameof(view));
        return new(view.Alias, collectionKind);
    }

    public static ActivityTypeReferenceView ToView(this TypeReference type) => new(
        type.Alias,
        type.CollectionKind.ToString());

    private static JsonElement? Clone(JsonElement? value) => value is { } element ? element.Clone() : null;
}

public sealed record ActivityDefinitionIdentityView(
    string DefinitionId,
    string ActivityTypeKey,
    string? TenantId,
    string Category,
    string DisplayName,
    string? Description,
    ActivityContentAuthority ContentAuthority,
    ActivityDefinitionForkOrigin? ForkedFrom,
    string? HeadVersionId,
    string? RecommendedVersionId);

public sealed record ActivityDefinitionRecommendationView(
    string DefinitionId,
    string? HeadVersionId,
    string? RecommendedVersionId,
    DateTimeOffset ChangedAt,
    string Reason);

public sealed record RecommendedActivityDefinitionView(
    string DefinitionId,
    string ActivityTypeKey,
    string? TenantId,
    string Category,
    string DisplayName,
    string? Description,
    string VersionId,
    string Version,
    bool IsAvailable,
    string? UnavailableReason);

public sealed record RecommendedActivityDefinitionPageView(
    IReadOnlyList<RecommendedActivityDefinitionView> Items,
    int? NextOffset);

public sealed record ReusableActivityDefinitionMutationView(
    ActivityDefinitionIdentityView Definition,
    ReusableActivityDraftSummaryView Draft);

public enum ActivityForkCandidateLifecycleView
{
    Reserved,
    Applied,
    Expired
}

public enum ActivityForkOutcomeView
{
    Applied,
    AlreadyApplied,
    Stale,
    Expired,
    Rejected,
    Collision,
    Failed,
    OutcomeUnknown
}

public sealed record ActivityForkAccessBindingView(string Fingerprint);

public sealed record ActivityForkSourceView(
    string DefinitionId,
    string VersionId,
    string Version,
    ActivityDefinitionVersionLifecycle Lifecycle,
    string ProviderKey,
    string ProviderSchemaVersion,
    string ProviderFingerprint);

public sealed record ActivityForkPresentationView(
    string Category,
    string DisplayName,
    string? Description);

public sealed record ActivityForkTargetView(
    string DefinitionId,
    string ActivityTypeKey,
    string DraftId,
    string ProviderKey,
    string ProviderSchemaVersion,
    string ManifestFingerprint,
    ActivityContractView Contract);

public sealed record ActivityForkProviderMigrationView(
    string SourceProviderKey,
    string SourceProviderSchemaVersion,
    string TargetProviderKey,
    string TargetProviderSchemaVersion,
    string TargetManifestFingerprint,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed record ActivityForkContractChangeView(
    string Kind,
    string? ReferenceKey,
    string Detail);

public sealed record ActivityForkContractComparisonView(
    string SourceFingerprint,
    string TargetFingerprint,
    bool IsCompatible,
    IReadOnlyList<ActivityForkContractChangeView> Changes);

public sealed record ActivityForkPreviewView(
    string CandidateId,
    string RequestFingerprint,
    ActivityForkCandidateLifecycleView Status,
    ActivityForkAccessBindingView AccessBinding,
    ActivityForkSourceView Source,
    ActivityForkPresentationView Presentation,
    ActivityForkTargetView Target,
    ActivityForkProviderMigrationView ProviderMigration,
    ActivityForkContractComparisonView ContractComparison,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record ActivityForkReceiptView(
    string IdempotencyKey,
    string CandidateId,
    string RequestFingerprint,
    ActivityForkOutcomeView Outcome,
    ActivityForkAccessBindingView AccessBinding,
    ActivityDefinitionIdentityView Definition,
    ReusableActivityDraftSummaryView Draft,
    DateTimeOffset AppliedAt);

public sealed record ReusableActivityDraftSummaryView(
    string DraftId,
    string DefinitionId,
    long Revision,
    string? SourceVersionId,
    ActivityDefinitionDraftStatus Status,
    string ProviderKey,
    string ProviderSchemaVersion,
    DateTimeOffset UpdatedAt,
    string? PresentationLabel = null);

public sealed record ActivityManagementSnapshotView(string SnapshotId, DateTimeOffset AsOf);

public sealed record ActivityManagementPageView<T>(
    IReadOnlyList<T> Items,
    int Count,
    long TotalCount,
    bool HasMore,
    string? Continuation,
    ActivityManagementSnapshotView Snapshot);

public sealed record ActivityDefinitionVersionReferenceView(
    string VersionId,
    string Version,
    ActivityDefinitionVersionLifecycle Lifecycle,
    string ProviderKey,
    string ProviderSchemaVersion);

public sealed record ActivityDefinitionLifecycleSummaryView(
    long DraftCount,
    long VersionCount,
    ActivityDefinitionVersionReferenceView? Head,
    ActivityDefinitionVersionReferenceView? Recommendation);

public sealed record ReusableActivityDefinitionManagementView(
    ActivityDefinitionIdentityView Definition,
    ActivityDefinitionLifecycleSummaryView Lifecycle,
    IReadOnlyList<ActivityActionAvailabilityView> Actions,
    DateTimeOffset UpdatedAt);

public sealed record ActivityActionAvailabilityView(string Action, bool Allowed, string? UnavailableCode = null);

public sealed record ReusableActivityDraftManagementView(
    ReusableActivityDraftSummaryView Draft,
    IReadOnlyList<ActivityActionAvailabilityView> Actions);

public sealed record ReusableActivityVersionManagementView(
    ReusableActivityVersionSummaryView Version,
    string ProviderKey,
    string ProviderSchemaVersion,
    bool IsRecommended,
    IReadOnlyList<ActivityActionAvailabilityView> Actions);

public sealed record ActivityProviderManifestView(
    string ProviderKey,
    string SchemaVersion,
    string ManifestFingerprint,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Payload);

public sealed record ActivityProviderManifestSchemaCapabilityView(
    string SchemaVersion,
    bool IsAuthorable,
    IReadOnlyList<string> MigratableFromSchemaVersions);

public sealed record ActivityProviderAuthoringCapabilityView(
    string ProviderKey,
    string DisplayName,
    IReadOnlyList<ActivityProviderManifestSchemaCapabilityView> ManifestSchemas,
    IReadOnlyList<ActivityOutcomeContractView> RequiredOutcomes);

public sealed record ActivityContractTypeCapabilityView(
    string Alias,
    string DisplayName,
    string Category,
    string DefaultEditor,
    IReadOnlyList<string> SupportedCollectionKinds,
    bool SupportsNull,
    bool SupportsDurability,
    IReadOnlyList<string> CompatibleStorageDriverKeys);

public sealed record ActivityAuthoringCapabilitiesView(
    IReadOnlyList<string> ContractSchemaVersions,
    ActivityTypeKeyRules ActivityTypeKeyRules,
    IReadOnlyList<ActivityProviderAuthoringCapabilityView> Providers,
    IReadOnlyList<ActivityContractTypeCapabilityView> Types,
    IReadOnlyList<string> StorageDriverKeys,
    string SnapshotFingerprint);

public sealed record ActivityContractProposalChangeView(
    string ChangeId,
    ActivityContractProposalOperation Operation,
    ActivityContractMemberKind MemberKind,
    string ReferenceKey,
    ActivityInputContractView? Input,
    ActivityOutputContractView? Output,
    ActivityOutcomeContractView? Outcome);

public sealed record ActivityContractProposalView(
    string DraftId,
    long Revision,
    string ProviderKey,
    string ProviderSchemaVersion,
    string ManifestFingerprint,
    string ProposalFingerprint,
    IReadOnlyList<ActivityContractProposalChangeView> Changes,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed record ActivityDraftValidationView(
    string DraftId,
    long Revision,
    bool IsValid,
    DateTimeOffset ValidatedAt,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed record ReusableActivityDraftView(
    string DraftId,
    string DefinitionId,
    string? TenantId,
    long Revision,
    string? SourceVersionId,
    ActivityDefinitionDraftStatus Status,
    ActivityContractView Contract,
    ActivityProviderManifestView Provider,
    IReadOnlyList<ActivityLayoutRecord> Layout,
    ActivityDraftValidationView? Validation,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? PresentationLabel = null);

public sealed record ReusableActivityVersionSummaryView(
    string VersionId,
    string DefinitionId,
    string Version,
    ActivityDefinitionVersionLifecycle Lifecycle,
    DateTimeOffset PublishedAt);

public sealed record ActivityPublishedTemplateView(
    string TemplateId,
    string TemplateHash,
    string SourceReferenceId,
    string ProviderFingerprint,
    long DirectDependencyCount,
    long ClosedTemplateCount,
    IReadOnlyList<ActivityRuntimeRequirementDeclaration> RuntimeRequirements);

public sealed record ReusableActivityVersionView(
    ActivityDefinitionIdentityView Definition,
    string VersionId,
    string Version,
    string? SourceDraftId,
    string? SourceVersionId,
    ActivityContractView Contract,
    ActivityProviderManifestView Provider,
    ActivityPublishedTemplateView Template,
    ActivityDefinitionVersionLifecycle Lifecycle,
    DateTimeOffset PublishedAt);

public sealed record ReusableActivityVersionLifecycleView(
    string VersionId,
    ActivityDefinitionVersionLifecycle Lifecycle,
    string Reason,
    DateTimeOffset ChangedAt);

public sealed class ActivityAuthoringException(
    int statusCode,
    string errorCode,
    string title,
    string message,
    IReadOnlyList<ActivityDiagnostic>? diagnostics = null,
    Exception? innerException = null,
    ActivityRecoveryView? recovery = null) : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
    public string Title { get; } = title;
    public IReadOnlyList<ActivityDiagnostic> Diagnostics { get; } = diagnostics ?? [];
    public ActivityRecoveryView? Recovery { get; } = recovery;
}
