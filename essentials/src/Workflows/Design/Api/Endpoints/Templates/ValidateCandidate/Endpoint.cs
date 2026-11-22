using System.Text.Json.Serialization;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core;
using MinimalEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Templates.ValidateCandidate;

public sealed record ValidateTemplateCandidate([property: JsonIgnore] string TemplateId, string ExpectedRevision, WorkflowTemplateContentView Candidate);

/// <summary>Uses the template's own replace authority and content admission; a template is not a workflow draft.</summary>
[Post("templates/{templateId}/candidate-validation")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint(ICredentialLiteralValidator credentials, IEnumerable<IActivityAuthoringCatalog> catalogs,
    IActivityStructureService structures, IActivityDefinitionLookup lookup, IWorkflowTemplateStore? store = null)
    : ApiEndpoint<ValidateTemplateCandidate, WorkflowCandidateValidationView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "WorkflowTemplatesValidateCandidate";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override async Task<WorkflowCandidateValidationView> HandleAsync(ValidateTemplateCandidate request, CancellationToken ct)
    {
        WorkflowCandidateValidationView Refuse(string state) => new(state, []);
        var templates = TemplateStoreAvailability.Require(store);
        var owners = catalogs.ToArray();
        if (owners.Length != 1) return Refuse("unavailable");
        var catalog = owners[0];
        if (!await catalog.CanReadAsync(ct)) return Refuse("denied");
        var current = await templates.FindAsync(request.TemplateId, ct);
        if (current is null) return Refuse("denied");
        if (string.IsNullOrWhiteSpace(request.ExpectedRevision) || current.Revision != request.ExpectedRevision) return Refuse("stale");
        var content = request.Candidate.ToContent();
        var visible = await catalog.ListAsync(ct);
        if (!visible.Allowed || !ActivityDataContextService.CatalogAllows(content.State.RootActivity, structures, visible, ct)) return Refuse("denied");
        var scope = ActivityDataContextService.CatalogScopeRevision(visible);
        var versions = await ActivityDataContextService.CatalogRevisionAsync(content.State.RootActivity, structures, lookup, ct);
        await credentials.AdmitAsync(content.State, ct);
        if (!await catalog.CanReadAsync(ct)) return Refuse("denied");
        var afterCatalog = await catalog.ListAsync(ct);
        if (!afterCatalog.Allowed) return Refuse("denied");
        if (ActivityDataContextService.CatalogScopeRevision(afterCatalog) != scope ||
            await ActivityDataContextService.CatalogRevisionAsync(content.State.RootActivity, structures, lookup, ct) != versions) return Refuse("stale");
        var after = await templates.FindAsync(request.TemplateId, ct);
        return after is null ? Refuse("denied") : after.Revision != current.Revision ? Refuse("stale") : Refuse("validated");
    }
}
