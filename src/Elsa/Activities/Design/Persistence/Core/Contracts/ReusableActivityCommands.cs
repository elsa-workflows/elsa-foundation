using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;

namespace Elsa.Activities.Design.Persistence.Core.Contracts;

public sealed record CreateActivityDefinitionRequest(
    ActivityDefinition Definition,
    ActivityDefinitionAuthoringState AuthoringState,
    ActivityDefinitionDraft InitialDraft,
    ActivityDefinitionDraftLayout InitialLayout);

public interface ICreateActivityDefinitionCommand
{
    Task ExecuteAsync(
        CreateActivityDefinitionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record UpdateActivityDefinitionPresentationRequest(
    string DefinitionId,
    string? TenantId,
    string Category,
    string DisplayName,
    string? Description,
    DateTimeOffset LastModifiedAt);

/// <summary>
/// Updates only mutable catalog presentation metadata. Implementations must preserve the activity
/// type key, tenant, content authority, fork provenance, and head identity.
/// </summary>
public interface IUpdateActivityDefinitionPresentationCommand
{
    Task<ActivityDefinition> ExecuteAsync(
        UpdateActivityDefinitionPresentationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record CreateActivityDraftRequest(
    ActivityDefinitionDraft Draft,
    ActivityDefinitionDraftLayout Layout,
    string? ExpectedDefinitionHeadVersionId);

public interface ICreateActivityDraftCommand
{
    Task ExecuteAsync(
        CreateActivityDraftRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ReplaceActivityDraftRequest(
    string DraftId,
    long ExpectedRevision,
    ActivityDefinitionDraftState State,
    IReadOnlyList<ActivityLayoutRecord> Layout);

public interface IReplaceActivityDraftCommand
{
    Task<ActivityDefinitionDraft> ExecuteAsync(
        ReplaceActivityDraftRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record DiscardActivityDraftRequest(string DraftId, long ExpectedRevision);

public interface IDiscardActivityDraftCommand
{
    Task ExecuteAsync(
        DiscardActivityDraftRequest request,
        CancellationToken cancellationToken = default);
}

public interface IStoreActivityDraftValidationCommand
{
    Task ExecuteAsync(
        ActivityDraftValidationState validation,
        CancellationToken cancellationToken = default);
}

public sealed record ChangeActivityVersionLifecycleRequest(
    string DefinitionVersionId,
    ActivityDefinitionVersionLifecycle ExpectedLifecycle,
    ActivityDefinitionVersionLifecycle Lifecycle,
    string Reason);

public interface IChangeActivityVersionLifecycleCommand
{
    Task<ActivityDefinitionVersionPublication> ExecuteAsync(
        ChangeActivityVersionLifecycleRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ActivityPublicationDesignMutation(
    string DraftId,
    long ExpectedDraftRevision,
    string DefinitionId,
    string? ExpectedDefinitionHeadVersionId,
    ActivityDefinitionVersion CatalogVersion,
    ActivityDefinitionVersionPublication Publication,
    ActivityDefinitionVersionLayout Layout,
    IReadOnlyList<ActivityDependencyEdge> DirectDependencies);

/// <summary>
/// Complete atomic publication input. Generic artifact types keep this Design persistence contract
/// independent of Runtime while allowing the Publishing Groundwork bridge to close it over Runtime's
/// executable-template and Source Reference types and commit every document in one transaction.
/// </summary>
public sealed record ActivityPublicationCommit<TExecutableTemplate, TSourceReference>(
    ActivityPublicationDesignMutation Design,
    TExecutableTemplate ExecutableTemplate,
    TSourceReference SourceReference)
    where TExecutableTemplate : class
    where TSourceReference : class;

public sealed record ActivityPublicationResult(
    string DefinitionId,
    string DefinitionVersionId,
    string DraftId,
    string TemplateId,
    string SourceReferenceId,
    DateTimeOffset PublishedAt);

public interface ICommitActivityPublicationCommand<TExecutableTemplate, TSourceReference>
    where TExecutableTemplate : class
    where TSourceReference : class
{
    Task<ActivityPublicationResult> ExecuteAsync(
        ActivityPublicationCommit<TExecutableTemplate, TSourceReference> commit,
        CancellationToken cancellationToken = default);
}

public sealed record SourceActivityPublicationCommit<TExecutableTemplate, TSourceReference>(
    ActivityDefinition Definition,
    ActivityDefinitionAuthoringState AuthoringState,
    ActivityDefinitionVersion CatalogVersion,
    ActivityDefinitionVersionPublication Publication,
    ActivityDefinitionVersionLayout Layout,
    TExecutableTemplate ExecutableTemplate,
    TSourceReference SourceReference)
    where TExecutableTemplate : class
    where TSourceReference : class;

/// <summary>
/// Atomic source-authority counterpart of draft publication. There is no mutable draft or head CAS:
/// the source owns content and reconciliation supplies deterministic definition/version identities.
/// </summary>
public interface ICommitSourceActivityPublicationCommand<TExecutableTemplate, TSourceReference>
    where TExecutableTemplate : class
    where TSourceReference : class
{
    Task ExecuteAsync(
        SourceActivityPublicationCommit<TExecutableTemplate, TSourceReference> commit,
        CancellationToken cancellationToken = default);
}
