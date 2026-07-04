using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Replaces the default in-memory identity stores with Groundwork-backed, durable stores so users, roles,
/// external identities, and tenant memberships survive process restarts. Each store is registered as its
/// own singleton (unlike the in-memory store, which is a single object cast to four interfaces) because
/// the Groundwork stores are stateless bridges over a shared <see cref="Groundwork.Documents.Store.IDocumentStore"/>.
/// </summary>
public static class GroundworkIdentityStoresRegistration
{
    public static IServiceCollection AddGroundworkIdentityStores(this IServiceCollection services)
    {
        services.RemoveAll<IUserStore>();
        services.RemoveAll<IRoleStore>();
        services.RemoveAll<IExternalIdentityStore>();
        services.RemoveAll<ITenantMembershipStore>();

        services.AddSingleton<IUserStore, GroundworkUserStore>();
        services.AddSingleton<IRoleStore, GroundworkRoleStore>();
        services.AddSingleton<IExternalIdentityStore, GroundworkExternalIdentityStore>();
        services.AddSingleton<ITenantMembershipStore, GroundworkTenantMembershipStore>();

        return services;
    }
}
