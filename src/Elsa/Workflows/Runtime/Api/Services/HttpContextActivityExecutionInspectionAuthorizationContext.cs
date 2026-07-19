using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>Fail-closed HTTP authorization adapter with independent structure and captured-value grants.</summary>
public sealed class HttpContextActivityExecutionInspectionAuthorizationContext(IHttpContextAccessor httpContextAccessor)
    : IActivityExecutionInspectionAuthorizationContext
{
    public const string StructurePermission = "workflows.activity-executions.inspect";
    public const string SensitiveValuesPermission = "workflows.activity-executions.inspect-values";
    public const string ResolveValuePayloadsPermission = "workflows.activity-executions.resolve-value-payloads";
    private const string ElsaTenantClaim = "elsa.identity.tenant_id";
    private const string ConventionalTenantClaim = "tenant_id";
    private const string PermissionClaim = "elsa.identity.permission";

    private HttpContext? HttpContext => httpContextAccessor.HttpContext;
    private string? TenantId =>
        HttpContext?.User.FindFirst(ElsaTenantClaim)?.Value
        ?? HttpContext?.User.FindFirst(ConventionalTenantClaim)?.Value;

    public string TenantScope => TenantId is null ? "global" : $"tenant:{TenantId}";
    public string AuditSubject
    {
        get
        {
            var principal = HttpContext?.User;
            var subject = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrWhiteSpace(subject))
                return subject;

            subject = principal?.FindFirst("sub")?.Value;
            return string.IsNullOrWhiteSpace(subject) ? string.Empty : subject;
        }
    }
    public string RequestCorrelationId => HttpContext?.TraceIdentifier ?? string.Empty;

    public string AuthorizationProfile
    {
        get
        {
            var material = $"{TenantScope}|structure:{HasPermission(StructurePermission)}|values:{HasPermission(SensitiveValuesPermission)}|resolve:{HasPermission(ResolveValuePayloadsPermission)}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        }
    }

    public bool CanInspectStructure(WorkflowExecutionState workflowExecution) =>
        CanAccess(workflowExecution) && HasPermission(StructurePermission);

    public bool CanInspectSensitiveValues(WorkflowExecutionState workflowExecution) =>
        CanInspectStructure(workflowExecution) && HasPermission(SensitiveValuesPermission);

    public bool CanResolveSensitiveValuePayloads(WorkflowExecutionState workflowExecution) =>
        CanInspectSensitiveValues(workflowExecution) && HasPermission(ResolveValuePayloadsPermission);

    private bool CanAccess(WorkflowExecutionState workflowExecution) =>
        workflowExecution.TenantId is null || StringComparer.Ordinal.Equals(workflowExecution.TenantId, TenantId);

    private bool HasPermission(string permission)
    {
        var values = HttpContext?.User.FindAll(PermissionClaim).Select(x => x.Value).ToArray() ?? [];
        return values.Contains("*", StringComparer.Ordinal) || values.Contains(permission, StringComparer.Ordinal);
    }
}
