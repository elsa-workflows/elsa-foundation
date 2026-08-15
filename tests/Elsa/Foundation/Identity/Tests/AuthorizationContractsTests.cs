using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests;

public sealed class AuthorizationContractsTests
{
    [Theory]
    [InlineData("read", "Elsa.Permission:v1:s:UkVBRA")]
    [InlineData("re\u0301ad", "Elsa.Permission:v1:s:UsOJQUQ")]
    [InlineData("*", "Elsa.Permission:v1:s:Kg")]
    public void PermissionPolicyCodecFormatsCanonicalSingleFixtures(string permission, string expected)
    {
        var codec = new PermissionPolicyCodec();

        var policy = codec.Format(PermissionPolicyDescriptor.Single(permission));

        Assert.Equal(expected, policy);
    }

    [Fact]
    public void PermissionPolicyCodecCanonicalizesSortsAndDeduplicatesCompositeFixtures()
    {
        var codec = new PermissionPolicyCodec();

        var any = codec.Format(PermissionPolicyDescriptor.Any("write", "read", "READ"));
        var all = codec.Format(PermissionPolicyDescriptor.All("write", "read"));

        Assert.Equal("Elsa.Permission:v1:a:UkVBRA.V1JJVEU", any);
        Assert.Equal("Elsa.Permission:v1:l:UkVBRA.V1JJVEU", all);
    }

    [Theory]
    [InlineData("ELSA.PERMISSION:V1:S:UkVBRA")]
    [InlineData("elsa.permission:v1:S:UkVBRA")]
    [InlineData("elsa.permission:v1:s:UkVBRA")]
    [InlineData("Elsa.Permission:V1:s:cmVhZA")]
    [InlineData("Elsa.Permission:V1:s:UkVBRA")]
    [InlineData("Elsa.Permission:v1:a:")]
    public void PermissionPolicyCodecRejectsMalformedReservedVariantsWithoutLegacyFallback(string policyName)
    {
        var codec = new PermissionPolicyCodec();

        var result = codec.Parse(policyName);

        Assert.Equal(PermissionPolicyParseStatus.MalformedReservedPolicy, result.Status);
    }

    [Fact]
    public void PermissionPolicyCodecDistinguishesUnrelatedPolicies()
    {
        var result = new PermissionPolicyCodec().Parse("host.custom");

        Assert.Equal(PermissionPolicyParseStatus.NotPermission, result.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" read")]
    [InlineData("read ")]
    public void PermissionPolicyDescriptorRejectsInvalidMembers(string? permission)
    {
        Assert.ThrowsAny<ArgumentException>(() => PermissionPolicyDescriptor.Single(permission!));
    }

