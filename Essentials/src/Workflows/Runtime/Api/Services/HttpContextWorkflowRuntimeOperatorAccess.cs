using System.Security.Claims;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>
/// Evaluates <c>workflows/runtime:manage</c> for the request's caller with Foundation Identity's permission evaluator,
/// the caller's tenant and the run as the resource, as run deletion does. An anonymous or untrusted principal, or a run
/// of another tenant, gets <see langword="false"/>.
/// </summary>
public sealed class HttpContextWorkflowRuntimeOperatorAccess : IWorkflowRuntimeOperatorAccess
{
    private readonly IPermissionAuthorizationService _authorization;
    private readonly ClaimsPrincipal _principal;
    private readonly bool _trusted;
    private readonly string? _tenantId;

    public HttpContextWorkflowRuntimeOperatorAccess(
        IHttpContextAccessor httpContextAccessor,
        IPermissionAuthorizationService authorization,
        NormalizedPrincipalValidator principalValidator)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(principalValidator);
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));

        var rawPrincipal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        _trusted = principalValidator.TryGetNormalizedPrincipal(rawPrincipal, out var normalizedPrincipal);
        _principal = _trusted ? normalizedPrincipal : new ClaimsPrincipal(new ClaimsIdentity());
        _tenantId = _trusted
            ? _principal.FindFirst(IdentityClaimTypes.TenantId)?.Value ?? _principal.FindFirst("tenant_id")?.Value
            : null;
    }

    public async ValueTask<bool> CanManageAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflowExecution);
        if (!_trusted || (workflowExecution.TenantId is not null && !StringComparer.Ordinal.Equals(workflowExecution.TenantId, _tenantId)))
            return false;

        var result = await _authorization.AuthorizeAsync(
            new PermissionEvaluationContext(_principal, WorkflowRuntimePermissions.WorkflowRuntimeManage, _tenantId, workflowExecution),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }
}
