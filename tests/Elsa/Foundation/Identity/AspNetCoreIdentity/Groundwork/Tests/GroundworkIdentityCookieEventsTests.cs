using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Authentication;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Persistence.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class GroundworkIdentityCookieEventsTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Authenticated_cookie_missing_a_required_binding_claim_is_rejected(
        bool includeSubject,
        bool includeTenant,
        bool includeSecurityStamp)
    {
        await using var fixture = AspNetCoreIdentityGroundworkAcceptanceFixture.Create();
        await using var scope = fixture.CreateScope();
        var context = CreateContext(
            scope.ServiceProvider,
            includeSubject,
            includeTenant,
            includeSecurityStamp);

        await scope.ServiceProvider.GetRequiredService<GroundworkIdentityCookieEvents>()
            .ValidatePrincipal(context);

        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task Cookie_tenant_that_conflicts_with_an_already_bound_request_scope_is_rejected()
    {
        await using var fixture = AspNetCoreIdentityGroundworkAcceptanceFixture.Create();
        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
            PersistenceAccessContext.Scoped(new PersistenceScope(AspNetCoreIdentityScenarioData.Ids.SecondaryTenant)));
        var context = CreateContext(
            scope.ServiceProvider,
            includeSubject: true,
            includeTenant: true,
            includeSecurityStamp: true);

        await scope.ServiceProvider.GetRequiredService<GroundworkIdentityCookieEvents>()
            .ValidatePrincipal(context);

        Assert.Null(context.Principal);
    }

    private static CookieValidatePrincipalContext CreateContext(
        IServiceProvider services,
        bool includeSubject,
        bool includeTenant,
        bool includeSecurityStamp)
    {
        var identity = new ClaimsIdentity(AspNetCoreIdentityDefaults.CookieScheme);
        if (includeSubject)
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, AspNetCoreIdentityScenarioData.Ids.PrimaryUser));
        if (includeTenant)
            identity.AddClaim(new Claim(IdentityClaimTypes.TenantId, AspNetCoreIdentityScenarioData.Ids.PrimaryTenant));
        if (includeSecurityStamp)
        {
            var stampClaimType = services.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity.SecurityStampClaimType;
            identity.AddClaim(new Claim(stampClaimType, "security-v1"));
        }

        var principal = new ClaimsPrincipal(identity);
        var scheme = new AuthenticationScheme(
            AspNetCoreIdentityDefaults.CookieScheme,
            AspNetCoreIdentityDefaults.CookieScheme,
            typeof(CookieAuthenticationHandler));
        var options = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AspNetCoreIdentityDefaults.CookieScheme);
        var ticket = new AuthenticationTicket(principal, scheme.Name);
        var httpContext = new DefaultHttpContext { RequestServices = services };
        return new CookieValidatePrincipalContext(httpContext, scheme, options, ticket);
    }
}
