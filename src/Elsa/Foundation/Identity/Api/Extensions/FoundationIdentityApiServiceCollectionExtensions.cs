using CShells.FastEndpoints.Contracts;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Api.Configurators;
using Elsa.Foundation.Identity.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.Api.Extensions;

public static class FoundationIdentityApiServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationIdentityApi(this IServiceCollection services)
    {
        services.AddFoundationIdentityAbstractions();
        services.TryAddScoped<IAuthSessionService, ClaimsAuthSessionService>();
        services.AddOptions<FoundationIdentityApiOptions>();

        // Teach FastEndpoints to read the permission/role claim types the first-party token carries, so
        // ConfigurePermissions()-secured endpoints accept a bearer issued by the OpenIddict module.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFastEndpointsConfigurator, IdentityClaimTypeFastEndpointsConfigurator>());

        return services;
    }
}
