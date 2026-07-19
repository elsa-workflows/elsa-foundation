using Elsa.Api.FastEndpoints.Constants;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Design.Api.Services;

public sealed class HttpWorkflowDefinitionTagAuthorizationContext(IHttpContextAccessor accessor)
    : IWorkflowDefinitionTagAuthorizationContext
{
    private const string PermissionClaim = "elsa.identity.permission";
    private HttpContext? HttpContext => accessor.HttpContext;

    public string ActorId => HttpContext?.User.Identity?.Name ?? "anonymous";

    public string CorrelationId =>
        HttpContext?.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? HttpContext?.TraceIdentifier
        ?? Guid.NewGuid().ToString("N");

    public bool CanAssign =>
        HasPermission(PermissionNames.WorkflowDesignRead)
        && HasPermission(PermissionNames.WorkflowDesignTagsAssign);

    private bool HasPermission(string permission)
    {
        var values = HttpContext?.User.FindAll(PermissionClaim).Select(x => x.Value).ToArray() ?? [];
        return values.Contains(PermissionNames.All, StringComparer.Ordinal)
               || values.Contains(permission, StringComparer.Ordinal);
    }
}
