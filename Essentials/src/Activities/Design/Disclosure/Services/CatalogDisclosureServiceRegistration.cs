using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Disclosure.Services;

/// <summary>Registration mechanics kept out of the public extension methods.</summary>
internal sealed class CatalogDisclosureServiceRegistration
{
    private CatalogDisclosureServiceRegistration() { }

    public static IServiceCollection AddPolicy<TPolicy>(IServiceCollection services, string policyKey, string bindingSchema)
        where TPolicy : class, IActivityCatalogDisclosurePolicy
    {
        ArgumentNullException.ThrowIfNull(services);
        var key = new CatalogDisclosurePolicyKey(policyKey, bindingSchema);
        if (!CatalogDisclosureRegistry.ValidPolicyKey(key))
            throw new ArgumentException("The catalog policy key or schema is invalid.");
        services.AddKeyedScoped<IActivityCatalogDisclosurePolicy, TPolicy>(key);
        services.AddSingleton<IActivityCatalogDisclosureRegistrationSource>(
            new MetadataSource(new CatalogDisclosurePolicyRegistration(key, typeof(TPolicy))));
        return services;
    }

    public static IServiceCollection AddProducer<TProducer, TAdmissionOwner>(IServiceCollection services,
        string registrationId, CatalogProducerOwnerScope ownerScope, CatalogDisclosurePolicyKey? minimumPolicy,
        Type[] aliases)
        where TProducer : class
        where TAdmissionOwner : class, IActivityCatalogAdmissionOwner
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationId);
        ArgumentNullException.ThrowIfNull(aliases);
        var allAliases = aliases.Append(typeof(TProducer)).Distinct().ToArray();
        if (!CatalogDisclosureText.IsValid(registrationId, 128) || ownerScope is not (CatalogProducerOwnerScope.Global or CatalogProducerOwnerScope.CurrentTenant) ||
            minimumPolicy is not null && !CatalogDisclosureRegistry.ValidPolicyKey(minimumPolicy) ||
            allAliases.Any(alias => alias is null || !alias.IsAssignableFrom(typeof(TProducer))))
            throw new ArgumentException("The catalog producer registration is invalid.");

        services.AddKeyedScoped<TProducer>(registrationId);
        foreach (var alias in allAliases.Where(alias => alias != typeof(TProducer)))
            services.AddKeyedScoped(alias, registrationId,
                static (provider, key) => provider.GetRequiredKeyedService<TProducer>(key));
        services.AddKeyedScoped<IActivityCatalogAdmissionOwner, TAdmissionOwner>(registrationId);
        services.AddSingleton<IActivityCatalogDisclosureRegistrationSource>(new MetadataSource(
            new CatalogProducerRegistration(registrationId, typeof(TProducer), typeof(TAdmissionOwner),
                ownerScope, minimumPolicy, allAliases)));
        return services;
    }

    private sealed class MetadataSource(CatalogDisclosureRegistration registration) : IActivityCatalogDisclosureRegistrationSource
    {
        public IEnumerable<CatalogDisclosureRegistration> Read() => [registration];
    }
}
