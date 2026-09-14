using CShells.Lifecycle;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Composition;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Authentication;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;

/// <summary>Opt-in ASP.NET Core Identity adapters over the shared Foundation Identity EF authority.</summary>
public static class AspNetCoreIdentityEntityFrameworkCoreServiceCollectionExtensions
{
    private const string AuthorityName = "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore";
    private const string UnmarkedFrameworkStoreAuthority = "UnmarkedAspNetCoreIdentityUserOrRoleStore";

    public static IServiceCollection AddFoundationAspNetCoreIdentityEntityFrameworkCore(
        this IServiceCollection services,
        IdentityIamEntityFrameworkCoreOptions persistenceOptions,
        IdentitySeedOptions? initialAdmin = null,
        bool isDevelopmentOrDemo = false,
        Action<AspNetCoreIdentityOptions>? configureIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(persistenceOptions);

        var registration = AspNetCoreIdentityEntityFrameworkCoreRegistration.Create(
            initialAdmin,
            isDevelopmentOrDemo,
            configureIdentity);
        var existingRegistration = services
            .Where(descriptor => descriptor.ServiceType == typeof(AspNetCoreIdentityEntityFrameworkCoreRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<AspNetCoreIdentityEntityFrameworkCoreRegistration>()
            .SingleOrDefault();
        if (existingRegistration is not null)
        {
            existingRegistration.EnsureEquivalent(registration);
            services.AddIdentityIamEntityFrameworkCore(persistenceOptions);
            return services;
        }

        IdentityIamEntityFrameworkCoreRegistration.EnsureCanAddIdentityIamEntityFrameworkCore(
            services,
            persistenceOptions);
        services.SelectIdentityPersistenceAuthority(
            AuthorityName,
            new IdentityAuthorityCompatibility(UnmarkedFrameworkStoreAuthority, IsUnmarkedFrameworkStoreDescriptor));

        services.AddIdentityIamEntityFrameworkCore(persistenceOptions);
        // Bind the durable provider-neutral contracts before the provider-neutral ASP.NET substrate
        // can install its in-memory fallback descriptors. This keeps A's ownership marker intact.
        services.AddFoundationAspNetCoreIdentity(configureIdentity);
        services.AddIdentityCoreServices(isDevelopmentOrDemo);
        services.AddFoundationIdentityAbstractions();

        services.RemoveAll<IIdentityEmailUniquenessPolicy>();
        services.AddScoped<IIdentityEmailUniquenessPolicy, EfCoreIdentityEmailUniquenessPolicy>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<AuthenticationOptions>, ConfigureEfCoreIdentityDefaultAuthenticationSchemes>());

        ReplaceScoped<IUserStore<AspNetCoreIdentityUser>, EfCoreIdentityUserStore>(services);
        ReplaceScoped<IUserPasswordStore<AspNetCoreIdentityUser>, EfCoreIdentityUserStore>(services);
        ReplaceScoped<IUserSecurityStampStore<AspNetCoreIdentityUser>, EfCoreIdentityUserStore>(services);
        ReplaceScoped<IUserEmailStore<AspNetCoreIdentityUser>, EfCoreIdentityUserStore>(services);
        ReplaceScoped<IUserLockoutStore<AspNetCoreIdentityUser>, EfCoreIdentityUserStore>(services);
        ReplaceScoped<IUserPhoneNumberStore<AspNetCoreIdentityUser>, EfCoreIdentityUserStore>(services);
        ReplaceScoped<IUserTwoFactorStore<AspNetCoreIdentityUser>, EfCoreIdentityUserStore>(services);
        ReplaceScoped<IUserLoginStore<AspNetCoreIdentityUser>, EfCoreIdentityUserStore>(services);
        ReplaceScoped<IUserClaimStore<AspNetCoreIdentityUser>, EfCoreIdentityUserStore>(services);
        ReplaceScoped<IUserRoleStore<AspNetCoreIdentityUser>, EfCoreIdentityUserStore>(services);
        ReplaceScoped<IUserAuthenticationTokenStore<AspNetCoreIdentityUser>, EfCoreIdentityUserStore>(services);
        ReplaceScoped<IUserAuthenticatorKeyStore<AspNetCoreIdentityUser>, EfCoreIdentityUserStore>(services);
        ReplaceScoped<IUserTwoFactorRecoveryCodeStore<AspNetCoreIdentityUser>, EfCoreIdentityUserStore>(services);
        ReplaceScoped<IRoleStore<IdentityRole>, EfCoreIdentityRoleStore>(services);
        ReplaceScoped<IRoleClaimStore<IdentityRole>, EfCoreIdentityRoleStore>(services);
        ReplaceScoped<IUserClaimsPrincipalFactory<AspNetCoreIdentityUser>, EfCoreIdentityClaimsPrincipalFactory>(services);

        services.AddScoped<EfCoreIdentityCookieEvents>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthenticationSessionInvalidator, EfCoreIdentitySessionInvalidator>());
        services.PostConfigure<CookieAuthenticationOptions>(AspNetCoreIdentityDefaults.CookieScheme, options =>
        {
            if (options.EventsType is not null && options.EventsType != typeof(EfCoreIdentityCookieEvents))
                throw new InvalidOperationException($"The '{AspNetCoreIdentityDefaults.CookieScheme}' cookie already uses events type '{options.EventsType.FullName}'. EF Core Identity requires '{typeof(EfCoreIdentityCookieEvents).FullName}'.");
            options.EventsType = typeof(EfCoreIdentityCookieEvents);
        });

        if (initialAdmin is not null)
        {
            services.AddSingleton(Options.Create(initialAdmin));
            services.AddSingleton<EfCoreIdentitySeeder>();
            services.AddHostedService(sp => sp.GetRequiredService<EfCoreIdentitySeeder>());
            services.AddSingleton<IShellInitializer>(sp => sp.GetRequiredService<EfCoreIdentitySeeder>());
        }

        services.AddSingleton(registration);
        return services;
    }

