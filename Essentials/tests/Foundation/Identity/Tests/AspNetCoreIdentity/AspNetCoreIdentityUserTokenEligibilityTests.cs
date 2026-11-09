using Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;
using Elsa.Foundation.Identity.Authentication;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>Token refresh asks the identity provider whether the user may still hold tokens (#2840); the answer is read now.</summary>
public sealed class AspNetCoreIdentityUserTokenEligibilityTests
{
    private const string Tenant = "tenant-a";

    [Fact]
    public async Task An_active_user_is_eligible_until_it_is_disabled_and_again_once_re_enabled()
    {
        await using var provider = Provider();
        var users = provider.GetRequiredService<IUserStore>();
        await users.SaveAsync(User("ada"));

        Assert.True(await IsEligibleAsync(provider, "ada"));

        await users.SetStatusAsync(Tenant, "ada", UserStatus.Disabled);
        Assert.False(await IsEligibleAsync(provider, "ada"));

        await users.SetStatusAsync(Tenant, "ada", UserStatus.Active);
        Assert.True(await IsEligibleAsync(provider, "ada"));
    }

    [Fact]
    public async Task A_deleted_missing_or_foreign_tenant_user_is_not_eligible()
    {
        await using var provider = Provider();
        var users = provider.GetRequiredService<IUserStore>();
        await users.SaveAsync(User("ada"));
        await users.SaveAsync(User("bob") with { TenantId = "tenant-b" });

        Assert.True(await users.DeleteAsync(Tenant, "ada"));

        Assert.False(await IsEligibleAsync(provider, "ada"));
        Assert.False(await IsEligibleAsync(provider, "missing"));
        Assert.False(await IsEligibleAsync(provider, "bob"), "a user of another tenant is not this tenant's user");
    }

    [Fact]
    public async Task Hosts_without_an_identity_provider_fail_closed()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions();
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<UnsupportedUserTokenEligibility>(provider.CreateScope().ServiceProvider.GetRequiredService<IUserTokenEligibility>());
        Assert.False(await IsEligibleAsync(provider, "ada"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_host_replacement_wins_over_the_provider_default_in_either_order(bool hostFirst)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (hostFirst) services.ReplaceUserTokenEligibility<AdmitEveryone>();
        services.AddFoundationAspNetCoreIdentity();
        if (!hostFirst) services.ReplaceUserTokenEligibility<AdmitEveryone>();
        await using var provider = services.BuildServiceProvider();

        Assert.Single(services, x => x.ServiceType == typeof(IUserTokenEligibility));
        Assert.True(await IsEligibleAsync(provider, "nobody"));
    }

    private sealed class AdmitEveryone : IUserTokenEligibility
    {
        public ValueTask<bool> IsEligibleAsync(string tenantId, string userId, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private static UserRecord User(string id) =>
        new(id, Tenant, id, null, id, UserStatus.Active, ResourceOwnership.Foundation, new HashSet<string>(), new HashSet<string>());

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationAspNetCoreIdentity();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<bool> IsEligibleAsync(ServiceProvider provider, string userId)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IUserTokenEligibility>().IsEligibleAsync(Tenant, userId);
    }
}
