using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
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
        services.TryAddScoped<IClaimMappingRuleEvaluator, ClaimMappingRuleEvaluator>();
        services.TryAddSingleton<IPermissionCatalog, DefaultIdentityPermissionCatalog>();
        services.TryAddScoped<ISecurityDefaultGuardEvaluator, SecurityDefaultGuardEvaluator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISecurityDefaultGuard, SigningKeySecurityDefaultGuard>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISecurityDefaultGuard, HttpsMetadataSecurityDefaultGuard>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISecurityDefaultGuard, SecretHashSecurityDefaultGuard>());

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
