using System.Security.Claims;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>
/// The principal factory projects an admitted Elsa user from stored authority. External origin context must not
/// replace the canonical user identifiers or tenant (#2848), and repeated projection must not create users.
/// </summary>
public sealed class AspNetCoreIdentityPrincipalFactoryTests
{
    private const string Tenant = "tenant-a";

    [Fact]
    public async Task The_Elsa_user_owns_the_identifiers_and_tenant_even_with_external_origin_context()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IPrincipalFactory>();
        var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
        var user = new UserRecord("canonical-ada", Tenant, "ada", "ada@example.com", "Ada",
            UserStatus.Active, ResourceOwnership.External, new HashSet<string>(), new HashSet<string>());
        await users.SaveAsync(user);
        var principal = await factory.CreateAsync(new PrincipalFactoryContext(Tenant, user.Id, "idp")
        { ExternalIdentity = new ExternalIdentityKey(Tenant, "idp", "https://validated.example/", "provider-sub") });

        var identity = Assert.Single(principal.Identities);
        Assert.Equal(user.Id, Assert.Single(identity.FindAll(ClaimTypes.NameIdentifier)).Value);
        Assert.Equal(user.Id, Assert.Single(identity.FindAll("sub")).Value);
        Assert.Equal(user.Id, Assert.Single(identity.FindAll(IdentityClaimTypes.UserId)).Value);
        Assert.Equal(Tenant, Assert.Single(identity.FindAll(IdentityClaimTypes.TenantId)).Value);
        Assert.Equal("v1", Assert.Single(identity.FindAll(IdentityClaimTypes.Normalized)).Value);
        Assert.Equal(user.Id, principal.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public async Task Repeated_projection_keeps_the_admitted_Elsa_user()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IPrincipalFactory>();
        await scope.ServiceProvider.GetRequiredService<IUserStore>().SaveAsync(new UserRecord(
            "canonical-ada", Tenant, "ada", null, "Ada", UserStatus.Active, ResourceOwnership.External,
            new HashSet<string>(), new HashSet<string>()));
        var context = new PrincipalFactoryContext(Tenant, "canonical-ada", "idp")
        { ExternalIdentity = new ExternalIdentityKey(Tenant, "idp", "https://validated.example/", "provider-sub") };
        var first = await factory.CreateAsync(context);
        var second = await factory.CreateAsync(context);

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
