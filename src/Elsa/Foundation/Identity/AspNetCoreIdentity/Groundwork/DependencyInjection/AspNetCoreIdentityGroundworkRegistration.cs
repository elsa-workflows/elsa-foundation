using CShells.Lifecycle;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Composition;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Authentication;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Seeding;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ElsaExternalIdentityStore = Elsa.Foundation.Identity.Abstractions.Iam.IExternalIdentityStore;
using ElsaRoleStore = Elsa.Foundation.Identity.Abstractions.Iam.IRoleStore;
using ElsaTenantMembershipStore = Elsa.Foundation.Identity.Abstractions.Iam.ITenantMembershipStore;
using ElsaUserStore = Elsa.Foundation.Identity.Abstractions.Iam.IUserStore;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection;

public static class AspNetCoreIdentityGroundworkRegistration
{
    private const string GroundworkAuthority = "FoundationIdentityAspNetCoreIdentityGroundwork";
    private const string FrozenEntityFrameworkAuthority = "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore";
    private const string UnmarkedFrameworkStoreAuthority = "UnmarkedAspNetCoreIdentityUserOrRoleStore";

    public static IServiceCollection AddFoundationAspNetCoreIdentityGroundwork(this IServiceCollection services) =>
        services.AddFoundationAspNetCoreIdentityGroundwork(initialAdmin: null);

    public static IServiceCollection AddFoundationAspNetCoreIdentityGroundwork(
        this IServiceCollection services,
        IdentitySeedOptions? initialAdmin) =>
        services.AddFoundationAspNetCoreIdentityGroundwork(initialAdmin, isDevelopmentOrDemo: false);

    public static IServiceCollection AddFoundationAspNetCoreIdentityGroundwork(
        this IServiceCollection services,
        IdentitySeedOptions? initialAdmin,
        bool isDevelopmentOrDemo)
    {
        services.SelectIdentityPersistenceAuthority(
            GroundworkAuthority,
            new IdentityAuthorityCompatibility(
                FrozenEntityFrameworkAuthority,
                IsFrozenEntityFrameworkIdentityDescriptor),
            new IdentityAuthorityCompatibility(
                UnmarkedFrameworkStoreAuthority,
                IsUnmarkedFrameworkStoreDescriptor));

        services.AddFoundationAspNetCoreIdentity();
        services.AddIdentityCoreServices(isDevelopmentOrDemo);
        services.AddFoundationIdentityAbstractions();
        services.AddGroundworkIdentityStores();
        services.RemoveAll<IIdentityEmailUniquenessPolicy>();
        services.AddScoped<IIdentityEmailUniquenessPolicy, AspNetCoreIdentityEmailUniquenessPolicy>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<AuthenticationOptions>, ConfigureAspNetCoreIdentityDefaultAuthenticationSchemes>());

