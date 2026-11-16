using Elsa.Connections.Core.Contracts;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.AspNetCore.Http;

namespace Elsa.Connections.Design.Services;

/// <summary>
/// The validator's tenant is the principal's tenant when an HTTP caller is present, otherwise the
/// persistence partition already selected for the draft operation. Owner/run-as stays reserved (D5).
/// </summary>
public sealed class PersistenceScopeConnectionPublishTenantAccessor(
    IPersistenceAccessContextAccessor access,
    IHttpContextAccessor httpContextAccessor) : IConnectionPublishTenantAccessor
{
    public string? TenantId
    {
        get
        {
            var fromPrincipal = httpContextAccessor.HttpContext?.User.FindFirst(IdentityClaimTypes.TenantId)?.Value;
            if (!string.IsNullOrWhiteSpace(fromPrincipal))
                return fromPrincipal;

            return access.Current.Scope?.Value;
        }
    }
}
