using Elsa.Tagging.Core.Contracts;
using Microsoft.AspNetCore.Http;

namespace Elsa.Tagging.Api.Services;

/// <summary>Bridges the host request boundary into catalog audit facts without leaking HTTP into the core domain.</summary>
internal sealed class HttpTagDefinitionAuditContext(IHttpContextAccessor accessor) : ITagDefinitionAuditContext
{
    private const string ElsaTenantClaim = "elsa.identity.tenant_id";
    private const string ConventionalTenantClaim = "tenant_id";
    private HttpContext? Context => accessor.HttpContext;

    public string Actor => Context?.User.Identity?.Name ?? "anonymous";
    public string? TenantId =>
        Context?.User.FindFirst(ElsaTenantClaim)?.Value
        ?? Context?.User.FindFirst(ConventionalTenantClaim)?.Value;
    public string CorrelationId => Context?.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? Context?.TraceIdentifier
        ?? "unavailable";
}