        services.ReplaceScoped<IUserStore<AspNetCoreIdentityUser>, GroundworkIdentityUserStore>();
        services.ReplaceScoped<IUserPasswordStore<AspNetCoreIdentityUser>, GroundworkIdentityUserStore>();
        services.ReplaceScoped<IUserSecurityStampStore<AspNetCoreIdentityUser>, GroundworkIdentityUserStore>();
        services.ReplaceScoped<IUserEmailStore<AspNetCoreIdentityUser>, GroundworkIdentityUserStore>();
        services.ReplaceScoped<IUserLockoutStore<AspNetCoreIdentityUser>, GroundworkIdentityUserStore>();
        services.ReplaceScoped<IUserPhoneNumberStore<AspNetCoreIdentityUser>, GroundworkIdentityUserStore>();
        services.ReplaceScoped<IUserTwoFactorStore<AspNetCoreIdentityUser>, GroundworkIdentityUserStore>();
        services.ReplaceScoped<IUserLoginStore<AspNetCoreIdentityUser>, GroundworkIdentityUserStore>();
        services.ReplaceScoped<IUserClaimStore<AspNetCoreIdentityUser>, GroundworkIdentityUserStore>();
        services.ReplaceScoped<IUserRoleStore<AspNetCoreIdentityUser>, GroundworkIdentityUserStore>();
        services.ReplaceScoped<IUserAuthenticationTokenStore<AspNetCoreIdentityUser>, GroundworkIdentityUserStore>();
        services.ReplaceScoped<IUserAuthenticatorKeyStore<AspNetCoreIdentityUser>, GroundworkIdentityUserStore>();
        services.ReplaceScoped<IUserTwoFactorRecoveryCodeStore<AspNetCoreIdentityUser>, GroundworkIdentityUserStore>();
        services.ReplaceScoped<IRoleStore<IdentityRole>, GroundworkIdentityRoleStore>();
        services.ReplaceScoped<IRoleClaimStore<IdentityRole>, GroundworkIdentityRoleStore>();
        services.ReplaceScoped<IUserClaimsPrincipalFactory<AspNetCoreIdentityUser>, GroundworkIdentityClaimsPrincipalFactory>();
        services.AddScoped<GroundworkIdentityCookieEvents>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IAuthenticationSessionInvalidator, GroundworkIdentitySessionInvalidator>());
        services.PostConfigure<CookieAuthenticationOptions>(
            AspNetCoreIdentityDefaults.CookieScheme,
            options =>
            {
                if (options.EventsType is not null && options.EventsType != typeof(GroundworkIdentityCookieEvents))
                {
                    throw new InvalidOperationException(
                        $"The '{AspNetCoreIdentityDefaults.CookieScheme}' cookie already uses events type " +
                        $"'{options.EventsType.FullName}'. Groundwork Identity requires " +
                        $"'{typeof(GroundworkIdentityCookieEvents).FullName}' for immediate tenant-bound session invalidation.");
                }

                options.EventsType = typeof(GroundworkIdentityCookieEvents);
            });

        services.ReplaceScoped<ElsaUserStore, Persistence.Groundwork.Stores.GroundworkUserStore>();
        services.ReplaceScoped<ElsaRoleStore, Persistence.Groundwork.Stores.GroundworkRoleStore>();
        services.ReplaceScoped<ElsaExternalIdentityStore, Persistence.Groundwork.Stores.GroundworkExternalIdentityStore>();
        services.ReplaceScoped<ElsaTenantMembershipStore, Persistence.Groundwork.Stores.GroundworkTenantMembershipStore>();

        if (initialAdmin is not null)
        {
            services.AddSingleton(Options.Create(initialAdmin));
            services.AddSingleton<GroundworkIdentitySeeder>();
            services.AddHostedService(sp => sp.GetRequiredService<GroundworkIdentitySeeder>());
            services.AddSingleton<IShellInitializer>(sp => sp.GetRequiredService<GroundworkIdentitySeeder>());
        }

        return services;
    }

    private static void ReplaceScoped<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.RemoveAll<TService>();
        services.AddScoped<TService, TImplementation>();
    }

    private static bool IsFrozenEntityFrameworkIdentityDescriptor(ServiceDescriptor descriptor) =>
        IsFrozenEntityFrameworkIdentityType(descriptor.ServiceType) ||
        IsFrozenEntityFrameworkIdentityType(descriptor.ImplementationType) ||
        IsFrozenEntityFrameworkIdentityType(descriptor.ImplementationInstance?.GetType());

    private static bool IsUnmarkedFrameworkStoreDescriptor(ServiceDescriptor descriptor)
    {
        var isFrameworkStore = descriptor.ServiceType == typeof(IUserStore<AspNetCoreIdentityUser>) ||
                               descriptor.ServiceType == typeof(IRoleStore<IdentityRole>);
        if (!isFrameworkStore)
            return false;

        return !IsGroundworkIdentityStoreType(descriptor.ImplementationType) &&
               !IsGroundworkIdentityStoreType(descriptor.ImplementationInstance?.GetType()) &&
               !IsFrozenEntityFrameworkIdentityType(descriptor.ImplementationType) &&
               !IsFrozenEntityFrameworkIdentityType(descriptor.ImplementationInstance?.GetType());
    }

    private static bool IsFrozenEntityFrameworkIdentityType(Type? type) =>
        type?.FullName?.StartsWith(
            "Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.",
            StringComparison.Ordinal) == true;

    private static bool IsGroundworkIdentityStoreType(Type? type) =>
        type == typeof(GroundworkIdentityUserStore) || type == typeof(GroundworkIdentityRoleStore);
}
