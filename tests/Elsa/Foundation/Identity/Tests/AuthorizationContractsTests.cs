using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    [Fact]
    public async Task PermissionEvaluatorDoesNotExpandRequestedPermission()
    {
        var evaluator = new ClaimsPermissionEvaluator(new DefaultIdentityPermissionCatalog());
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new(IdentityClaimTypes.Permission, DefaultIdentityPermissionKeys.IdentityUsersRead)
        ]));

        var result = await evaluator.EvaluateAsync(new(principal, DefaultIdentityPermissionKeys.IdentityUsersManage));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task PermissionEvaluatorExpandsGrantedPermissionsTransitively()
    {
        var evaluator = new ClaimsPermissionEvaluator(new TestPermissionCatalog(
        [
            new("identity.admin", "Admin", "Identity", "All identity permissions.", new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersManage }),
            new(DefaultIdentityPermissionKeys.IdentityUsersManage, "Manage users", "Identity", "Manage users.", new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersRead }),
            new(DefaultIdentityPermissionKeys.IdentityUsersRead, "Read users", "Identity", "Read users.")
        ]));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new(IdentityClaimTypes.Permission, "identity.admin")
        ]));

        var result = await evaluator.EvaluateAsync(new(principal, DefaultIdentityPermissionKeys.IdentityUsersRead));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task PermissionAuthorizationHandlerPassesTenantContextAndDoesNotShadowLaterSuccess()
    {
        var services = new ServiceCollection();
        var observedTenants = new List<string?>();
        services.AddLogging();
        services.AddSingleton(observedTenants);
        services.AddFoundationIdentityAbstractions();
        services.AddScoped<IPermissionResourceHandler, DenyingTenantObserver>();
        services.AddScoped<IPermissionResourceHandler, SucceedingTenantObserver>();
        using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new(IdentityClaimTypes.TenantId, "tenant-claim")
        ], "test"));
        var policyName = new PermissionPolicyNameFormatter().Format(DefaultIdentityPermissionKeys.IdentityUsersRead);

        var result = await authorization.AuthorizeAsync(principal, new TenantResource("tenant-resource"), policyName);

        Assert.True(result.Succeeded);
        Assert.Equal(["tenant-resource", "tenant-resource"], observedTenants);
    }

    [Fact]
    public async Task RequirePermissionPolicyProviderDelegatesNonPermissionPolicies()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthorizationPolicyProvider, TestPolicyProvider>();
        services.AddFoundationIdentityAbstractions();
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        var policy = await policyProvider.GetPolicyAsync("custom");

        Assert.NotNull(policy);
        Assert.Contains(policy.Requirements, x => x is TestRequirement);
    }

    [Fact]
    public async Task RequirePermissionPolicyProviderPreservesScopedFallbackProviderLifetime()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedPolicyDependency>();
        services.AddScoped<IAuthorizationPolicyProvider, ScopedPolicyProvider>();
        services.AddFoundationIdentityAbstractions();
        var descriptor = services.Last(x => x.ServiceType == typeof(IAuthorizationPolicyProvider));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var policyProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        var policy = await policyProvider.GetPolicyAsync("scoped");

        Assert.NotNull(policy);
        Assert.Contains(policy.Requirements, x => x is TestRequirement);
    }

    private sealed class TestPermissionCatalog(IReadOnlyCollection<Permission> permissions) : IPermissionCatalog
    {
        public IReadOnlyCollection<Permission> List() => permissions;

        public Permission? Find(string key) => permissions.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record TenantResource(string TenantId);

    private sealed class DenyingTenantObserver(List<string?> observedTenants) : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            observedTenants.Add(context.TenantId);
            return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Denied());
        }
    }

    private sealed class SucceedingTenantObserver(List<string?> observedTenants) : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            observedTenants.Add(context.TenantId);
            return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Success);
        }
    }

    private sealed record TestRequirement : IAuthorizationRequirement;

    private sealed class TestPolicyProvider : IAuthorizationPolicyProvider
    {
        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
            Task.FromResult<AuthorizationPolicy?>(policyName == "custom"
                ? new AuthorizationPolicyBuilder().AddRequirements(new TestRequirement()).Build()
                : null);

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
            new DefaultAuthorizationPolicyProvider(Options.Create(new AuthorizationOptions())).GetDefaultPolicyAsync();

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => Task.FromResult<AuthorizationPolicy?>(null);
    }

    private sealed class ScopedPolicyDependency;

    private sealed class ScopedPolicyProvider(ScopedPolicyDependency dependency) : IAuthorizationPolicyProvider
    {
        private readonly ScopedPolicyDependency _dependency = dependency;

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
            Task.FromResult<AuthorizationPolicy?>(policyName == "scoped" && _dependency is not null
                ? new AuthorizationPolicyBuilder().AddRequirements(new TestRequirement()).Build()
                : null);

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
            new DefaultAuthorizationPolicyProvider(Options.Create(new AuthorizationOptions())).GetDefaultPolicyAsync();

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => Task.FromResult<AuthorizationPolicy?>(null);
    }
}
