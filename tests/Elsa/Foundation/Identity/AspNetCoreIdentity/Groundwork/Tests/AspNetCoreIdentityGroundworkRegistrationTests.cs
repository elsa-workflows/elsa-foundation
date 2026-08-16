using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Authentication;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ElsaExternalIdentityStore = Elsa.Foundation.Identity.Abstractions.Iam.IExternalIdentityStore;
using ElsaRoleStore = Elsa.Foundation.Identity.Abstractions.Iam.IRoleStore;
using ElsaTenantMembershipStore = Elsa.Foundation.Identity.Abstractions.Iam.ITenantMembershipStore;
using ElsaUserStore = Elsa.Foundation.Identity.Abstractions.Iam.IUserStore;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityGroundworkRegistrationTests
{
    private const string AssemblyName = "Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork";
    private const string FeatureTypeName = "Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.AspNetCoreIdentityGroundworkFeature";
    private const string RegistrationTypeName = "Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection.AspNetCoreIdentityGroundworkRegistration";
    private const string RegistrationMethodName = "AddFoundationAspNetCoreIdentityGroundwork";

    [Fact]
    public void Feature_type_is_public_non_sealed_and_discoverable()
    {
        var featureType = RequiredType(FeatureTypeName);

        Assert.True(featureType.IsPublic);
        Assert.False(featureType.IsSealed);
    }

    [Fact]
    public void Registration_exposes_the_exact_framework_capability_denominator()
    {
        var services = RegisterGroundworkIdentity();

        AssertRegistered<IUserStore<AspNetCoreIdentityUser>>(services);
        AssertRegistered<IUserPasswordStore<AspNetCoreIdentityUser>>(services);
        AssertRegistered<IUserSecurityStampStore<AspNetCoreIdentityUser>>(services);
        AssertRegistered<IUserEmailStore<AspNetCoreIdentityUser>>(services);
        AssertRegistered<IUserLockoutStore<AspNetCoreIdentityUser>>(services);
        AssertRegistered<IUserPhoneNumberStore<AspNetCoreIdentityUser>>(services);
        AssertRegistered<IUserTwoFactorStore<AspNetCoreIdentityUser>>(services);
        AssertRegistered<IUserLoginStore<AspNetCoreIdentityUser>>(services);
        AssertRegistered<IUserClaimStore<AspNetCoreIdentityUser>>(services);
        AssertRegistered<IUserRoleStore<AspNetCoreIdentityUser>>(services);
        AssertRegistered<IUserAuthenticationTokenStore<AspNetCoreIdentityUser>>(services);
        AssertRegistered<IUserAuthenticatorKeyStore<AspNetCoreIdentityUser>>(services);
        AssertRegistered<IUserTwoFactorRecoveryCodeStore<AspNetCoreIdentityUser>>(services);
        AssertRegistered<IRoleStore<IdentityRole>>(services);
        AssertRegistered<IRoleClaimStore<IdentityRole>>(services);
    }

    [Fact]
    public void Unsupported_framework_capabilities_are_not_registered()
    {
        var services = RegisterGroundworkIdentity();

        AssertNotRegistered<IQueryableUserStore<AspNetCoreIdentityUser>>(services);
        AssertNotRegistered<IQueryableRoleStore<IdentityRole>>(services);
        AssertNotRegistered<IUserPasskeyStore<AspNetCoreIdentityUser>>(services);
        AssertNotRegistered<IProtectedUserStore<AspNetCoreIdentityUser>>(services);
    }

    [Fact]
    public void Framework_and_elsa_store_adapters_are_scoped()
    {
        var services = RegisterGroundworkIdentity();

        AssertScoped<IUserStore<AspNetCoreIdentityUser>>(services);
        AssertScoped<IRoleStore<IdentityRole>>(services);
        AssertScoped<ElsaUserStore>(services);
        AssertScoped<ElsaRoleStore>(services);
        AssertScoped<ElsaExternalIdentityStore>(services);
        AssertScoped<ElsaTenantMembershipStore>(services);
        AssertScoped<GroundworkIdentityAuthorityAggregateCoordinator>(services);
    }

    [Fact]
    public void Shared_email_uniqueness_policy_mirrors_aspnet_identity_options()
    {
        var services = RegisterGroundworkIdentity();
        services.Configure<IdentityOptions>(options => options.User.RequireUniqueEmail = true);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.True(scope.ServiceProvider.GetRequiredService<IIdentityEmailUniquenessPolicy>().RequireUniqueEmail);
    }

    [Fact]
    public void Groundwork_registration_owns_session_invalidation_and_cookie_replay_validation()
    {
        var services = RegisterGroundworkIdentity();
        var invalidator = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IAuthenticationSessionInvalidator));
        Assert.Equal(typeof(GroundworkIdentitySessionInvalidator), invalidator.ImplementationType);
        using var provider = services.BuildServiceProvider();

        var cookieOptions = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AspNetCoreIdentityDefaults.CookieScheme);
        Assert.Equal(typeof(GroundworkIdentityCookieEvents), cookieOptions.EventsType);
    }

    [Fact]
    public void Groundwork_registration_rejects_a_conflicting_host_cookie_events_type_instead_of_overwriting_it()
    {
        var services = RegisterGroundworkIdentity();
        services.Configure<CookieAuthenticationOptions>(
            AspNetCoreIdentityDefaults.CookieScheme,
            options => options.EventsType = typeof(HostCookieEvents));
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(AspNetCoreIdentityDefaults.CookieScheme));
        Assert.Contains(typeof(HostCookieEvents).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(GroundworkIdentityCookieEvents).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_registration_declares_the_v2_identity_units_and_atomic_write_seam()
    {
        var services = RegisterGroundworkIdentity();
        var registry = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry))
            .ImplementationInstance as GroundworkStorageUnitRegistry;
        Assert.NotNull(registry);
        Assert.Equal(17, registry!.Registrations.Count);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IGroundworkStorageSessionSource));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var atomicWrite = scope.ServiceProvider.GetRequiredService<GroundworkIdentityAtomicWrite>();
        Assert.NotNull(atomicWrite);
    }

    private static IServiceCollection RegisterGroundworkIdentity()
    {
        var services = new ServiceCollection();
        return RegisterGroundworkIdentity(services);
    }

    private static IServiceCollection RegisterGroundworkIdentity(IServiceCollection services)
    {
        var registrationType = RequiredType(RegistrationTypeName);
        var method = registrationType.GetMethods()
            .SingleOrDefault(candidate =>
                candidate.Name == RegistrationMethodName &&
                candidate.IsStatic &&
                candidate.GetParameters() is [{ ParameterType: var first }] &&
                first == typeof(IServiceCollection));

        Assert.NotNull(method);
        method!.Invoke(null, [services]);
        return services;
    }

    private sealed class HostCookieEvents : CookieAuthenticationEvents;

    private static Type RequiredType(string typeName)
    {
        var type = Type.GetType($"{typeName}, {AssemblyName}", throwOnError: false);
        Assert.NotNull(type);
        return type!;
    }

    private static void AssertRegistered<TService>(IServiceCollection services) =>
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TService));

    private static void AssertNotRegistered<TService>(IServiceCollection services) =>
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(TService));

    private static void AssertScoped<TService>(IServiceCollection services)
    {
        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == typeof(TService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }
}
