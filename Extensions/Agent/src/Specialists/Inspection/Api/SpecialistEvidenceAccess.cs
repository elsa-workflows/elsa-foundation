using System.Security.Claims;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Agent.Specialists.Inspection.Api;

/// <summary>What the caller may see of one workflow run's specialist evidence.</summary>
internal readonly record struct SpecialistEvidenceGrant(bool Found, bool Structure, bool Values);

/// <summary>
/// Re-authorizes evidence per workflow run with the runtime's own inspection permissions: structure for the journal facts,
/// and captured values for the accepted result. Seeing a run, or holding the Test Run permission, does not imply its values.
/// </summary>
internal sealed class SpecialistEvidenceAccess(IHttpContextAccessor http, NormalizedPrincipalValidator principals,
    IPermissionAuthorizationService authorization, IWorkflowExecutionStateStore workflows)
{
    // Owned by the Workflows Runtime API (HttpContextActivityExecutionInspectionAuthorizationContext).
    internal const string InspectStructure = "workflows/activity-executions:inspect";
    internal const string InspectValues = "workflows/activity-executions:inspect-values";

    public async ValueTask<SpecialistEvidenceGrant> AuthorizeAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var workflow = await workflows.FindAsync(workflowExecutionId, cancellationToken);
        if (workflow is null) return new(false, false, false);
        // An untrusted caller learns nothing, not even that the run exists.
        if (http.HttpContext is not { } context || !principals.TryGetNormalizedPrincipal(context.User, out var principal)) return new(false, false, false);
        var tenant = principal.FindFirst(IdentityClaimTypes.TenantId)?.Value ?? principal.FindFirst("tenant_id")?.Value;
        if (workflow.TenantId is not null && !StringComparer.Ordinal.Equals(workflow.TenantId, tenant)) return new(false, false, false);
        var structure = await GrantedAsync(principal, InspectStructure, tenant, workflow, cancellationToken);
        var values = structure && await GrantedAsync(principal, InspectValues, tenant, workflow, cancellationToken);
        return new(true, structure, values);
    }

    /// <summary>Keeps the capture decision but drops the value itself when values are not granted.</summary>
    public static SpecialistInvocationEvidence? Redact(SpecialistInvocationEvidence? evidence, bool values) =>
        !values && evidence?.Output is { Value: not null } output ? evidence with { Output = output with { Value = null } } : evidence;

    private async ValueTask<bool> GrantedAsync(ClaimsPrincipal principal, string permission, string? tenant, WorkflowExecutionState workflow, CancellationToken cancellationToken) =>
        (await authorization.AuthorizeAsync(new PermissionEvaluationContext(principal, permission, tenant, workflow) { CancellationToken = cancellationToken }, cancellationToken)).Succeeded;
}
