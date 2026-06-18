using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests;

public sealed class AuthorizationContractsTests
{
    [Fact]
    public async Task RequirePermissionPolicyProviderBuildsPermissionRequirement()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions();
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var attribute = new RequirePermissionAttribute(DefaultIdentityPermissionKeys.IdentityUsersManage);

        var policy = await policyProvider.GetPolicyAsync(attribute.Policy!);

        var requirement = Assert.IsType<PermissionAuthorizationRequirement>(Assert.Single(policy!.Requirements));
        Assert.Equal(DefaultIdentityPermissionKeys.IdentityUsersManage, requirement.Permission);
    }

    [Fact]
    public async Task PermissionEvaluatorTreatsGrantedPermissionImplicationsAsEffectivePermissions()
    {
        var evaluator = new ClaimsPermissionEvaluator(new DefaultIdentityPermissionCatalog());
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new(IdentityClaimTypes.Permission, DefaultIdentityPermissionKeys.IdentityUsersManage)
        ]));

        var result = await evaluator.EvaluateAsync(new(principal, DefaultIdentityPermissionKeys.IdentityUsersRead));

        Assert.True(result.Succeeded);
    }
}
