using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Api.Services;

// Occurrence windows can outlive a request's sign-in snapshot. Intersect current persisted grants
// with that request's ceiling before admission and again before disclosing a pending result.
internal sealed class RuntimeHistoryOccurrenceAuthorization(IHttpContextAccessor accessor,
    NormalizedPrincipalValidator validator, IPermissionAuthorizationService permissions,
    ICurrentSubjectPermissionAuthorizer current)
{
    internal async ValueTask<string?> BindAsync(WorkflowExecutionState workflow, string requestBinding, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (accessor.HttpContext is not { } http || !validator.TryGetNormalizedPrincipal(http.User, out var principal))
            throw new RuntimeResourceMissingSignal();
        var tenants = principal.FindAll(IdentityClaimTypes.TenantId).Select(claim => claim.Value).ToArray();
        var subjects = principal.FindAll(IdentityClaimTypes.UserId).Select(claim => claim.Value).ToArray();
        if (tenants.Length != 1 || subjects.Length != 1 || principal.FindAll(IdentityClaimTypes.SubjectProvider).Any() ||
            string.IsNullOrWhiteSpace(tenants[0]) || string.IsNullOrWhiteSpace(subjects[0]))
            return null; // NameIdentifier/sub may belong to an external provider; only its owner can assert an Elsa UserId.
        var tenant = tenants[0]; var subject = subjects[0];
        if (workflow.TenantId is not null && workflow.TenantId != tenant) throw new RuntimeResourceMissingSignal();
        var names = new[] { WorkflowRuntimePermissions.WorkflowRuntimeRead,
            HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission,
            HttpContextActivityExecutionInspectionAuthorizationContext.SensitiveValuesPermission,
            HttpContextActivityExecutionInspectionAuthorizationContext.ResolveValuePayloadsPermission };
        var effective = new bool[names.Length];
        for (var index = 0; index < names.Length; index++)
        {
            var ceiling = await permissions.AuthorizeAsync(new(principal, names[index], tenant, workflow), token);
            if (!ceiling.Succeeded)
            {
                if (index < 2) throw new RuntimeResourceMissingSignal();
                continue;
            }
            var decision = await current.AuthorizeAsync(new(tenant, subject, names[index], workflow), token);
            if (decision.Status == CurrentSubjectPermissionStatus.Unsupported) return null;
            if (decision.Status == CurrentSubjectPermissionStatus.Denied)
            {
                if (index < 2) throw new RuntimeResourceMissingSignal();
                continue;
            }
            if (decision.Status != CurrentSubjectPermissionStatus.Granted) return null;
            effective[index] = true;
        }
        // A payload grant cannot bypass the independent captured-value grant.
        effective[3] &= effective[2];
        return RuntimeHistoryIdentity.CreateBinding([requestBinding, tenant, subject,
            effective[2] ? "values" : "structure", effective[3] ? "payloads" : "no-payloads"]);
    }
}
