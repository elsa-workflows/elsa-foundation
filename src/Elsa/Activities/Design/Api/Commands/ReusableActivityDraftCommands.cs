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

    bool CanAuthorProvider(string providerKey);

    bool CanReadProviderPayload(string providerKey);
}

public sealed record CreateReusableActivityDefinition(
    string ActivityTypeKey,
    string Category,
    string DisplayName,
    string? Description,
    ActivityProviderManifest Provider,
    ActivityContractView Contract,
    IReadOnlyList<ActivityLayoutRecord> Layout) : ICommand<ReusableActivityDefinitionDetailsView>;

public sealed record ForkReusableActivityDefinition(
    [property: JsonIgnore] string DefinitionId,
    string SourceVersionId,
    string ActivityTypeKey,
    string Category,
    string DisplayName,
    string? Description,
    string TargetProviderKey,
    string TargetProviderSchemaVersion) : ICommand<ReusableActivityDefinitionDetailsView>;

public sealed record UpdateReusableActivityDefinition(
    [property: JsonIgnore] string DefinitionId,
    string Category,
    string DisplayName,
    string? Description) : ICommand<ReusableActivityDefinitionDetailsView>;

public sealed record CreateReusableActivityDraft(
    [property: JsonIgnore] string DefinitionId,
    string? SourceVersionId,
    ActivityProviderManifest? Provider = null,
    ActivityContractView? Contract = null,
    IReadOnlyList<ActivityLayoutRecord>? Layout = null) : ICommand<ReusableActivityDraftView>;

public sealed record ReplaceReusableActivityDraft(
    [property: JsonIgnore] string DraftId,
    long ExpectedRevision,
    ActivityContractView Contract,
    ActivityProviderManifest Provider,
    IReadOnlyList<ActivityLayoutRecord> Layout) : ICommand<ReusableActivityDraftView>;

public sealed record DiscardReusableActivityDraft([property: JsonIgnore] string DraftId, long ExpectedRevision) : ICommand;

public sealed record ValidateReusableActivityDraft([property: JsonIgnore] string DraftId, long ExpectedRevision) : ICommand<ActivityDraftValidationView>;

public sealed record MigrateReusableActivityDraft(
    [property: JsonIgnore] string DraftId,
    long ExpectedRevision,
    string TargetProviderKey,
    string TargetSchemaVersion) : ICommand<ReusableActivityDraftView>;

public sealed record ListReusableActivityDefinitions : IRequest<IReadOnlyList<ActivityDefinitionIdentityView>>;

public sealed record GetReusableActivityDefinition(string DefinitionId) : IRequest<ReusableActivityDefinitionDetailsView>;

public sealed record ListReusableActivityDrafts(string DefinitionId) : IRequest<IReadOnlyList<ReusableActivityDraftSummaryView>>;

public sealed record GetReusableActivityDraft(string DraftId) : IRequest<ReusableActivityDraftView>;

public sealed record ListReusableActivityVersions(string DefinitionId) : IRequest<IReadOnlyList<ReusableActivityVersionSummaryView>>;

public sealed record GetReusableActivityVersion(string VersionId) : IRequest<ReusableActivityVersionView>;
