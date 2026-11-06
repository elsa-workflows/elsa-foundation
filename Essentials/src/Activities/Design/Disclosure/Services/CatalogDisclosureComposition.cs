using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Events;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Disclosure.Handlers;
using Elsa.Events.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Disclosure.Services;

/// <summary>
/// Validates final DI configuration at startup. This is configuration metadata, separate from
/// the registry; the registry never retains this collection, factories, instances or providers.
/// </summary>
internal sealed class CatalogDisclosureComposition(IServiceCollection services)
{
    public void ValidateReplacements()
    {
        foreach (var contract in new[]
                 {
                     typeof(IActivityCatalogDisclosureEvaluator), typeof(IActivityCatalogAdmission),
                     typeof(ICatalogProducerScope), typeof(IActivityCatalogOwnerScopeAccessor),
                     typeof(IActivityCatalogDisclosureAuthority)
                 })
            RequireSingle(contract, null, null);

        if (services.Count(descriptor => descriptor.ServiceType == typeof(IEventHandler<ActivityCatalogDisclosureInitializing>) &&
                !descriptor.IsKeyedService && descriptor.ImplementationType == typeof(CollectActivityCatalogDisclosureRegistrations)) != 1)
            throw new CatalogDisclosureUnavailableException(
                new InvalidOperationException("Exactly one catalog registration aggregator must be registered."));
    }

    public void ValidateContributions(IEnumerable<CatalogDisclosureRegistration> registrations)
    {
        foreach (var registration in registrations)
        {
            switch (registration)
            {
                case CatalogDisclosurePolicyRegistration policy:
                    RequireSingle(typeof(IActivityCatalogDisclosurePolicy), policy.Key, policy.PolicyType);
                    break;
                case CatalogProducerRegistration producer:
                    RequireSingle(producer.ProducerType, producer.RegistrationId, producer.ProducerType);
                    RequireSingle(typeof(IActivityCatalogAdmissionOwner), producer.RegistrationId, producer.AdmissionOwnerType);
                    foreach (var alias in producer.Aliases.Where(alias => alias != producer.ProducerType))
                        RequireSingle(alias, producer.RegistrationId, null);
                    break;
                default:
                    throw new CatalogDisclosureUnavailableException();
            }
        }
    }

    private void RequireSingle(Type contract, object? key, Type? implementation)
    {
        var matching = services.Where(descriptor => descriptor.ServiceType == contract &&
            (key is null ? !descriptor.IsKeyedService : descriptor.IsKeyedService && Equals(descriptor.ServiceKey, key))).ToArray();
        if (matching.Length != 1 || matching[0].Lifetime != ServiceLifetime.Scoped ||
            implementation is not null && matching[0].KeyedImplementationType != implementation)
            throw new CatalogDisclosureUnavailableException(
                new InvalidOperationException("Exactly one scoped registration is required for each catalog replacement, policy and producer alias."));
    }
}
