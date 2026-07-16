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
        var collectionKind = view.CollectionKind switch
        {
            var value when string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) => CollectionKind.Single,
            var value when string.Equals(value, "Single", StringComparison.OrdinalIgnoreCase) => CollectionKind.Single,
            var value when Enum.TryParse<CollectionKind>(value, true, out var parsed) => parsed,
            _ => throw new ArgumentException($"Collection kind '{view.CollectionKind}' is not supported.", nameof(view))
        };
        return new(view.Alias, collectionKind);
    }

    public static ActivityTypeReferenceView ToView(this TypeReference type) => new(
        type.Alias,
        type.CollectionKind == CollectionKind.Single ? "None" : type.CollectionKind.ToString());

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
    string? HeadVersionId);

public sealed record ReusableActivityDefinitionDetailsView(
    ActivityDefinitionIdentityView Definition,
    IReadOnlyList<ReusableActivityDraftSummaryView> Drafts,
    IReadOnlyList<ReusableActivityVersionSummaryView> Versions);

public sealed record ReusableActivityDraftSummaryView(
    string DraftId,
    string DefinitionId,
    long Revision,
    string? SourceVersionId,
    ActivityDefinitionDraftStatus Status,
    string ProviderKey,
    string ProviderSchemaVersion,
    DateTimeOffset UpdatedAt);

public sealed record ActivityProviderManifestView(
    string ProviderKey,
    string SchemaVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Payload);

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
    DateTimeOffset UpdatedAt);

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
    Exception? innerException = null) : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
    public string Title { get; } = title;
    public IReadOnlyList<ActivityDiagnostic> Diagnostics { get; } = diagnostics ?? [];
}
