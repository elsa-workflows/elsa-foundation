using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

public sealed class AspNetCoreIdentityGroundworkIsolationTests
{
    [Fact]
    public async Task Ef_only_composition_retains_its_existing_cookie_and_principal_behavior()
    {
        await using var fixture = new AspNetCoreIdentityFixture();
        await using var scope = fixture.CreateScope();

        Assert.Empty(scope.ServiceProvider.GetServices<IAuthenticationSessionInvalidator>());
        var cookieOptions = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AspNetCoreIdentityDefaults.CookieScheme);
        Assert.Null(cookieOptions.EventsType);

        var principalFactory = scope.ServiceProvider
            .GetRequiredService<IUserClaimsPrincipalFactory<AspNetCoreIdentityUser>>();
        Assert.IsType<AspNetCoreIdentityUserClaimsPrincipalFactory>(principalFactory);
        var user = new AspNetCoreIdentityUser
        {
            Id = "ef-isolation-user",
            TenantId = AspNetCoreIdentityDefaults.DefaultTenantId,
            UserName = "ef-isolation-user",
            SecurityStamp = "must-not-be-projected"
        };

        var principal = await principalFactory.CreateAsync(user);
        var securityStampClaimType = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>()
            .Value.ClaimsIdentity.SecurityStampClaimType;
        Assert.DoesNotContain(principal.Claims, claim => claim.Type == securityStampClaimType);
    }
}
