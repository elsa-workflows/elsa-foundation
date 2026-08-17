using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using System.Text.Json.Serialization;
using RouteParam = FastEndpoints.RouteParamAttribute;

namespace Elsa.Activities.Design.Api.Commands;

/// <summary>
/// Required host adapter for tenant scoping and provider-source authorization. T033 supplies the
/// shell registration; authoring handlers deliberately have no global/null fallback.
/// </summary>
[Obsolete("Use IActivityAuthoringContextAsync. This interface will be removed in the next major version.")]
public interface IActivityAuthoringContext
{
    string? TenantId { get; }

    string ActorId => AuthorizationProfile;

    string AuthorizationProfile { get; }

    bool CanAuthorProvider(string providerKey);

    bool CanReadProviderPayload(string providerKey);

    bool CanManageActivityDefinitions => false;
}

/// <summary>
/// Asynchronous replacement seam for <see cref="IActivityAuthoringContext"/>. First-party request
/// handlers use this interface so permission decisions always go through Foundation Identity's
/// canonical evaluator. The synchronous interface remains for one compatibility window and is not
/// used by production handlers.
/// </summary>
[ReplacementContract]
public interface IActivityAuthoringContextAsync
{
    string? TenantId { get; }

    string ActorId { get; }

    ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default);

    ValueTask<bool> CanAuthorProviderAsync(string providerKey, CancellationToken cancellationToken = default);

    ValueTask<bool> CanReadProviderPayloadAsync(string providerKey, CancellationToken cancellationToken = default);

    ValueTask<bool> CanManageActivityDefinitionsAsync(CancellationToken cancellationToken = default);
}

public sealed record CreateReusableActivityDefinition(
    string Category,
    string DisplayName,
    string? Description,
    ActivityProviderManifest Provider,
    ActivityContractView Contract,
    IReadOnlyList<ActivityLayoutRecord> Layout,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ActivityTypeKey = null) : ICommand<ReusableActivityDefinitionMutationView>;

public sealed record PreviewReusableActivityFork(
    [property: RouteParam, JsonIgnore] string DefinitionId,
    string IdempotencyKey,
    string SourceVersionId,
    string Category,
    string DisplayName,
    string? Description,
    string TargetProviderKey,
    string TargetProviderSchemaVersion) : ICommand<ActivityForkPreviewView>;

public sealed record ApplyReusableActivityFork(
    [property: RouteParam, JsonIgnore] string CandidateId,
    string RequestFingerprint,
    string IdempotencyKey) : ICommand<ActivityForkReceiptView>;

public sealed record GetReusableActivityForkStatus(
    [property: RouteParam, JsonIgnore] string IdempotencyKey) : IRequest<ActivityForkReceiptView>;

public sealed record UpdateReusableActivityDefinition(
    [property: RouteParam, JsonIgnore] string DefinitionId,
    string Category,
    string DisplayName,
    string? Description) : ICommand<ActivityDefinitionIdentityView>;

public sealed record CreateReusableActivityDraft(
    [property: RouteParam, JsonIgnore] string DefinitionId,
    string? SourceVersionId,
    ActivityProviderManifest? Provider = null,
    ActivityContractView? Contract = null,
    IReadOnlyList<ActivityLayoutRecord>? Layout = null,
    string? PresentationLabel = null) : ICommand<ReusableActivityDraftView>;

public sealed record UpdateReusableActivityDraftPresentation(
    [property: RouteParam, JsonIgnore] string DraftId,
    long ExpectedRevision,
    string? PresentationLabel) : ICommand<ReusableActivityDraftView>;

public sealed record CreateReusableActivityDraftConflictCopy(
    [property: RouteParam, JsonIgnore] string DraftId,
    long ExpectedSourceRevision,
    ActivityContractView Contract,
    ActivityProviderManifest Provider,
    IReadOnlyList<ActivityLayoutRecord> Layout,
    string? PresentationLabel = null) : ICommand<ReusableActivityDraftView>;

public sealed record ReplaceReusableActivityDraft(
    [property: RouteParam, JsonIgnore] string DraftId,
    long ExpectedRevision,
    ActivityContractView Contract,
    ActivityProviderManifest Provider,
    IReadOnlyList<ActivityLayoutRecord> Layout,
    string? PresentationLabel = null) : ICommand<ReusableActivityDraftView>;

public sealed record DiscardReusableActivityDraft([property: RouteParam, JsonIgnore] string DraftId, long ExpectedRevision) : ICommand;

public sealed record ValidateReusableActivityDraft([property: RouteParam, JsonIgnore] string DraftId, long ExpectedRevision) : ICommand<ActivityDraftValidationView>;

public sealed record MigrateReusableActivityDraft(
    [property: RouteParam, JsonIgnore] string DraftId,
    long ExpectedRevision,
    string TargetProviderKey,
    string TargetSchemaVersion) : ICommand<ReusableActivityDraftView>;

public sealed record ProposeReusableActivityContract(
    [property: RouteParam, JsonIgnore] string DraftId,
    long ExpectedRevision,
    string ExpectedProviderKey,
    string ExpectedProviderSchemaVersion,
    string ExpectedManifestFingerprint) : IRequest<ActivityContractProposalView>;

public sealed record ApplyReusableActivityContractProposal(
    [property: RouteParam, JsonIgnore] string DraftId,
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
