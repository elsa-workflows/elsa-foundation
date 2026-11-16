using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

/// <summary>
/// Composes the details view for one workflow definition: its record, current draft with layout,
/// and version summaries.
/// </summary>
/// <remarks>
/// Shared by the Get endpoint and the mutations that return the updated details view. It replaces
/// the internal <c>GetDefinition</c> mediator round-trip those handlers used, so the read
/// composition exists once without any dispatch indirection.
/// </remarks>
public interface IWorkflowDefinitionDetailsReader
{
    /// <summary>Checks that a mutation may return its details before committing. Existing
    /// readers can use the complete read; the default reader checks only organization metadata.</summary>
    Task AuthorizeReadAsync(string definitionId, CancellationToken cancellationToken) => ReadAsync(definitionId, cancellationToken);
    Task<WorkflowDefinitionDetailsView> ReadAsync(string definitionId, CancellationToken cancellationToken);
}

public sealed class WorkflowDefinitionDetailsReader(
    IWorkflowDefinitionVersionStore versionStore,
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionDraftStore draftStore,
    IWorkflowDefinitionPublicationStateLookup? publicationStateLookup = null,
    IWorkflowLibraryStore? library = null,
    IHttpContextAccessor? httpContextAccessor = null) : IWorkflowDefinitionDetailsReader
{
    public async Task AuthorizeReadAsync(string definitionId, CancellationToken cancellationToken)
    {
        if (library is null) return;
        var context = httpContextAccessor?.HttpContext
            ?? throw new InvalidOperationException("Workflow library details require an authenticated HTTP request context.");
        await WorkflowLibraryAccess.RequireAsync(context, WorkflowDesignPermissions.Read,
            new WorkflowLibraryResource(DefinitionId: definitionId), cancellationToken);
        if (await library.GetOrganizationAsync(definitionId, cancellationToken) is { } organization)
            await WorkflowLibraryAccess.RequireOrganizationReadAsync(context, organization, cancellationToken);
    }

    public async Task<WorkflowDefinitionDetailsView> ReadAsync(string definitionId, CancellationToken cancellationToken)
    {
        // Evidence from an earlier read never survives into this one: it is recorded again only on success.
        var request = httpContextAccessor?.HttpContext;
        WorkflowDefinitionReadEvidence.Discard(request);
        var context = library is null ? null : request
            ?? throw new InvalidOperationException("Workflow library details require an authenticated HTTP request context.");
        if (context is not null)
            await WorkflowLibraryAccess.RequireAsync(context, WorkflowDesignPermissions.Read,
                new WorkflowLibraryResource(DefinitionId: definitionId), cancellationToken);
        // All three stores share the request's scoped persistence context, so these reads are awaited one at a time.
        var definition = await definitionStore.GetAsync(definitionId, cancellationToken);
        var versions = await versionStore.ListByDefinitionAsync(definitionId, cancellationToken);
        var draft = await draftStore.FindByWorkflowDefinitionIdAsync(definitionId, cancellationToken);
        var metadata = draft is null
            ? null
            : await draftStore.FindWithLayoutByIdAsync(draft.Id, cancellationToken);
        var latestVersion = versions
            .OrderByDescending(version => version.SemVerSortKey, StringComparer.Ordinal)
            .ThenByDescending(version => version.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        var versionsById = versions.ToDictionary(version => version.Id, version => version.Version, StringComparer.Ordinal);
        var publicationSummaries = publicationStateLookup is null
            ? null
            : await publicationStateLookup.ListByDefinitionIdsAsync([definitionId], cancellationToken);
        var definitionView = definition.ToView() with
        {
            DraftId = draft?.Id,
            DraftSourceVersionId = draft?.SourceVersionId,
            DraftSourceVersion = draft?.SourceVersionId is { } sourceVersionId && versionsById.TryGetValue(sourceVersionId, out var sourceVersion)
                ? sourceVersion
                : null,
            LatestVersionId = latestVersion?.Id,
            LatestVersion = latestVersion?.Version,
            VersionCount = versions.Count
        };
        if (library is not null && await library.GetOrganizationAsync(definitionId, cancellationToken) is { } organization)
        {
            await WorkflowLibraryAccess.RequireOrganizationReadAsync(context!, organization, cancellationToken);
            definitionView = WorkflowLibraryProjection.WithOrganization(definitionView, organization, WorkflowLibraryAccess.OrganizationEnabled(context!));
        }

        var details = new WorkflowDefinitionDetailsView(
            definitionView.WithPublicationState(publicationSummaries, versionsById),
            draft is null
                ? null
                : WorkflowDraftView.From(
                    draft,
                    metadata?.Layout ?? [],
                    metadata?.ActivityPresentation ?? []),
            versions.Select(e => new WorkflowDefinitionVersionSummary(e.Id, e.Version, e.CreatedAt))
        );
        // Recorded last, once every check above has passed, for the built-in authorizer's next decision in this
        // request. With a library store the Read permission and organization checks ran through the request's
        // permission service; without one, the read confirms existence only.
        if (request is not null)
            WorkflowDefinitionReadEvidence.Record(request, this, definitionStore,
                context?.RequestServices.GetService<IPermissionAuthorizationService>(), definitionId, definition);
        return details;
    }
}
