using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using System.Text.Json.Serialization;

namespace Elsa.Activities.Design.Api.Commands;

/// <summary>
/// Required host adapter for tenant scoping and provider-source authorization. T033 supplies the
/// shell registration; authoring handlers deliberately have no global/null fallback.
/// </summary>
public interface IActivityAuthoringContext
{
    string? TenantId { get; }

    string AuthorizationProfile { get; }

    bool CanAuthorProvider(string providerKey);

    bool CanReadProviderPayload(string providerKey);

    bool CanManageActivityDefinitions => false;
}

public sealed record CreateReusableActivityDefinition(
    string Category,
    string DisplayName,
    string? Description,
    ActivityProviderManifest Provider,
    ActivityContractView Contract,
    IReadOnlyList<ActivityLayoutRecord> Layout) : ICommand<ReusableActivityDefinitionMutationView>;

public sealed record ForkReusableActivityDefinition(
    [property: JsonIgnore] string DefinitionId,
    string SourceVersionId,
    string Category,
    string DisplayName,
    string? Description,
    string TargetProviderKey,
    string TargetProviderSchemaVersion) : ICommand<ReusableActivityDefinitionMutationView>;

public sealed record UpdateReusableActivityDefinition(
    [property: JsonIgnore] string DefinitionId,
    string Category,
    string DisplayName,
    string? Description) : ICommand<ActivityDefinitionIdentityView>;

public sealed record CreateReusableActivityDraft(
    [property: JsonIgnore] string DefinitionId,
    string? SourceVersionId,
    ActivityProviderManifest? Provider = null,
    ActivityContractView? Contract = null,
    IReadOnlyList<ActivityLayoutRecord>? Layout = null,
    string? PresentationLabel = null) : ICommand<ReusableActivityDraftView>;

public sealed record UpdateReusableActivityDraftPresentation(
    [property: JsonIgnore] string DraftId,
    long ExpectedRevision,
    string? PresentationLabel) : ICommand<ReusableActivityDraftView>;

public sealed record CreateReusableActivityDraftConflictCopy(
    [property: JsonIgnore] string DraftId,
    long ExpectedSourceRevision,
    ActivityContractView Contract,
    ActivityProviderManifest Provider,
    IReadOnlyList<ActivityLayoutRecord> Layout,
    string? PresentationLabel = null) : ICommand<ReusableActivityDraftView>;

public sealed record ReplaceReusableActivityDraft(
    [property: JsonIgnore] string DraftId,
    long ExpectedRevision,
    ActivityContractView Contract,
    ActivityProviderManifest Provider,
    IReadOnlyList<ActivityLayoutRecord> Layout,
    string? PresentationLabel = null) : ICommand<ReusableActivityDraftView>;

public sealed record DiscardReusableActivityDraft([property: JsonIgnore] string DraftId, long ExpectedRevision) : ICommand;

public sealed record ValidateReusableActivityDraft([property: JsonIgnore] string DraftId, long ExpectedRevision) : ICommand<ActivityDraftValidationView>;

public sealed record MigrateReusableActivityDraft(
    [property: JsonIgnore] string DraftId,
    long ExpectedRevision,
    string TargetProviderKey,
    string TargetSchemaVersion) : ICommand<ReusableActivityDraftView>;

public sealed record ProposeReusableActivityContract(
    [property: JsonIgnore] string DraftId,
    long ExpectedRevision,
    string ExpectedProviderKey,
    string ExpectedProviderSchemaVersion,
    string ExpectedManifestFingerprint) : IRequest<ActivityContractProposalView>;

public sealed record ApplyReusableActivityContractProposal(
    [property: JsonIgnore] string DraftId,
    long ExpectedRevision,
    string ExpectedProviderKey,
    string ExpectedProviderSchemaVersion,
    string ExpectedManifestFingerprint,
    string ProposalFingerprint,
    IReadOnlyList<string> SelectedChangeIds) : ICommand<ReusableActivityDraftView>;

public sealed record ListReusableActivityDefinitions(
    int Limit = 25,
    string? Cursor = null,
    string? Search = null,
    string? Authority = null,
    string? ProviderKey = null,
    string Sort = "identity-asc") : IRequest<ActivityManagementPageView<ReusableActivityDefinitionManagementView>>;

public sealed record GetReusableActivityDefinition(string DefinitionId) : IRequest<ReusableActivityDefinitionManagementView>;

public sealed record ListReusableActivityDrafts(
    string DefinitionId,
    int Limit = 25,
    string? Cursor = null,
    string? Search = null,
    string? ProviderKey = null,
    string? Status = null,
    string Sort = "identity-asc") : IRequest<ActivityManagementPageView<ReusableActivityDraftManagementView>>;

public sealed record GetReusableActivityDraft(string DraftId) : IRequest<ReusableActivityDraftView>;

public sealed record ListReusableActivityVersions(
    string DefinitionId,
    int Limit = 25,
    string? Cursor = null,
    string? Search = null,
    string? ProviderKey = null,
    string? Lifecycle = null,
    string Sort = "identity-asc") : IRequest<ActivityManagementPageView<ReusableActivityVersionManagementView>>;

public sealed record GetReusableActivityVersion(string VersionId) : IRequest<ReusableActivityVersionView>;
