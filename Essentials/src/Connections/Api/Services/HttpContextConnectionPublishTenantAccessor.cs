using Elsa.Connections.Core.Contracts;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Http;

namespace Elsa.Connections.Api.Services;

public sealed class HttpContextConnectionPublishTenantAccessor(IHttpContextAccessor httpContextAccessor) : IConnectionPublishTenantAccessor
{
    public string? TenantId =>
        httpContextAccessor.HttpContext?.User.FindFirst(IdentityClaimTypes.TenantId)?.Value;
}
