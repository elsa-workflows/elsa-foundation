using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Api.AspNetCore;
using Elsa.Events.Core.Contracts;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using MinimalEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts.ValidateCandidate;

[Post("drafts/{draftId}/candidate-validation")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint(
    IWorkflowDefinitionDraftStore drafts,
    IWorkflowDefinitionStore definitions,
    IEnumerable<IWorkflowDefinitionAuthorizer> authorizers,
    IEnumerable<IActivityAuthoringCatalog> catalogs,
    IActivityStructureService structures,
    IActivityDefinitionLookup lookup,
    ICredentialLiteralValidator credentials,
    IInlineEventPublisher events) : ApiEndpoint<ValidateWorkflowCandidate, WorkflowCandidateValidationView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsValidateCandidate";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override async Task<WorkflowCandidateValidationView> HandleAsync(ValidateWorkflowCandidate request, CancellationToken ct)
    {
        WorkflowCandidateValidationView Refuse(string state) => new(state, []);
        var owners = authorizers.ToArray();
        var catalogOwners = catalogs.ToArray();
        if (owners.Length != 1 || catalogOwners.Length != 1) return Refuse("unavailable");
        var authority = owners[0];
        var catalog = catalogOwners[0];
        if (request.Baseline is null || request.Candidate is null || request.Baseline.Id != request.DraftId)
            return Refuse("invalid");
        var definitionId = request.Baseline.DefinitionId;
        if (!await authority.CanManageAsync(definitionId, ct) || !await catalog.CanReadAsync(ct)) return Refuse("denied");
        var current = await drafts.FindWithLayoutByIdAsync(request.DraftId, ct);
        var definition = await definitions.FindByIdAsync(definitionId, ct);
        if (current is null || current.Draft.WorkflowDefinitionId != definitionId || definition is null || definition.Id != definitionId || definition.TenantId != current.Draft.TenantId || definition.DeletedAt is not null || definition.IsSourceOwned)
            return Refuse("denied");
        var baselineTenant = current.Draft.TenantId;
        var baseline = WorkflowDraftView.From(current.Draft, current.Layout, current.ActivityPresentation);
        // The opaque acknowledgement binds the actual stored state. Studio expands that state for
        // editing and may encode Object JSON as text on the wire; that representation is not a new
        // acknowledged resource. Compare the remaining identity and sidecars exactly, then retain
        // the full server snapshot for the post-validation fence below.
        if (string.IsNullOrWhiteSpace(request.Baseline.AuthoringRevision) ||
            request.Baseline.AuthoringRevision != baseline.AuthoringRevision ||
            Hash(baseline) != Hash(request.Baseline with { State = baseline.State }))
            return Refuse("stale");
        var proposal = request.Candidate.ToContent();
        var visible = await catalog.ListAsync(ct);
        if (!visible.Allowed || !ActivityDataContextService.CatalogAllows(proposal.State.RootActivity, structures, visible, ct))
            return Refuse("denied");
        var scope = ActivityDataContextService.CatalogScopeRevision(visible);
        var versions = await ActivityDataContextService.CatalogRevisionAsync(proposal.State.RootActivity, structures, lookup, ct);
        // A detached entity is essential: validators must never receive the tracked persisted draft as the proposal.
        var candidate = new WorkflowDefinitionDraft
        {
            Id = current.Draft.Id, TenantId = current.Draft.TenantId, WorkflowDefinitionId = definitionId,
            SourceVersionId = current.Draft.SourceVersionId, CreatedAt = current.Draft.CreatedAt,
            LastModifiedAt = current.Draft.LastModifiedAt, State = proposal.State
        };
        await credentials.AdmitAsync(proposal.State, ct);
        var errors = await events.DeriveValidationErrorsAsync(candidate, ct);
        // Do not disclose a now-stale verdict, or accept access revoked while validators were awaited.
        if (!await authority.CanManageAsync(definitionId, ct) || !await catalog.CanReadAsync(ct)) return Refuse("denied");
        var afterCatalog = await catalog.ListAsync(ct);
        if (!afterCatalog.Allowed || !ActivityDataContextService.CatalogAllows(proposal.State.RootActivity, structures, afterCatalog, ct))
            return Refuse("denied");
        if (ActivityDataContextService.CatalogScopeRevision(afterCatalog) != scope ||
            await ActivityDataContextService.CatalogRevisionAsync(proposal.State.RootActivity, structures, lookup, ct) != versions)
            return Refuse("stale");
        var after = await drafts.FindWithLayoutByIdAsync(request.DraftId, ct);
        definition = await definitions.FindByIdAsync(definitionId, ct);
        if (definition is null || definition.Id != definitionId || definition.DeletedAt is not null || definition.IsSourceOwned || after is null || after.Draft.WorkflowDefinitionId != definitionId || after.Draft.TenantId != baselineTenant || definition.TenantId != baselineTenant)
            return Refuse("denied");
        if (Hash(WorkflowDraftView.From(after.Draft, after.Layout, after.ActivityPresentation)) != Hash(baseline)) return Refuse("stale");
        var findings = errors.Select(DraftValidationErrorView.From).ToArray();
        return new(findings.Any(x => x.Severity == DraftValidationErrorView.ErrorSeverity) ? "invalid" : "validated", findings);
    }

    private static string Hash(WorkflowDraftView value) => WorkflowAuthoringRevision.JsonDigest(
        JsonSerializer.SerializeToElement(value, WorkflowsDesignJsonOptions.WireContext.WorkflowDraftView));
}