    [Fact]
    public void PermissionPolicyDescriptorsAndSetRequirementsAreImmutableAndRejectInvalidShapes()
    {
        var descriptor = PermissionPolicyDescriptor.Any("write", "read");
        var requirement = new PermissionSetAuthorizationRequirement(PermissionRequirementMode.Any, ["write", "read"]);

        Assert.Throws<NotSupportedException>(() => ((IList<string>)descriptor.Permissions).Add("DELETE"));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)requirement.Permissions).Add("DELETE"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PermissionSetAuthorizationRequirement(PermissionRequirementMode.Single, ["read"]));
        Assert.Throws<ArgumentException>(() =>
            new PermissionSetAuthorizationRequirement(PermissionRequirementMode.All, []));
    }

    [Fact]
    public void PermissionEndpointExtensionsRejectInvalidPermissionMetadata()
    {
        var builder = new TestEndpointConventionBuilder();

        Assert.Throws<ArgumentException>(() => builder.RequirePermission(" "));
        Assert.Throws<ArgumentException>(() => builder.RequireAnyPermission());
        Assert.Throws<ArgumentException>(() => builder.RequireAllPermissions());
        Assert.Throws<ArgumentNullException>(() =>
            PermissionEndpointConventionBuilderExtensions.RequirePermission<TestEndpointConventionBuilder>(null!, "read"));
    }

    [Fact]
    public async Task RequirePermissionPolicyProviderBuildsCanonicalCompositeRequirements()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions();
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var codec = provider.GetRequiredService<IPermissionPolicyCodec>();

        var policy = await policyProvider.GetPolicyAsync(codec.Format(PermissionPolicyDescriptor.Any("write", "read")));

        Assert.NotNull(policy);
        Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.Contains(policy.Requirements, requirement =>
            requirement.GetType().Name == "NormalizedPermissionPrincipalRequirement");
        var requirement = Assert.Single(policy.Requirements.OfType<PermissionSetAuthorizationRequirement>());
        Assert.Equal(PermissionRequirementMode.Any, requirement.Mode);
        Assert.Equal(["READ", "WRITE"], requirement.Permissions);
    }

    [Fact]
    public async Task RequirePermissionPolicyProviderRejectsMixedCaseMalformedV1WithoutCallingLegacyFormatter()
    {
        var services = new ServiceCollection();
        services.ReplacePermissionPolicyNameFormatter<CountingPolicyNameFormatter>();
        services.AddFoundationIdentityAbstractions();
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var formatter = Assert.IsType<CountingPolicyNameFormatter>(provider.GetRequiredService<IPermissionPolicyNameFormatter>());

        var policy = await policyProvider.GetPolicyAsync("eLsA.pErMiSsIoN:V1:s:cmVhZA");

        Assert.Null(policy);
        Assert.Equal(0, formatter.ParseCalls);
    }

    [Fact]
    public void PermissionEndpointExtensionsAttachOneCanonicalPolicyAndReturnSameBuilder()
    {
        var builder = new TestEndpointConventionBuilder();

        var returned = builder.RequireAnyPermission("write", "read");
        var endpointBuilder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/test"), 0);
        Assert.Single(builder.Conventions)(endpointBuilder);

        Assert.Same(builder, returned);
        var authorize = Assert.Single(endpointBuilder.Metadata.OfType<AuthorizeAttribute>());
        Assert.Equal("Elsa.Permission:v1:a:UkVBRA.V1JJVEU", authorize.Policy);
    }

    [Fact]
    public async Task RequirePermissionPolicyProviderBuildsPermissionRequirement()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions();
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var attribute = new RequirePermissionAttribute(DefaultIdentityPermissionKeys.IdentityUsersManage);

        var policy = await policyProvider.GetPolicyAsync(attribute.Policy!);

        var requirement = Assert.Single(policy!.Requirements.OfType<PermissionAuthorizationRequirement>());
        Assert.Equal(PermissionKey.Normalize(DefaultIdentityPermissionKeys.IdentityUsersManage), requirement.Permission);
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
    public void CompositePermissionCatalogPreservesDefaultIdentityPermissions()
    {
        var composite = BuildCompositeCatalog();
        var defaults = new DefaultIdentityPermissionCatalog().List();

        Assert.All(defaults, permission =>
        {
            var found = composite.Find(permission.Key);
            Assert.NotNull(found);
            Assert.Equal(permission, found);
        });
    }

    [Fact]
    public void CompositePermissionCatalogLayersContributedPermissions()
    {
        var composite = BuildCompositeCatalog(new HostControlContributor());

        var read = composite.Find(HostControlContributor.ReadKey);
        var manage = composite.Find(HostControlContributor.ManageKey);

        Assert.NotNull(read);
        Assert.NotNull(manage);
        Assert.Contains(HostControlContributor.ReadKey, manage!.Implies!);
        // Existing identity permissions remain listed alongside the contributions.
        Assert.NotNull(composite.Find(DefaultIdentityPermissionKeys.IdentityUsersRead));
    }

    [Fact]
    public async Task PermissionEvaluatorHonorsContributedImpliedPermissions()
    {
        var evaluator = new ClaimsPermissionEvaluator(BuildCompositeCatalog(new HostControlContributor()));
        var principal = PrincipalWithPermissions(HostControlContributor.ManageKey);

        var result = await evaluator.EvaluateAsync(new(principal, HostControlContributor.ReadKey));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void CompositePermissionCatalogRejectsContributionsShadowingIdentityPermissions()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BuildCompositeCatalog(new ShadowingContributor()));

        Assert.Contains(DefaultIdentityPermissionKeys.IdentityUsersRead, exception.Message);
        Assert.Contains("reserved identity permission", exception.Message);
    }

    [Fact]
    public void CompositePermissionCatalogRejectsDuplicateContributedKeys()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BuildCompositeCatalog(new HostControlContributor(), new HostControlContributor()));

        Assert.Contains(HostControlContributor.ReadKey, exception.Message);
        Assert.Contains("duplicate", exception.Message);
    }

    [Fact]
    public void AddPermissionContributorRegistersContributionIntoResolvedCatalog()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions();
        services.AddPermissionContributor<HostControlContributor>();
        using var provider = services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<IPermissionCatalog>();

        Assert.IsType<CompositePermissionCatalog>(catalog);
        Assert.NotNull(catalog.Find(HostControlContributor.ManageKey));
        Assert.NotNull(catalog.Find(DefaultIdentityPermissionKeys.IdentityUsersRead));
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
    public async Task PermissionAuthorizationHandlerPassesTenantContextAndDenialIsAHardVeto()
    {
        var services = new ServiceCollection();
        var observedTenants = new List<string?>();
        services.AddLogging();
        services.AddSingleton(observedTenants);
        services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "test" });
        services.AddScoped<IPermissionResourceHandler, DenyingTenantObserver>();
        services.AddScoped<IPermissionResourceHandler, SucceedingTenantObserver>();
        using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new(IdentityClaimTypes.TenantId, "tenant-claim"),
            new(IdentityClaimTypes.Normalized, "v1")
        ], "test"));
        var policyName = new PermissionPolicyNameFormatter().Format(DefaultIdentityPermissionKeys.IdentityUsersRead);

        var result = await authorization.AuthorizeAsync(principal, new TenantResource("tenant-resource"), policyName);

        Assert.False(result.Succeeded);
        Assert.Equal(["tenant-resource", "tenant-resource"], observedTenants);
    }

    [Fact]
    public async Task CompatibilityPermissionHandlerConstructorFailsClosedWithoutNormalizedPrincipalValidator()
    {
        var evaluator = new AlwaysGrantPermissionEvaluator();
        var handler = new PermissionAuthorizationHandler(evaluator, []);
        var requirement = new PermissionAuthorizationRequirement("read");
        var context = new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(IdentityClaimTypes.Permission, "read")],
                "raw-provider")),
            resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.Equal(0, evaluator.Calls);
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

    private static CompositePermissionCatalog BuildCompositeCatalog(params IPermissionContributor[] contributors) =>
        new([new DefaultIdentityPermissionCatalog(), .. contributors]);

    private static ClaimsPrincipal PrincipalWithPermissions(params string[] permissions) =>
        new(new ClaimsIdentity(permissions.Select(x => new Claim(IdentityClaimTypes.Permission, x))));

    private sealed class HostControlContributor : IPermissionContributor
    {
        public const string ReadKey = "test-feature.read";
        public const string ManageKey = "test-feature.manage";

        public IEnumerable<Permission> Contribute()
        {
            yield return new(ReadKey, "Read test feature", "Test feature", "Read.");
            yield return new(ManageKey, "Manage test feature", "Test feature", "Manage.", new HashSet<string> { ReadKey });
        }
    }

    private sealed class ShadowingContributor : IPermissionContributor
    {
        public IEnumerable<Permission> Contribute()
        {
            yield return new(DefaultIdentityPermissionKeys.IdentityUsersRead, "Shadow", "Test feature", "Attempts to shadow an identity permission.");
        }
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

    private sealed class AlwaysGrantPermissionEvaluator : IPermissionEvaluator
    {
        public int Calls { get; private set; }

        public ValueTask<PermissionEvaluationResult> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(PermissionEvaluationResult.Success);
        }
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

    private sealed class CountingPolicyNameFormatter : IPermissionPolicyNameFormatter
    {
        public int ParseCalls { get; private set; }

        public string Format(string permission) => $"custom:{permission}";

        public bool TryParse(string policyName, out string permission)
        {
            ParseCalls++;
            permission = string.Empty;
            return false;
        }
    }

    private sealed class TestEndpointConventionBuilder : IEndpointConventionBuilder
    {
        public IList<Action<EndpointBuilder>> Conventions { get; } = [];

        public void Add(Action<EndpointBuilder> convention) => Conventions.Add(convention);
    }
}
