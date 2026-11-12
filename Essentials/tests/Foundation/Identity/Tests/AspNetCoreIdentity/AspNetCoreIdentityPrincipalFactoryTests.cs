using System.Security.Claims;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>
/// The external-provider principal factory projects the Elsa user over the provider's principal. The provider's own
/// subject must not stay first (#2848): tokens are issued and revoked under the first NameIdentifier.
/// </summary>
public sealed class AspNetCoreIdentityPrincipalFactoryTests
{
    private const string Tenant = "tenant-a";

    [Fact]
    public async Task The_Elsa_user_replaces_the_providers_subject_and_tenant_claims_instead_of_following_them()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IPrincipalFactory>();
        var external = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "provider-sub"),
            new Claim("sub", "provider-sub"),
            new Claim(IdentityClaimTypes.TenantId, "provider-tenant"),
            new Claim(IdentityClaimTypes.UserId, "forged-user"),
            new Claim(ClaimTypes.Name, "Ada"),
            new Claim(ClaimTypes.Email, "ada@example.com")
        ], "External"));

        var principal = await factory.CreateAsync(new PrincipalFactoryContext(Tenant, "idp", "provider-sub", external, [], []));

        var user = await scope.ServiceProvider.GetRequiredService<IExternalIdentityStore>().FindBySubjectAsync(Tenant, "idp", "provider-sub");
        Assert.NotNull(user);
        var identity = Assert.Single(principal.Identities);
        Assert.Equal(user!.UserId, Assert.Single(identity.FindAll(ClaimTypes.NameIdentifier)).Value);
        Assert.Equal(user.UserId, Assert.Single(identity.FindAll("sub")).Value);
        Assert.Equal(user.UserId, Assert.Single(identity.FindAll(IdentityClaimTypes.UserId)).Value);
        Assert.Equal(Tenant, Assert.Single(identity.FindAll(IdentityClaimTypes.TenantId)).Value);
        Assert.Equal("v1", Assert.Single(identity.FindAll(IdentityClaimTypes.Normalized)).Value);
        Assert.Equal(user.UserId, principal.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public async Task A_returning_external_subject_resolves_to_the_same_Elsa_user()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IPrincipalFactory>();
        var external = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "provider-sub")], "External"));

        var first = await factory.CreateAsync(new PrincipalFactoryContext(Tenant, "idp", "provider-sub", external, [], []));
        var second = await factory.CreateAsync(new PrincipalFactoryContext(Tenant, "idp", "provider-sub", external, [], []));

        Assert.Equal(first.FindFirstValue(IdentityClaimTypes.UserId), second.FindFirstValue(IdentityClaimTypes.UserId));
        Assert.Equal(first.FindFirstValue(ClaimTypes.NameIdentifier), second.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationAspNetCoreIdentity();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
