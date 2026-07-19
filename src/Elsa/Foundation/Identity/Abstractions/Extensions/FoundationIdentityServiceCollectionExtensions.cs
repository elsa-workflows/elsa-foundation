using CShells.Lifecycle;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Abstractions.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.Abstractions.Extensions;

public static class FoundationIdentityServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationIdentityAbstractions(this IServiceCollection services, Action<FoundationIdentityOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);

        services.AddAuthorizationCore();

        services.TryAddScoped<IAuthenticationProviderResolver, DefaultAuthenticationProviderResolver>();
        services.TryAddScoped<IOwnershipModeProvider, OptionsOwnershipModeProvider>();
        services.TryAddScoped<IEffectiveCapabilitiesResolver, DefaultEffectiveCapabilitiesResolver>();
        services.TryAddScoped<IPermissionEvaluator, ClaimsPermissionEvaluator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, PermissionAuthorizationHandler>());
        services.TryAddSingleton<IPermissionPolicyNameFormatter, PermissionPolicyNameFormatter>();
        services.AddRequirePermissionPolicyProvider();
        services.TryAddScoped<IClaimsNormalizer, DefaultClaimsNormalizer>();
        services.TryAddScoped<IIdentityEmailUniquenessPolicy, OptionsIdentityEmailUniquenessPolicy>();
        services.TryAddScoped<IClaimMappingRuleEvaluator, ClaimMappingRuleEvaluator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionContributor, DefaultIdentityPermissionCatalog>());
        services.TryAddSingleton<IPermissionCatalog, CompositePermissionCatalog>();
        services.TryAddScoped<ISecurityDefaultGuardEvaluator, SecurityDefaultGuardEvaluator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISecurityDefaultGuard, SigningKeySecurityDefaultGuard>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISecurityDefaultGuard, HttpsMetadataSecurityDefaultGuard>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISecurityDefaultGuard, SecretHashSecurityDefaultGuard>());

        return services;
    }

    /// <summary>
    /// Registers a feature-owned <see cref="IPermissionContributor"/> so the feature can additively
    /// contribute permissions to the shared catalog without replacing it (per ADR 0037). Safe to call
    /// from a feature's own service-registration home; the identity abstractions do not need to be
    /// initialized first because <see cref="CompositePermissionCatalog"/> aggregates every registered
    /// contributor.
    /// </summary>
    public static IServiceCollection AddPermissionContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, IPermissionContributor
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionContributor, TContributor>());
        return services;
    }

    /// <summary>
    /// Registers a <see cref="DevelopmentOrDemoGuard"/> for <paramref name="featureName"/> so that a host
    /// which enables that identity feature's <c>IsDevelopmentOrDemo</c> flag outside the Development
    /// environment hard-fails at startup instead of silently booting into the insecure development posture
    /// (ephemeral signing keys + well-known seeded credentials). The guard is exposed under both the
    /// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> and CShells <see cref="IShellInitializer"/> hooks so it fires in plain
    /// hosts/tests and in the shell-composed server alike.
    /// </summary>
    public static IServiceCollection AddIdentityDevelopmentOrDemoGuard(this IServiceCollection services, string featureName)
    {
        services.AddSingleton(sp => new DevelopmentOrDemoGuard(sp, featureName));
        services.AddHostedService(sp => sp.GetRequiredService<DevelopmentOrDemoGuard>());
        services.AddSingleton<IShellInitializer>(sp => sp.GetRequiredService<DevelopmentOrDemoGuard>());
        return services;
    }

    private static void AddRequirePermissionPolicyProvider(this IServiceCollection services)
    {
        var fallbackDescriptor = services.LastOrDefault(x => x.ServiceType == typeof(IAuthorizationPolicyProvider));
        if (fallbackDescriptor?.ImplementationType == typeof(RequirePermissionPolicyProvider))
            return;

        if (fallbackDescriptor is not null)
        {
            services.Remove(fallbackDescriptor);
            services.Add(ServiceDescriptor.Describe(
                typeof(AuthorizationPolicyProviderFallback),
                sp => new AuthorizationPolicyProviderFallback((IAuthorizationPolicyProvider)CreateFromDescriptor(sp, fallbackDescriptor)),
                fallbackDescriptor.Lifetime));
        }

        services.Add(ServiceDescriptor.Describe(
            typeof(IAuthorizationPolicyProvider),
            typeof(RequirePermissionPolicyProvider),
            fallbackDescriptor?.Lifetime ?? ServiceLifetime.Singleton));
    }

    private static object CreateFromDescriptor(IServiceProvider serviceProvider, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is not null)
            return descriptor.ImplementationInstance;

        if (descriptor.ImplementationFactory is not null)
            return descriptor.ImplementationFactory(serviceProvider);

        if (descriptor.ImplementationType is not null)
            return ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType);

        throw new InvalidOperationException($"Unable to create fallback service for '{descriptor.ServiceType}'.");
    }
}
