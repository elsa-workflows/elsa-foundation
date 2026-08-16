using CShells.Lifecycle;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Abstractions.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Abstractions.Extensions;

public static class FoundationIdentityServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationIdentityAbstractions(this IServiceCollection services, Action<FoundationIdentityOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);

        services.AddAuthorizationCore();
        services.AddHttpContextAccessor();
        services.TryAddSingleton(new FoundationIdentityRegistrationState(services));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FoundationIdentityOptions>, FoundationIdentityRegistrationValidator>());
        services.AddOptions<FoundationIdentityOptions>().ValidateOnStart();

        services.TryAddScoped<IAuthenticationProviderResolver, DefaultAuthenticationProviderResolver>();
        services.TryAddScoped<IOwnershipModeProvider, OptionsOwnershipModeProvider>();
        services.TryAddScoped<IEffectiveCapabilitiesResolver, DefaultEffectiveCapabilitiesResolver>();
        EnsureReplacement<IPermissionEvaluator, ClaimsPermissionEvaluator>(services, ServiceLifetime.Scoped);
        EnsureReplacement<IPermissionAuthorizationService, PermissionAuthorizationService>(services, ServiceLifetime.Scoped);
        // The public handler retains obsolete source-compatible constructors. Use an explicit
        // factory so the DI container cannot select an obsolete constructor by accident. A
        // separate marker keeps repeated Foundation registration idempotent because the DI
        // enumerable helper rejects factory descriptors as indistinguishable.
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(FoundationPermissionAuthorizationHandlerRegistration)))
        {
            services.Add(ServiceDescriptor.Describe(
                typeof(IAuthorizationHandler),
                sp => new PermissionAuthorizationHandler(sp.GetRequiredService<IPermissionAuthorizationService>()),
                ServiceLifetime.Scoped));
            services.AddSingleton<FoundationPermissionAuthorizationHandlerRegistration>();
        }
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, PermissionSetAuthorizationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, NormalizedPermissionPrincipalHandler>());
        services.TryAddSingleton<NormalizedPrincipalValidator>();
        services.TryAddSingleton<IPermissionPolicyCodec, PermissionPolicyCodec>();
        EnsureReplacement<IPermissionPolicyNameFormatter, PermissionPolicyNameFormatter>(services, ServiceLifetime.Singleton);
        services.AddRequirePermissionPolicyProvider();
        services.AddPermissionAuthorizationResultHandler();
        services.TryAddScoped<IClaimsNormalizer, DefaultClaimsNormalizer>();
        services.TryAddScoped<IIdentityEmailUniquenessPolicy, OptionsIdentityEmailUniquenessPolicy>();
        services.TryAddScoped<IClaimMappingRuleEvaluator, ClaimMappingRuleEvaluator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionContributor, DefaultIdentityPermissionCatalog>());
        EnsureReplacement<IPermissionCatalog, CompositePermissionCatalog>(services, ServiceLifetime.Singleton);
        services.TryAddScoped<ISecurityDefaultGuardEvaluator, SecurityDefaultGuardEvaluator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISecurityDefaultGuard, SigningKeySecurityDefaultGuard>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISecurityDefaultGuard, HttpsMetadataSecurityDefaultGuard>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISecurityDefaultGuard, SecretHashSecurityDefaultGuard>());

        return services;
    }

    public static IServiceCollection ReplacePermissionEvaluator<TEvaluator>(this IServiceCollection services)
        where TEvaluator : class, IPermissionEvaluator =>
        Replace<IPermissionEvaluator, TEvaluator>(services, ServiceLifetime.Scoped);

    public static IServiceCollection ReplacePermissionAuthorizationService<TService>(this IServiceCollection services)
        where TService : class, IPermissionAuthorizationService =>
        Replace<IPermissionAuthorizationService, TService>(services, ServiceLifetime.Scoped);

    /// <summary>
    /// Registers the default for a replacement contract while preserving one explicit host
    /// registration. A second registration is a composition error, never a last-write-wins choice.
    /// </summary>
    public static IServiceCollection EnsureReplacementContract<TContract, TDefault>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TContract : class
        where TDefault : class, TContract
    {
        if (typeof(TContract).GetCustomAttributes(typeof(ReplacementContractAttribute), inherit: false).Length == 0)
            throw new InvalidOperationException($"Replacement contract '{typeof(TContract).FullName}' must be marked with {nameof(ReplacementContractAttribute)}.");

        var descriptors = services.Where(descriptor => descriptor.ServiceType == typeof(TContract)).ToArray();
        if (descriptors.Length > 1)
            throw new InvalidOperationException(
                $"Replacement contract '{typeof(TContract).FullName}' has conflicting registrations: " +
                FoundationIdentityRegistrationValidator.Describe(descriptors));

        var descriptor = descriptors.SingleOrDefault();
        if (descriptor is null)
        {
            services.Add(ServiceDescriptor.Describe(typeof(TContract), typeof(TDefault), lifetime));
            descriptor = services.Last(x => x.ServiceType == typeof(TContract));
        }

        if (!services.Any(item => item.ServiceType == typeof(FoundationIdentityReplacementRegistration) &&
                                 item.ImplementationInstance is FoundationIdentityReplacementRegistration marker &&
                                 marker.ContractType == typeof(TContract)))
        {
            services.AddSingleton(new FoundationIdentityReplacementRegistration(
                typeof(TContract),
                descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType() ?? typeof(TDefault),
                descriptor));
        }

        return services;
    }

    public static IServiceCollection ReplacePermissionPolicyNameFormatter<TFormatter>(this IServiceCollection services)
        where TFormatter : class, IPermissionPolicyNameFormatter =>
        Replace<IPermissionPolicyNameFormatter, TFormatter>(services, ServiceLifetime.Singleton);

    public static IServiceCollection ReplacePermissionCatalog<TCatalog>(this IServiceCollection services)
        where TCatalog : class, IPermissionCatalog =>
        Replace<IPermissionCatalog, TCatalog>(services, ServiceLifetime.Singleton);

    public static IServiceCollection AddNormalizedAuthenticationType(this IServiceCollection services, string authenticationType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationType);
        services.Configure<FoundationIdentityOptions>(options =>
        {
            var types = options.NormalizedAuthenticationTypes.ToHashSet(StringComparer.Ordinal);
            types.Add(authenticationType);
            options.NormalizedAuthenticationTypes = types;
        });
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
        if (services.Any(descriptor => descriptor.ServiceType == typeof(FoundationIdentityPolicyProviderRegistration)))
            return;

        var fallbackDescriptor = services.LastOrDefault(x => x.ServiceType == typeof(IAuthorizationPolicyProvider));
        if (fallbackDescriptor?.ImplementationType == typeof(RequirePermissionPolicyProvider))
        {
            throw new InvalidOperationException(
                $"Authorization policy provider '{typeof(RequirePermissionPolicyProvider).FullName}' exists without its Foundation registration marker.");
        }

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
        services.AddSingleton(new FoundationIdentityPolicyProviderRegistration());
    }

    private static void AddPermissionAuthorizationResultHandler(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(FoundationIdentityResultHandlerRegistration)))
            return;

        if (services.Any(descriptor => descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler) &&
                                       descriptor.ImplementationType == typeof(PermissionAuthorizationMiddlewareResultHandler)))
        {
            throw new InvalidOperationException(
                $"Authorization result handler '{typeof(PermissionAuthorizationMiddlewareResultHandler).FullName}' exists without its Foundation registration marker.");
        }

        var descriptors = services.Where(descriptor => descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler)).ToArray();
        if (descriptors.Length > 1)
        {
            throw new InvalidOperationException(
                $"Multiple '{typeof(IAuthorizationMiddlewareResultHandler).FullName}' registrations exist before Foundation Identity: " +
                FoundationIdentityRegistrationValidator.Describe(descriptors));
        }

        ServiceLifetime lifetime;
        if (descriptors.Length == 1)
        {
            var fallbackDescriptor = descriptors[0];
            services.Remove(fallbackDescriptor);
            lifetime = fallbackDescriptor.Lifetime;
            services.Add(ServiceDescriptor.Describe(
                typeof(AuthorizationMiddlewareResultHandlerFallback),
                sp => new AuthorizationMiddlewareResultHandlerFallback((IAuthorizationMiddlewareResultHandler)CreateFromDescriptor(sp, fallbackDescriptor)),
                lifetime));
        }
        else
        {
            lifetime = ServiceLifetime.Singleton;
            services.AddSingleton(new AuthorizationMiddlewareResultHandlerFallback(new AuthorizationMiddlewareResultHandler()));
        }

        services.Add(ServiceDescriptor.Describe(
            typeof(IAuthorizationMiddlewareResultHandler),
            typeof(PermissionAuthorizationMiddlewareResultHandler),
            lifetime));
        services.AddSingleton(new FoundationIdentityResultHandlerRegistration());
    }

    private static void EnsureReplacement<TContract, TImplementation>(IServiceCollection services, ServiceLifetime lifetime)
        where TContract : class
        where TImplementation : class, TContract
    {
        var contractType = typeof(TContract);
        var descriptors = services.Where(descriptor => descriptor.ServiceType == contractType).ToArray();
        var markers = GetReplacementMarkers(services, contractType);

        if (descriptors.Length == 0 && markers.Length == 0)
        {
            services.Add(ServiceDescriptor.Describe(contractType, typeof(TImplementation), lifetime));
            services.AddSingleton(new FoundationIdentityReplacementRegistration(
                contractType,
                typeof(TImplementation),
                services.Last(x => x.ServiceType == contractType)));
            return;
        }

        if (descriptors.Length == 1 && markers.Length == 1 &&
            (markers[0].Descriptor is not null
                ? ReferenceEquals(descriptors[0], markers[0].Descriptor)
                : descriptors[0].ImplementationType == markers[0].ImplementationType ||
                  descriptors[0].ImplementationInstance?.GetType() == markers[0].ImplementationType))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Replacement contract '{contractType.FullName}' was registered outside its explicit Replace* method. " +
            $"Descriptors: {FoundationIdentityRegistrationValidator.Describe(descriptors)}.");
    }

    private static IServiceCollection Replace<TContract, TImplementation>(IServiceCollection services, ServiceLifetime lifetime)
        where TContract : class
        where TImplementation : class, TContract
    {
        services.RemoveAll<TContract>();
        RemoveReplacementMarkers(services, typeof(TContract));
        services.Add(ServiceDescriptor.Describe(typeof(TContract), typeof(TImplementation), lifetime));
        services.AddSingleton(new FoundationIdentityReplacementRegistration(
            typeof(TContract),
            typeof(TImplementation),
            services.Last(x => x.ServiceType == typeof(TContract))));
        return services;
    }

    private static FoundationIdentityReplacementRegistration[] GetReplacementMarkers(IServiceCollection services, Type contractType) =>
        services
            .Where(descriptor => descriptor.ServiceType == typeof(FoundationIdentityReplacementRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<FoundationIdentityReplacementRegistration>()
            .Where(marker => marker.ContractType == contractType)
            .ToArray();

    private static void RemoveReplacementMarkers(IServiceCollection services, Type contractType)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            if (services[index].ServiceType == typeof(FoundationIdentityReplacementRegistration) &&
                services[index].ImplementationInstance is FoundationIdentityReplacementRegistration marker &&
                marker.ContractType == contractType)
            {
                services.RemoveAt(index);
            }
        }
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