    private static void ReplaceScoped<TService, TImplementation>(IServiceCollection services)
        where TService : class where TImplementation : class, TService
    {
        services.RemoveAll<TService>();
        services.AddScoped<TService, TImplementation>();
    }

    private static bool IsUnmarkedFrameworkStoreDescriptor(ServiceDescriptor descriptor)
    {
        if (descriptor.ServiceType != typeof(IUserStore<AspNetCoreIdentityUser>) && descriptor.ServiceType != typeof(IRoleStore<IdentityRole>))
            return false;
        return descriptor.ImplementationType != typeof(EfCoreIdentityUserStore) && descriptor.ImplementationType != typeof(EfCoreIdentityRoleStore);
    }

    private sealed record AspNetCoreIdentityEntityFrameworkCoreRegistration(
        bool IsDevelopmentOrDemo,
        string? SeedFingerprint,
        string IdentityOptionsFingerprint)
    {
        public static AspNetCoreIdentityEntityFrameworkCoreRegistration Create(
            IdentitySeedOptions? seed,
            bool isDevelopmentOrDemo,
            Action<AspNetCoreIdentityOptions>? configureIdentity) =>
            new(
                isDevelopmentOrDemo,
                seed is null
                    ? null
                    : IdentityEntityFrameworkAdapterSupport.FramedRecordId(
                        seed.UserName,
                        seed.Password,
                        seed.Email,
                        seed.RoleName,
                        seed.IsDevelopmentSeed.ToString()),
                FingerprintIdentityOptions(configureIdentity));

        public void EnsureEquivalent(AspNetCoreIdentityEntityFrameworkCoreRegistration incoming)
        {
            if (IsDevelopmentOrDemo != incoming.IsDevelopmentOrDemo ||
                !string.Equals(SeedFingerprint, incoming.SeedFingerprint, StringComparison.Ordinal) ||
                !string.Equals(IdentityOptionsFingerprint, incoming.IdentityOptionsFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "ASP.NET Core Identity EF persistence is already registered with different framework or seed options.");
            }
        }

        private static string FingerprintIdentityOptions(Action<AspNetCoreIdentityOptions>? configureIdentity)
        {
            var options = new AspNetCoreIdentityOptions();
            configureIdentity?.Invoke(options);
            var values = new List<string?>
            {
                options.ProviderId,
                options.DisplayName,
                options.TenantId,
                options.Enabled.ToString(),
                options.IsDefault.ToString(),
                options.DefaultTenantId,
                options.AllowedReturnUrlOrigins?.Count.ToString()
            };
            if (options.AllowedReturnUrlOrigins is not null)
                values.AddRange(options.AllowedReturnUrlOrigins);
            return IdentityEntityFrameworkAdapterSupport.FramedRecordId(values.ToArray());
        }
    }
}
