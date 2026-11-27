using System.Security.Claims;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Models;
using Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed record KernelRunOperatorCaller(ClaimsPrincipal Principal, string? Tenant)
{
    internal bool Matches(BoundScope scope) => StringComparer.Ordinal.Equals(Tenant ?? PersistenceScope.DefaultValue, scope.Tenant);

    internal static KernelRunOperatorCaller? Read(ClaimsPrincipal raw, NormalizedPrincipalValidator validator)
    {
        if (!validator.TryGetNormalizedPrincipal(raw, out var principal) ||
            !TryReadClaim(principal, IdentityClaimTypes.TenantId, out var canonical) ||
            !TryReadClaim(principal, "tenant_id", out var legacy) ||
            canonical is not null && legacy is not null && !StringComparer.Ordinal.Equals(canonical, legacy))
            return null;
        return new(principal, canonical ?? legacy);
    }

    private static bool TryReadClaim(ClaimsPrincipal principal, string type, out string? value)
    {
        var claims = principal.FindAll(type).Take(2).ToArray();
        value = claims is [var only] ? only.Value : null;
        return claims.Length == 0 || !string.IsNullOrWhiteSpace(value);
    }
}
