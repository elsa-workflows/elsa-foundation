using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Disclosure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Disclosure.Extensions;

/// <summary>Explicit keyed scoped registration for catalog contributor authors.</summary>
public static class CatalogProducerServiceCollectionExtensions
{
    public static IServiceCollection AddActivityCatalogDisclosurePolicy<TPolicy>(
        this IServiceCollection services, string policyKey, string bindingSchema)
        where TPolicy : class, IActivityCatalogDisclosurePolicy =>
        CatalogDisclosureServiceRegistration.AddPolicy<TPolicy>(services, policyKey, bindingSchema);

    public static IServiceCollection AddActivityCatalogProducer<TProducer, TAdmissionOwner>(
        this IServiceCollection services, string registrationId, CatalogProducerOwnerScope ownerScope,
        CatalogDisclosurePolicyKey? minimumPolicy = null, params Type[] aliases)
        where TProducer : class
        where TAdmissionOwner : class, IActivityCatalogAdmissionOwner =>
        CatalogDisclosureServiceRegistration.AddProducer<TProducer, TAdmissionOwner>(
            services, registrationId, ownerScope, minimumPolicy, aliases);
}
