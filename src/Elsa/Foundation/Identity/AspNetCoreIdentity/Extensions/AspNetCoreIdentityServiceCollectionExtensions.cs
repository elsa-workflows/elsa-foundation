using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;

public static class AspNetCoreIdentityServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationAspNetCoreIdentity(this IServiceCollection services)
    {
        services.AddFoundationIdentityAbstractions();

        services.TryAddSingleton<IUserStore, InMemoryIdentityStore>();
        services.TryAddSingleton<IRoleStore>(sp => (InMemoryIdentityStore)sp.GetRequiredService<IUserStore>());
        services.TryAddSingleton<IExternalIdentityStore>(sp => (InMemoryIdentityStore)sp.GetRequiredService<IUserStore>());
        services.TryAddSingleton<ITenantMembershipStore>(sp => (InMemoryIdentityStore)sp.GetRequiredService<IUserStore>());
        services.TryAddSingleton<IUserManager, AspNetCoreIdentityUserManager>();
        services.TryAddSingleton<IRoleManager, AspNetCoreIdentityRoleManager>();
        services.TryAddScoped<IPrincipalFactory, AspNetCoreIdentityPrincipalFactory>();

        return services;
    }
}
