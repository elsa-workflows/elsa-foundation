using System.Security.Claims;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Api.Services.Alterations;

/// <summary>Uses the immutable persistence scope as the alteration tenant boundary and captures only safe operator facts.</summary>
public sealed class HttpContextWorkflowAlterationRequestContext(
    IHttpContextAccessor httpContextAccessor,
    IPersistenceAccessContextAccessor persistenceAccessContextAccessor,
    IPermissionAuthorizationService authorization,
    NormalizedPrincipalValidator principalValidator) : IWorkflowAlterationRequestContext
{
    private const string SystemIdentity = "runtime-api";
    private HttpContext? HttpContext => httpContextAccessor.HttpContext;
    private string TenantPartition => persistenceAccessContextAccessor.Current.Scope?.Value ?? "global";

    public WorkflowAlterationAuthorityScope AuthorityScope => new(
        TenantPartition,
        SystemIdentity,
        SystemIdentity);

    public WorkflowAlterationOperatorProvenance Operator => new(Subject, HttpContext?.TraceIdentifier);

    public bool CanAccess(WorkflowAlterationPlanState plan) =>
        plan is not null &&
        StringComparer.Ordinal.Equals(plan.AuthorityScope.TenantPartition, TenantPartition) &&
        StringComparer.Ordinal.Equals(plan.AuthorityScope.SystemIdentity, SystemIdentity) &&
        StringComparer.Ordinal.Equals(plan.AuthorityScope.RootInitiator, SystemIdentity) &&
        WorkflowExecutionAuthoritySnapshot.MetadataEquals(plan.AuthorityScope.Metadata, AuthorityScope.Metadata);

    public async ValueTask<bool> CanManageAsync(WorkflowExecutionState execution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        // The same decision run deletion makes for each run: runtime manage, with the run's state as the resource, so
        // a host's IPermissionResourceHandler can refuse one run while allowing another.
        var rawPrincipal = HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        if (!principalValidator.TryGetNormalizedPrincipal(rawPrincipal, out var principal))
            return false;
        var tenantId = principal.FindFirst(IdentityClaimTypes.TenantId)?.Value ?? principal.FindFirst("tenant_id")?.Value;
        var decision = await authorization.AuthorizeAsync(
            new PermissionEvaluationContext(principal, WorkflowRuntimePermissions.WorkflowRuntimeManage, tenantId, execution),
            cancellationToken);
        return decision.Succeeded;
    }

    private string Subject
    {
        get
        {
            var principal = HttpContext?.User;
            var subject = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal?.FindFirst("sub")?.Value;
            return string.IsNullOrWhiteSpace(subject) ? "authenticated-operator" : subject;
        }
    }
}
