using Elsa.Workflows.Publishing.Api.Contracts;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Publishing.Api.Services;

public sealed class HttpContextActivityPublishingAuthorizationContext(IHttpContextAccessor httpContextAccessor)
    : IActivityPublishingAuthorizationContext
{
    private const string ElsaTenantClaim = "elsa.identity.tenant_id";
    private const string ConventionalTenantClaim = "tenant_id";

    public string? TenantId =>
        httpContextAccessor.HttpContext?.User.FindFirst(ElsaTenantClaim)?.Value
        ?? httpContextAccessor.HttpContext?.User.FindFirst(ConventionalTenantClaim)?.Value;

    public bool CanAccessTenant(string? tenantId) =>
        tenantId is null || StringComparer.Ordinal.Equals(tenantId, TenantId);
}
