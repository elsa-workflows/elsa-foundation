using Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>Current-subject decisions are re-derived from current IAM records, never from an earlier grant.</summary>
public sealed class AspNetCoreIdentityCurrentSubjectPermissionTests
{
    private const string Tenant = "tenant-a";
    private const string Granted = "editorial-reviews:decide";

    [Fact]
    public async Task Role_membership_and_direct_grants_are_read_now_and_revocation_takes_effect_at_once()
    {
        await using var provider = Provider();
        var users = provider.GetRequiredService<IUserStore>();
        var roles = provider.GetRequiredService<IRoleStore>();
        var memberships = provider.GetRequiredService<ITenantMembershipStore>();
        await roles.SaveAsync(new("reviewers", Tenant, "Reviewers", null, new HashSet<string> { Granted }, false));
        var user = new UserRecord("ada", Tenant, "ada", null, "Ada", UserStatus.Active, ResourceOwnership.Foundation,
            new HashSet<string> { "reviewers" }, new HashSet<string>());
        await users.SaveAsync(user);

        Assert.Equal(CurrentSubjectPermissionStatus.Granted, (await AuthorizeAsync(provider, "ada")).Status);

        await roles.SaveAsync(new("reviewers", Tenant, "Reviewers", null, new HashSet<string>(), false));
        Assert.Equal(CurrentSubjectPermissionStatus.Denied, (await AuthorizeAsync(provider, "ada")).Status);

        await memberships.SaveAsync(new(Tenant, "ada", TenantMembershipStatus.Active, new HashSet<string>(), new HashSet<string> { Granted }));
        Assert.Equal(CurrentSubjectPermissionStatus.Granted, (await AuthorizeAsync(provider, "ada")).Status);

        await memberships.SaveAsync(new(Tenant, "ada", TenantMembershipStatus.Suspended, new HashSet<string>(), new HashSet<string> { Granted }));
        Assert.Equal(CurrentSubjectPermissionStatus.Denied, (await AuthorizeAsync(provider, "ada")).Status);

        await users.SaveAsync(user with { DirectPermissions = new HashSet<string> { Granted } });
        Assert.Equal(CurrentSubjectPermissionStatus.Granted, (await AuthorizeAsync(provider, "ada")).Status);

        await users.SetStatusAsync(Tenant, "ada", UserStatus.Disabled);
        Assert.Equal(CurrentSubjectPermissionStatus.Denied, (await AuthorizeAsync(provider, "ada")).Status);
    }

    [Fact]
    public async Task Missing_and_foreign_subjects_are_denied()
    {
        await using var provider = Provider();
        await provider.GetRequiredService<IUserStore>().SaveAsync(new UserRecord("ada", "tenant-b", "ada", null, "Ada",
            UserStatus.Active, ResourceOwnership.Foundation, new HashSet<string>(), new HashSet<string> { Granted }));

        Assert.Equal(CurrentSubjectPermissionStatus.Denied, (await AuthorizeAsync(provider, "missing")).Status);
        Assert.Equal(CurrentSubjectPermissionStatus.Denied, (await AuthorizeAsync(provider, "ada")).Status);
    }

    [Fact]
    public async Task Hosts_without_a_current_subject_provider_fail_closed()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions();
        await using var provider = services.BuildServiceProvider();

        var result = await AuthorizeAsync(provider, "ada");

        Assert.Equal(CurrentSubjectPermissionStatus.Unsupported, result.Status);
        Assert.IsType<UnsupportedCurrentSubjectPermissionAuthorizer>(provider.CreateScope().ServiceProvider.GetRequiredService<ICurrentSubjectPermissionAuthorizer>());
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationAspNetCoreIdentity();
        services.AddFoundationAspNetCoreIdentity();
        services.AddPermissionContributor<EditorialPermissions>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<CurrentSubjectPermissionResult> AuthorizeAsync(ServiceProvider provider, string subject)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICurrentSubjectPermissionAuthorizer>()
            .AuthorizeAsync(new(Tenant, subject, Granted));
    }

    private sealed class EditorialPermissions : IPermissionContributor
    {
        public IEnumerable<Permission> Contribute() => [new(Granted, "Decide reviews", "Editorial", "Decide editorial reviews.")];
    }
}
