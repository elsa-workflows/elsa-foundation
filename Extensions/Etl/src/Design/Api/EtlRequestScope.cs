using Elsa.Etl.Core;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.AspNetCore.Http;

namespace Elsa.Etl.Design.Api;

internal sealed class EtlRequestScope(IHttpContextAccessor http, NormalizedPrincipalValidator principals, IPersistenceAccessContextAccessor persistence)
{
    public EtlScope Get()
    {
        if (http.HttpContext is not { } context || !principals.TryGetNormalizedPrincipal(context.User, out var principal))
            throw new EtlApiException(403, "etl.scope-unavailable");
        var tenants = principal.FindAll(IdentityClaimTypes.TenantId).Select(claim => claim.Value).ToArray();
        if (tenants.Length > 1)
            throw new EtlApiException(403, "etl.scope-unavailable");
        try { return new EtlScope(tenants.SingleOrDefault(), persistence.Current.RequireScope().Value); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { throw new EtlApiException(403, "etl.scope-unavailable"); }
    }
}
